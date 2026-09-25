using Filomena.Core.Shadow;
using Filomena.Core.Outbound;
using Filomena.OpenClaw.Observation;
using System.Collections.ObjectModel;

namespace Filomena.App;

/// <summary>Host-owned coordination for independently persisted native conversations.</summary>
public sealed class NativeConversationSupervisor : IAsyncDisposable
{
    private readonly object workersLock = new();
    private readonly Dictionary<string, NativeConversationWorker> workers = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource shutdown = new();
    private bool disposed;

    public NativeConversationWorker StartSession(ShadowSessionBinding binding, string stateRoot,
        Func<string, CancellationToken, Task> runSession, string? existingGroupStateDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(runSession);
        return StartSessionCore(binding, stateRoot, existingGroupStateDirectory, null,
            (directory, _, token) => runSession(directory, token));
    }

    public NativeConversationWorker StartMemberDmSession(ValidatedMemberDmScopeFacts facts,
        ShadowSessionBinding binding, string stateRoot,
        Func<string, MemberDmBoundTextBinding, CancellationToken, Task> runSession)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(runSession);
        var memberBinding = facts.ToBoundTextBinding();
        if (binding.Kind != Filomena.Core.Flows.Sessions.SessionEntryKind.Dm
            || binding.Session.AccountId != facts.AccountId
            || binding.Session.NativeConversationId != facts.NativeConversationId)
            throw new InvalidDataException("member_dm_worker_binding_mismatch");
        return StartSessionCore(binding, stateRoot, null, memberBinding,
            (directory, _, token) => runSession(directory, memberBinding, token));
    }

    public async Task<NativeConversationWorker> RestartMemberDmSessionAsync(
        NativeConversationWorker currentWorker,
        Func<CancellationToken, Task<ValidatedMemberDmScopeFacts>> revalidateFreshLease,
        string stateRoot,
        Func<string, MemberDmBoundTextBinding, CancellationToken, Task> runSession,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(currentWorker);
        ArgumentNullException.ThrowIfNull(revalidateFreshLease);
        ArgumentNullException.ThrowIfNull(runSession);
        if (currentWorker.MemberDmBinding is not { } previousBinding)
            throw new InvalidDataException("member_dm_worker_binding_missing");
        await StopSessionAsync(currentWorker.Binding, cancellationToken);
        var replacementFacts = await revalidateFreshLease(cancellationToken);
        ArgumentNullException.ThrowIfNull(replacementFacts);
        var replacement = await AcceptRevalidatedMemberDmLeaseAsync(previousBinding,
            replacementFacts.ToBoundTextBinding(), currentWorker.Completion, cancellationToken);
        var next = StartMemberDmSession(replacementFacts, currentWorker.Binding, stateRoot, runSession);
        if (next.StateDirectory != currentWorker.StateDirectory || next.MemberDmBinding != replacement)
        {
            await StopSessionAsync(currentWorker.Binding, cancellationToken);
            throw new InvalidDataException("member_dm_state_path_changed");
        }
        return next;
    }

    private NativeConversationWorker StartSessionCore(ShadowSessionBinding binding, string stateRoot,
        string? existingGroupStateDirectory, MemberDmBoundTextBinding? memberDmBinding,
        Func<string, MemberDmBoundTextBinding?, CancellationToken, Task> runSession)
    {
        var stateDirectory = ResolveSessionDirectory(stateRoot, binding, existingGroupStateDirectory);
        var reference = binding.OpaqueRef;
        lock (workersLock)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (workers.TryGetValue(reference, out var existing))
            {
                if (!existing.Binding.HasSameAuthority(binding))
                    throw new InvalidDataException("native_session_binding_conflict");
                if (existing.MemberDmBinding != memberDmBinding)
                    throw new InvalidOperationException("member_dm_worker_must_drain_before_rebind");
                return existing;
            }
            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token);
            var worker = new NativeConversationWorker(binding, stateDirectory, cancellation, memberDmBinding);
            workers.Add(reference, worker);
            worker.SetCompletion(RunWorkerAsync(worker, runSession));
            return worker;
        }
    }

    public async Task StopSessionAsync(ShadowSessionBinding binding, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(binding);
        NativeConversationWorker? worker;
        lock (workersLock) workers.TryGetValue(binding.OpaqueRef, out worker);
        if (worker is null) return;
        worker.Cancel();
        await worker.Completion.WaitAsync(cancellationToken);
        lock (workersLock)
        {
            if (workers.TryGetValue(binding.OpaqueRef, out var current) && ReferenceEquals(current, worker))
                workers.Remove(binding.OpaqueRef);
        }
        worker.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        NativeConversationWorker[] active;
        lock (workersLock)
        {
            if (disposed) return;
            disposed = true;
            shutdown.Cancel();
            active = workers.Values.ToArray();
        }
        foreach (var worker in active) worker.Cancel();
        await Task.WhenAll(active.Select(worker => worker.Completion));
        lock (workersLock) workers.Clear();
        foreach (var worker in active) worker.Dispose();
        shutdown.Dispose();
    }

    private static async Task<NativeConversationWorkerResult> RunWorkerAsync(
        NativeConversationWorker worker, Func<string, MemberDmBoundTextBinding?, CancellationToken, Task> runSession)
    {
        await Task.Yield();
        try
        {
            await runSession(worker.StateDirectory, worker.MemberDmBinding, worker.CancellationToken);
            return new(worker.CancellationToken.IsCancellationRequested ? "stopped" : "completed", null);
        }
        catch (OperationCanceledException) when (worker.CancellationToken.IsCancellationRequested)
        { return new("stopped", null); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        { return new("failed", exception.GetType().Name); }
    }

    /// <summary>Fully validated host/native facts used to create this scope's separate live proof.</summary>
    public sealed class ValidatedMemberDmScopeFacts
    {
        internal ValidatedMemberDmScopeFacts(string pinnedProjectRevision, string accountId,
            string sourceGroupJid, string nativeConversationId, IReadOnlyList<string> nativeAliases,
            string membershipLeaseRef, string scopeRef, string readerEndpoint, string nativeApprovedRevision,
            string routeBindingRef, NativeObservationCursor continueAfter,
            IReadOnlyDictionary<string, bool> observationCapabilities)
        {
            PinnedProjectRevision = pinnedProjectRevision;
            AccountId = accountId;
            SourceGroupJid = sourceGroupJid;
            NativeConversationId = nativeConversationId;
            NativeAliases = Array.AsReadOnly(nativeAliases.ToArray());
            MembershipLeaseRef = membershipLeaseRef;
            ScopeRef = scopeRef;
            ReaderEndpoint = readerEndpoint;
            NativeApprovedRevision = nativeApprovedRevision;
            RouteBindingRef = routeBindingRef;
            ContinueAfter = continueAfter;
            ObservationCapabilities = new ReadOnlyDictionary<string, bool>(
                observationCapabilities.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));
        }

        public string PinnedProjectRevision { get; }
        public string AccountId { get; }
        public string SourceGroupJid { get; }
        public string NativeConversationId { get; }
        public IReadOnlyList<string> NativeAliases { get; }
        public string MembershipLeaseRef { get; }
        public string ScopeRef { get; }
        public string ReaderEndpoint { get; }
        public string NativeApprovedRevision { get; }
        public string RouteBindingRef { get; }
        public NativeObservationCursor ContinueAfter { get; }
        public IReadOnlyDictionary<string, bool> ObservationCapabilities { get; }

        public MemberDmBoundTextBinding ToBoundTextBinding() => new(NativeConversationId, ScopeRef,
            RouteBindingRef, NativeApprovedRevision, MembershipLeaseRef);
    }

    public static async Task<NativeMemberDmDiscoverySnapshot> ReadMemberDmDiscoveryAsync(
        NativeObservationReader authenticatedGroupReader, string pinnedAccountId,
        string pinnedGroupJid, string authenticatedGroupSessionRef, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authenticatedGroupReader);
        var response = await authenticatedGroupReader.ReadMemberDmDiscoveryAsync(cancellationToken);
        return NativeMemberDmDiscoveryValidator.Parse(response, pinnedAccountId, pinnedGroupJid,
            authenticatedGroupSessionRef);
    }

    /// <summary>
    /// Rechecks the exact scope against the authenticated group discovery and the
    /// DM reader's own inactive-dispatch, effective-proof, isolation-gate and capability status.
    /// </summary>
    public static async Task<ValidatedMemberDmScopeFacts> ValidateMemberDmScopeAsync(
        NativeObservationReader groupReader,
        NativeObservationReader dmReader,
        NativeMemberDmDiscoverySnapshot initialDiscovery,
        NativeMemberDmDiscoveredScope selectedScope,
        string pinnedAccountId,
        string pinnedGroupJid,
        string authenticatedGroupSessionRef,
        string pinnedProjectRevision,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(groupReader);
        ArgumentNullException.ThrowIfNull(dmReader);
        ArgumentNullException.ThrowIfNull(initialDiscovery);
        ArgumentNullException.ThrowIfNull(selectedScope);
        if (initialDiscovery.Enrollment != "enabled" || !selectedScope.IsRunnable
            || !initialDiscovery.Scopes.Any(scope => SameScope(scope, selectedScope))
            || string.IsNullOrWhiteSpace(pinnedProjectRevision))
            throw new InvalidDataException("member_dm_scope_not_authorized");

        await EnsureCurrentMembershipLeaseAsync(groupReader, selectedScope, pinnedAccountId, pinnedGroupJid,
            authenticatedGroupSessionRef, cancellationToken);
        var transportSession = dmReader.BoundTransportSession;
        if (transportSession.AccountId != pinnedAccountId
            || transportSession.NativeConversationId != selectedScope.NativeConversationId
            || dmReader.BoundSessionRef != selectedScope.ScopeRef
            || !SameReaderEndpoint(dmReader.BoundEndpoint, selectedScope.ReaderEndpoint))
            throw new InvalidDataException("member_dm_reader_binding_mismatch");

        var inspection = await dmReader.InspectAsync(cancellationToken);
        if (inspection.Source.Version != "inbound-observation:v1"
            || inspection.Source.SessionRef != selectedScope.ScopeRef
            || inspection.Source.DispatchStatus != "inactive"
            || inspection.Revision != selectedScope.EffectiveConfigRevision
            || inspection.RouteBindingRef != selectedScope.RouteBindingRef
            || inspection.ReceiverEpoch != selectedScope.Epoch
            || NativeObservationPreflight.RequiredCapabilities.Any(capability =>
                !inspection.Source.Capabilities.GetValueOrDefault(capability)))
            throw new InvalidDataException("member_dm_reader_not_ready");

        var approval = await dmReader.PreflightAsync(cancellationToken);
        if (approval.Revision != selectedScope.EffectiveConfigRevision)
            throw new NativeObservationException("preflight_stale");
        var provisioning = await dmReader.ProvisioningChecksAsync(false, cancellationToken,
            selectedScope.EffectiveConfigRevision);
        if (provisioning.Any(check => !check.Passed))
            throw new NativeObservationException("capture_or_bootstrap_not_ready");

        // Membership may have changed while the DM reader was being proved. A new
        // generation is not adopted here; the caller must explicitly revalidate it.
        await EnsureCurrentMembershipLeaseAsync(groupReader, selectedScope, pinnedAccountId, pinnedGroupJid,
            authenticatedGroupSessionRef, cancellationToken);
        var cursor = selectedScope.InitialCursor!;
        return new ValidatedMemberDmScopeFacts(pinnedProjectRevision, pinnedAccountId, pinnedGroupJid,
            selectedScope.NativeConversationId!, selectedScope.NativeAliases,
            selectedScope.MembershipLeaseRef!, selectedScope.ScopeRef!, selectedScope.ReaderEndpoint!,
            selectedScope.EffectiveConfigRevision!, selectedScope.RouteBindingRef!,
            new(cursor.Version, cursor.Epoch, cursor.SessionRef, cursor.Sequence),
            inspection.Source.Capabilities.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));
    }

    /// <summary>
    /// Accepts only a host-validated replacement lease after the previous session
    /// worker has fully drained. The scope, native peer and dispatch route remain pinned.
    /// </summary>
    public static async Task<MemberDmBoundTextBinding> AcceptRevalidatedMemberDmLeaseAsync(
        MemberDmBoundTextBinding current,
        MemberDmBoundTextBinding replacement,
        Task previousWorkerDrained,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(replacement);
        ArgumentNullException.ThrowIfNull(previousWorkerDrained);
        await previousWorkerDrained.WaitAsync(cancellationToken);

        if (!System.Text.RegularExpressions.Regex.IsMatch(current.NativeConversationId, @"\A[0-9]+@s\.whatsapp\.net\z")
            || replacement.NativeConversationId != current.NativeConversationId
            || replacement.ScopeRef != current.ScopeRef
            || replacement.RouteBindingRef != current.RouteBindingRef
            || replacement.EffectiveConfigRevision != current.EffectiveConfigRevision
            || string.IsNullOrWhiteSpace(current.MembershipLeaseRef)
            || string.IsNullOrWhiteSpace(replacement.MembershipLeaseRef)
            || replacement.MembershipLeaseRef == current.MembershipLeaseRef)
            throw new InvalidDataException("member_dm_lease_replacement_mismatch");

        return replacement;
    }

    /// <summary>Captures one validated membership generation for pre-poll and pre-admission checks.</summary>
    public static Func<CancellationToken, Task> CreateMemberDmExecutionGenerationGuard(
        NativeObservationReader authenticatedGroupReader, ValidatedMemberDmScopeFacts facts)
    {
        ArgumentNullException.ThrowIfNull(authenticatedGroupReader);
        ArgumentNullException.ThrowIfNull(facts);
        var groupSession = authenticatedGroupReader.BoundTransportSession;
        if (authenticatedGroupReader.BoundKind != Filomena.Core.Flows.Sessions.SessionEntryKind.Group
            || groupSession.AccountId != facts.AccountId
            || groupSession.NativeConversationId != facts.SourceGroupJid
            || string.IsNullOrWhiteSpace(authenticatedGroupReader.BoundSessionRef))
            throw new InvalidDataException("member_dm_execution_guard_group_binding_mismatch");

        var capturedScope = new NativeMemberDmDiscoveredScope("ready", null, facts.ScopeRef,
            facts.NativeConversationId, facts.NativeAliases, facts.MembershipLeaseRef, null,
            facts.ReaderEndpoint, facts.RouteBindingRef, facts.NativeApprovedRevision,
            facts.ContinueAfter.Epoch, new NativeMemberDmCursor(facts.ContinueAfter.Version,
                facts.ContinueAfter.Epoch, facts.ContinueAfter.SessionRef, facts.ContinueAfter.Sequence));
        return cancellationToken => EnsureCurrentMembershipLeaseAsync(authenticatedGroupReader, capturedScope,
            facts.AccountId, facts.SourceGroupJid, authenticatedGroupReader.BoundSessionRef, cancellationToken);
    }

    private static async Task EnsureCurrentMembershipLeaseAsync(NativeObservationReader groupReader,
        NativeMemberDmDiscoveredScope selectedScope, string accountId, string groupJid,
        string groupSessionRef, CancellationToken cancellationToken)
    {
        var response = await groupReader.ReadMemberDmDiscoveryAsync(cancellationToken);
        var current = NativeMemberDmDiscoveryValidator.Parse(response, accountId, groupJid, groupSessionRef);
        if (current.Enrollment != "enabled"
            || !current.Scopes.Any(scope => SameScope(scope, selectedScope)))
            throw new NativeObservationException("member_dm_membership_lease_stale");
    }

    private static bool SameReaderEndpoint(Uri endpoint, string? expected) =>
        Uri.TryCreate(expected, UriKind.Absolute, out var expectedUri)
        && endpoint.GetLeftPart(UriPartial.Authority) + endpoint.AbsolutePath.TrimEnd('/')
            == expectedUri.GetLeftPart(UriPartial.Authority) + expectedUri.AbsolutePath.TrimEnd('/');

    private static bool SameScope(NativeMemberDmDiscoveredScope left, NativeMemberDmDiscoveredScope right) =>
        left.IsRunnable && right.IsRunnable
        && left.ScopeRef == right.ScopeRef
        && left.NativeConversationId == right.NativeConversationId
        && left.MembershipLeaseRef == right.MembershipLeaseRef
        && left.Epoch == right.Epoch
        && left.EffectiveConfigRevision == right.EffectiveConfigRevision
        && left.RouteBindingRef == right.RouteBindingRef
        && left.ReaderEndpoint == right.ReaderEndpoint
        && left.InitialCursor == right.InitialCursor
        && left.NativeAliases.SequenceEqual(right.NativeAliases, StringComparer.Ordinal);

    public sealed record NativeConversationWorkerResult(string Status, string? FailureCode);

    public sealed class NativeConversationWorker : IDisposable
    {
        private readonly CancellationTokenSource cancellation;
        private Task<NativeConversationWorkerResult> completion =
            Task.FromResult(new NativeConversationWorkerResult("pending", null));

        internal NativeConversationWorker(ShadowSessionBinding binding, string stateDirectory,
            CancellationTokenSource cancellation, MemberDmBoundTextBinding? memberDmBinding)
        { Binding = binding; StateDirectory = stateDirectory; this.cancellation = cancellation; MemberDmBinding = memberDmBinding; }

        public ShadowSessionBinding Binding { get; }
        public string StateDirectory { get; }
        public MemberDmBoundTextBinding? MemberDmBinding { get; }
        internal CancellationToken CancellationToken => cancellation.Token;
        public Task<NativeConversationWorkerResult> Completion => completion;
        internal void SetCompletion(Task<NativeConversationWorkerResult> value) => completion = value;
        internal void Cancel() { if (!cancellation.IsCancellationRequested) cancellation.Cancel(); }
        public void Dispose() => cancellation.Dispose();
    }

    /// <summary>
    /// Keeps the host's existing group checkpoint in place; dynamic DM scopes use
    /// isolated opaque directories under the host-selected state root.
    /// </summary>
    public static string ResolveSessionDirectory(
        string stateRoot,
        ShadowSessionBinding binding,
        string? existingGroupStateDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(binding);

        if (binding.Kind == Filomena.Core.Flows.Sessions.SessionEntryKind.Group)
        {
            if (string.IsNullOrWhiteSpace(existingGroupStateDirectory))
                throw new InvalidOperationException("existing_group_state_directory_required");
            var groupDirectory = Path.GetFullPath(existingGroupStateDirectory);
            if (!Directory.Exists(groupDirectory))
                throw new DirectoryNotFoundException("existing_group_state_directory_not_found");
            return groupDirectory;
        }

        return PrepareSessionDirectory(stateRoot, binding);
    }

    /// <summary>
    /// Creates a stable, opaque per-binding directory below the host-selected state root.
    /// The root itself must be private to the running host account.
    /// </summary>
    public static string PrepareSessionDirectory(string stateRoot, ShadowSessionBinding binding)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stateRoot);
        ArgumentNullException.ThrowIfNull(binding);

        var reference = binding.OpaqueRef;
        if (reference.Length != 64 || reference.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidDataException("invalid_native_session_reference");

        var root = Path.GetFullPath(stateRoot);
        Directory.CreateDirectory(root);
        var sessionDirectory = Path.Combine(root, reference);
        if (Directory.Exists(sessionDirectory)
            && (File.GetAttributes(sessionDirectory) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("native_session_directory_must_not_be_linked");
        Directory.CreateDirectory(sessionDirectory);
        return sessionDirectory;
    }
}
