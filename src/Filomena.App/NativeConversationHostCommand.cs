using System.Text.Json;
using System.Text.Json.Serialization;
using Filomena.Core.Flows.Sessions;
using Filomena.Core.Identity;
using Filomena.Core.Outbound;
using Filomena.Core.Shadow;
using Filomena.OpenClaw.Observation;

namespace Filomena.App;

public sealed record NativeConversationHostConfig(
    string GroupLiveConfigFile,
    string ManagedMemberDmTemplateFile,
    string MemberDmStateRoot,
    string AccountId,
    string SourceGroupJid,
    string PinnedProjectRevision,
    string ForeignControlNamespace,
    string ForeignControlCanary,
    int DiscoveryIntervalMilliseconds = 5000);

public sealed record NativeMemberDmMemoryReadinessTemplate(string ForeignNamespace, string ForeignCanary);

/// <summary>External boundaries for the CLI host; production defaults use the existing readers and commands.</summary>
public sealed record NativeConversationHostDependencies(
    Func<Uri, HttpClient> ReaderHttpClientFactory,
    Func<string, string, TextReader, TextWriter, CancellationToken, Task<int>> RunGroupSession,
    Func<string, string, NativeMemberDmExecutionContext, TextReader, TextWriter, CancellationToken, Task<int>> RunMemberDmSession);

/// <summary>Operational CLI owner for the existing group worker and discovered member-DM workers.</summary>
public static class NativeConversationHostCommand
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
    };

    public static async Task<int> RunAsync(string[] args, TextReader input, TextWriter output,
        CancellationToken cancellationToken, NativeConversationHostDependencies? dependencies = null)
    {
        if (args.Length != 2 || args[0] != "--native-conversation-host")
            return await FailAsync(output, "invalid_host_configuration");
        dependencies ??= ProductionDependencies();

        HostSetup setup;
        HttpClient groupClient;
        NativeObservationReader groupReader;
        try
        {
            setup = await LoadSetupAsync(args[1], cancellationToken);
            groupClient = dependencies.ReaderHttpClientFactory(new Uri(setup.GroupNative.ObservationEndpoint));
            groupReader = new NativeObservationReader(groupClient,
                new Uri(setup.GroupNative.ObservationEndpoint), setup.ReaderToken, setup.GroupBinding,
                setup.GroupNative.ObservationSessionRef, setup.GroupManual.SelfActorIds[0], setup.GroupManual.PeerActorId,
                setup.GroupManual.Session, setup.GroupNative.GroupParticipants, setup.GroupNative.SelfMentionIdentity);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return await FailAsync(output, exception is NativeObservationException observation
                ? observation.Code : exception is InvalidDataException { Message: "member_dm_template_binding_mismatch" }
                    ? "member_dm_template_binding_mismatch" : "invalid_host_configuration");
        }

        await using var supervisor = new NativeConversationSupervisor();
        var workers = new Dictionary<string, ManagedWorker>(StringComparer.Ordinal);
        var retiredMemberBindings = new Dictionary<string, MemberDmBoundTextBinding>(StringComparer.Ordinal);
        var groupWorker = supervisor.StartSession(setup.GroupBinding, setup.MemberDmStateRoot,
            async (stateDirectory, token) =>
            {
                if (stateDirectory != setup.GroupStateDirectory)
                    throw new InvalidDataException("existing_group_state_path_changed");
                var code = await dependencies.RunGroupSession(setup.GroupLiveConfigPath, stateDirectory,
                    new PollCommandReader(setup.PollInterval), output, token);
                if (code != 0) throw new InvalidOperationException("group_worker_failed");
            }, setup.GroupStateDirectory);

        async Task StopMemberAsync(string peer, CancellationToken stopToken)
        {
            if (!workers.Remove(peer, out var current)) return;
            if (current.Worker.MemberDmBinding is { } captured)
                retiredMemberBindings[peer] = captured;
            await supervisor.StopSessionAsync(current.Binding, stopToken);
        }

        async Task StopAllMembersAsync(CancellationToken stopToken)
        {
            foreach (var peer in workers.Keys.ToArray()) await StopMemberAsync(peer, stopToken);
        }

        async Task<NativeConversationSupervisor.ValidatedMemberDmScopeFacts> ValidateScopeAsync(
            NativeMemberDmDiscoverySnapshot snapshot, NativeMemberDmDiscoveredScope scope, CancellationToken token)
        {
            var nativeId = scope.NativeConversationId ?? throw new InvalidDataException("member_dm_scope_missing_peer");
            var session = setup.GroupManual.Session with { NativeConversationId = nativeId };
            var peerActor = "whatsapp:" + nativeId;
            var binding = new ShadowSessionBinding(session, FilomenaIdentity.WhatsAppConversationNamespace(nativeId),
                setup.GroupManual.SelfActorIds, [peerActor]);
            var http = dependencies.ReaderHttpClientFactory(new Uri(scope.ReaderEndpoint!));
            using (http)
            {
                var reader = new NativeObservationReader(http, new Uri(scope.ReaderEndpoint!), setup.ReaderToken,
                    binding, scope.ScopeRef!, setup.GroupManual.SelfActorIds[0], peerActor, session,
                    trustedManagedMemberDmAliases: scope.NativeAliases);
                return await NativeConversationSupervisor.ValidateMemberDmScopeAsync(groupReader, reader,
                    snapshot, scope, setup.HostConfig.AccountId, setup.HostConfig.SourceGroupJid,
                    setup.GroupNative.ObservationSessionRef, setup.HostConfig.PinnedProjectRevision, token);
            }
        }

        NativeConversationSupervisor.NativeConversationWorker StartMember(
            NativeConversationSupervisor.ValidatedMemberDmScopeFacts facts)
        {
            var binding = BuildBinding(setup.GroupManual, facts);
            return supervisor.StartMemberDmSession(facts, binding, setup.MemberDmStateRoot, groupReader,
                async (stateDirectory, context, token) =>
                {
                    var livePath = await WriteMemberScopeFilesAsync(setup, facts, stateDirectory, token);
                    var code = await dependencies.RunMemberDmSession(livePath, stateDirectory, context,
                        new PollCommandReader(setup.PollInterval), output, token);
                    if (code != 0) throw new InvalidOperationException("member_dm_worker_failed");
                });
        }

        async Task RefreshAsync(CancellationToken token)
        {
            if (groupWorker.Completion.IsCompleted)
                throw new InvalidOperationException("group_worker_stopped");
            NativeMemberDmDiscoverySnapshot snapshot;
            try
            {
                var wire = await groupReader.ReadMemberDmDiscoveryAsync(token);
                snapshot = NativeMemberDmDiscoveryValidator.Parse(wire, setup.HostConfig.AccountId,
                    setup.HostConfig.SourceGroupJid, setup.GroupNative.ObservationSessionRef);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                await StopAllMembersAsync(token);
                await EmitAsync(output, new { phase = "member_dm_discovery", status = "not_ready",
                    diagnostic = exception is NativeObservationException observation ? observation.Code : "discovery_invalid" });
                return;
            }

            var ready = snapshot.Enrollment == "enabled"
                ? snapshot.Scopes.Where(scope => scope.IsRunnable).ToDictionary(scope => scope.NativeConversationId!, StringComparer.Ordinal)
                : new Dictionary<string, NativeMemberDmDiscoveredScope>(StringComparer.Ordinal);

            foreach (var (peer, current) in workers.ToArray())
            {
                if (!ready.TryGetValue(peer, out var discovered))
                {
                    await StopMemberAsync(peer, token);
                    continue;
                }
                if (!current.Worker.Completion.IsCompleted && SameCapturedDescriptor(current.Worker.ExecutionContext!.ScopeFacts, discovered))
                    continue;

                // Drain first. The callback then rereads membership and DM status, so a
                // replacement descriptor cannot lend its newest lease to old queued work.
                try
                {
                    var replacement = await supervisor.RestartMemberDmSessionAsync(current.Worker,
                        async revalidateToken =>
                        {
                            var freshWire = await groupReader.ReadMemberDmDiscoveryAsync(revalidateToken);
                            var fresh = NativeMemberDmDiscoveryValidator.Parse(freshWire, setup.HostConfig.AccountId,
                                setup.HostConfig.SourceGroupJid, setup.GroupNative.ObservationSessionRef);
                            if (fresh.Enrollment != "enabled") throw new NativeObservationException("member_dm_enrollment_disabled");
                            var selected = fresh.Scopes.SingleOrDefault(item => item.IsRunnable && item.NativeConversationId == peer)
                                ?? throw new NativeObservationException("member_dm_scope_not_ready");
                            var facts = await ValidateScopeAsync(fresh, selected, revalidateToken);
                            if (!SameCapturedDescriptor(facts, discovered))
                                throw new NativeObservationException("member_dm_discovery_changed_during_revalidation");
                            return facts;
                        }, groupReader, setup.MemberDmStateRoot,
                        async (stateDirectory, context, runToken) =>
                        {
                            var livePath = await WriteMemberScopeFilesAsync(setup, context.ScopeFacts, stateDirectory, runToken);
                            var code = await dependencies.RunMemberDmSession(livePath, stateDirectory, context,
                                new PollCommandReader(setup.PollInterval), output, runToken);
                            if (code != 0) throw new InvalidOperationException("member_dm_worker_failed");
                        }, token);
                    workers[peer] = new(current.Binding, replacement);
                    retiredMemberBindings.Remove(peer);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    try { await supervisor.StopSessionAsync(current.Binding, CancellationToken.None); }
                    catch (Exception stopError) when (stopError is not OutOfMemoryException) { }
                    if (current.Worker.MemberDmBinding is { } captured)
                        retiredMemberBindings[peer] = captured;
                    // Keep the prior captured binding as a quarantine marker. A later
                    // discovery must pass RestartMemberDmSessionAsync's distinct-lease
                    // check; it must not become a fresh start merely because this
                    // revalidation attempt failed.
                    await EmitAsync(output, new { phase = "member_dm_scope", status = "not_ready",
                        diagnostic = exception is NativeObservationException observation ? observation.Code
                            : exception is InvalidDataException data ? data.Message : "scope_revalidation_failed" });
                }
            }

            foreach (var scope in ready.Values)
            {
                var peer = scope.NativeConversationId!;
                if (workers.ContainsKey(peer)) continue;
                try
                {
                    var facts = await ValidateScopeAsync(snapshot, scope, token);
                    if (retiredMemberBindings.TryGetValue(peer, out var retired))
                        _ = await NativeConversationSupervisor.AcceptRevalidatedMemberDmLeaseAsync(retired,
                            facts.ToBoundTextBinding(), Task.CompletedTask, token);
                    var worker = StartMember(facts);
                    workers.Add(peer, new(BuildBinding(setup.GroupManual, facts), worker));
                    retiredMemberBindings.Remove(peer);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    await EmitAsync(output, new { phase = "member_dm_scope", status = "not_ready",
                        diagnostic = exception is NativeObservationException observation ? observation.Code
                            : exception is InvalidDataException data ? data.Message : "scope_validation_failed" });
                }
            }

            await EmitAsync(output, new { phase = "member_dm_discovery", status = "reconciled",
                readyScopes = ready.Count, activeWorkers = workers.Count,
                notReadyScopes = snapshot.Scopes.Count(scope => !scope.IsRunnable) });
        }

        try
        {
            await RefreshAsync(cancellationToken);
            var nextLine = input.ReadLineAsync(cancellationToken).AsTask();
            while (!cancellationToken.IsCancellationRequested)
            {
                using var timerCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var timer = Task.Delay(setup.PollInterval, timerCancellation.Token);
                var completed = await Task.WhenAny(nextLine, timer);
                if (completed == timer)
                {
                    await RefreshAsync(cancellationToken);
                    continue;
                }
                timerCancellation.Cancel();
                var line = await nextLine;
                if (line is null || line == "/quit") break;
                if (line == "/refresh") await RefreshAsync(cancellationToken);
                else await EmitAsync(output, new { phase = "host", diagnostic = "only_refresh_or_quit_allowed" });
                nextLine = input.ReadLineAsync(cancellationToken).AsTask();
            }
            return 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return 0; }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            await EmitAsync(output, new { phase = "host", status = "failed",
                diagnostic = exception is NativeObservationException observation ? observation.Code
                    : exception is InvalidDataException data ? data.Message : "host_failed" });
            return 2;
        }
        finally
        {
            await StopAllMembersAsync(CancellationToken.None);
            await supervisor.StopSessionAsync(setup.GroupBinding, CancellationToken.None);
            groupClient.Dispose();
        }
    }

    private static ShadowSessionBinding BuildBinding(ManualShadowConfig template,
        NativeConversationSupervisor.ValidatedMemberDmScopeFacts facts) =>
        NativeObservationHostBinding.CreateManagedMemberDm(template with
        {
            Session = template.Session with { NativeConversationId = facts.NativeConversationId },
            ConversationNamespace = FilomenaIdentity.WhatsAppConversationNamespace(facts.NativeConversationId),
            PeerActorId = "whatsapp:" + facts.NativeConversationId,
            PeerDisplayName = "",
            GraphitiProbes = [],
        });

    private static bool SameCapturedDescriptor(NativeConversationSupervisor.ValidatedMemberDmScopeFacts facts,
        NativeMemberDmDiscoveredScope scope) =>
        facts.NativeConversationId == scope.NativeConversationId && facts.ScopeRef == scope.ScopeRef
        && facts.MembershipLeaseRef == scope.MembershipLeaseRef
        && facts.NativeAliases.SequenceEqual(scope.NativeAliases, StringComparer.Ordinal)
        && facts.ReaderEndpoint == scope.ReaderEndpoint && facts.RouteBindingRef == scope.RouteBindingRef
        && facts.NativeApprovedRevision == scope.EffectiveConfigRevision
        && facts.ContinueAfter.Epoch == scope.Epoch
        && scope.InitialCursor is { } cursor
        && facts.ContinueAfter == new NativeObservationCursor(cursor.Version, cursor.Epoch, cursor.SessionRef, cursor.Sequence);

    private static async Task<string> WriteMemberScopeFilesAsync(HostSetup setup,
        NativeConversationSupervisor.ValidatedMemberDmScopeFacts facts, string stateDirectory, CancellationToken token)
    {
        var artifact = NativeMemberDmEvidenceFactory.Generate(setup.MemberTemplate, facts, stateDirectory);
        var suffix = Guid.NewGuid().ToString("N");
        var nativePath = Path.Combine(stateDirectory, "native-member-dm-" + suffix + ".json");
        var manualPath = Path.Combine(stateDirectory, "manual-member-dm-" + suffix + ".json");
        var livePath = Path.Combine(stateDirectory, "live-member-dm-" + suffix + ".json");
        var session = setup.GroupManual.Session with { NativeConversationId = facts.NativeConversationId };
        var namespaceId = FilomenaIdentity.WhatsAppConversationNamespace(facts.NativeConversationId);
        var peer = "whatsapp:" + facts.NativeConversationId;
        var manual = setup.GroupManual with
        {
            ProjectFile = setup.ProjectFile,
            Session = session,
            ConversationNamespace = namespaceId,
            PeerActorId = peer,
            PeerDisplayName = "",
            OutboundBindings = setup.MemberDmOutboundBindings,
            JournalDirectory = stateDirectory,
            GraphitiProbes = [],
        };
        var native = setup.GroupNative with
        {
            ManualConfigFile = manualPath,
            ObservationEndpoint = facts.ReaderEndpoint,
            ObservationSessionRef = facts.ScopeRef,
            NativeConversationId = facts.NativeConversationId,
            ApprovedRevision = facts.NativeApprovedRevision,
            JournalDirectory = stateDirectory,
            MaxInteractions = 1,
            GroupParticipants = null,
            Recovery = null,
            NativeSourceGroupJid = facts.SourceGroupJid,
            NewDmMemoryReadiness = new(setup.HostConfig.ForeignControlNamespace, setup.HostConfig.ForeignControlCanary),
        };
        var live = artifact.LiveConfig with { NativeConfigFile = nativePath };
        await WriteNewJsonAsync(nativePath, native, token);
        await WriteNewJsonAsync(manualPath, manual, token);
        await WriteNewJsonAsync(livePath, live, token);
        return livePath;
    }

    private static async Task<HostSetup> LoadSetupAsync(string configPath, CancellationToken token)
    {
        var fullConfigPath = Path.GetFullPath(configPath);
        var host = await ReadJsonAsync<NativeConversationHostConfig>(fullConfigPath, token);
        if (host.DiscoveryIntervalMilliseconds is < 250 or > 60000
            || string.IsNullOrWhiteSpace(host.ForeignControlNamespace)
            || string.IsNullOrWhiteSpace(host.ForeignControlCanary)
            || string.IsNullOrWhiteSpace(host.AccountId) || string.IsNullOrWhiteSpace(host.PinnedProjectRevision)
            || !System.Text.RegularExpressions.Regex.IsMatch(host.SourceGroupJid, @"\A[0-9]+(?:-[0-9]+)?@g\.us\z"))
            throw new InvalidDataException("invalid_host_pins");
        var directory = Path.GetDirectoryName(fullConfigPath)!;
        var groupLivePath = Path.GetFullPath(host.GroupLiveConfigFile, directory);
        var memberTemplatePath = Path.GetFullPath(host.ManagedMemberDmTemplateFile, directory);
        var groupLive = await ReadJsonAsync<NativeLiveConfig>(groupLivePath, token);
        var memberTemplate = await ReadJsonAsync<NativeLiveConfig>(memberTemplatePath, token);
        if (!groupLive.ContinuousGroup || groupLive.ContinuousDm || !groupLive.AllowExactGroupDelivery
            || groupLive.ManagedMemberDm || groupLive.ContinueAfter is not null
            || !memberTemplate.ContinuousDm || !memberTemplate.ManagedMemberDm || memberTemplate.ContinuousGroup
            || memberTemplate.AllowExactGroupDelivery || memberTemplate.AllowNativeGroupParticipants
            || memberTemplate.AllowNativeGroupReplies || memberTemplate.MaxInteractions != 1
            || memberTemplate.ContinueAfter is not null)
            throw new InvalidDataException("host_live_authority_template_mismatch");
        var groupNativePath = Path.GetFullPath(groupLive.NativeConfigFile, Path.GetDirectoryName(groupLivePath)!);
        var groupNative = await ReadJsonAsync<NativeShadowConfig>(groupNativePath, token);
        var groupManualPath = Path.GetFullPath(groupNative.ManualConfigFile, Path.GetDirectoryName(groupNativePath)!);
        var groupManual = await ReadJsonAsync<ManualShadowConfig>(groupManualPath, token);
        if (groupNative.NativeConversationId != host.SourceGroupJid
            || groupManual.Session.NativeConversationId != host.SourceGroupJid
            || groupManual.Session.AccountId != host.AccountId
            || groupNative.ObservationSessionRef == string.Empty
            || string.IsNullOrWhiteSpace(groupNative.ObservationTokenEnvironmentVariable)
            || groupManual.SelfActorIds.Length != 1
            || groupNative.GroupParticipants is not { Count: > 0 })
            throw new InvalidDataException("group_host_binding_mismatch");
        var readerToken = Environment.GetEnvironmentVariable(groupNative.ObservationTokenEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(readerToken)) throw new InvalidDataException("observation_credential_unavailable");
        var groupDirectory = Path.GetFullPath(groupNative.JournalDirectory, Path.GetDirectoryName(groupNativePath)!);
        var manualGroupDirectory = Path.GetFullPath(groupManual.JournalDirectory, Path.GetDirectoryName(groupManualPath)!);
        if (groupDirectory != manualGroupDirectory || !Directory.Exists(groupDirectory))
            throw new InvalidDataException("existing_group_state_directory_required");
        var stateRoot = Path.GetFullPath(host.MemberDmStateRoot, directory);
        if (string.Equals(groupDirectory, stateRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("member_dm_state_root_must_be_separate");
        Directory.CreateDirectory(stateRoot);
        var projectPath = Path.GetFullPath(groupManual.ProjectFile, Path.GetDirectoryName(groupManualPath)!);
        var projectBytes = await File.ReadAllBytesAsync(projectPath, token);
        var pin = PinnedShadowProject.FromBytes(projectBytes, groupManual.ProjectId, groupManual.ProjectVersion,
            groupManual.ExpectedProjectSha256);
        if (pin.Project.ProjectRevision != host.PinnedProjectRevision)
            throw new InvalidDataException("pinned_project_revision_mismatch");
        var memberNativePath = Path.GetFullPath(memberTemplate.NativeConfigFile, Path.GetDirectoryName(memberTemplatePath)!);
        if (string.Equals(memberNativePath, groupNativePath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("member_dm_template_binding_mismatch");
        var memberNative = await ReadJsonAsync<NativeShadowConfig>(memberNativePath, token);
        var memberManualPath = Path.GetFullPath(memberNative.ManualConfigFile, Path.GetDirectoryName(memberNativePath)!);
        if (string.Equals(memberManualPath, groupManualPath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("member_dm_template_binding_mismatch");
        var memberManual = await ReadJsonAsync<ManualShadowConfig>(memberManualPath, token);
        var groupProjectFile = Path.GetFullPath(groupManual.ProjectFile, Path.GetDirectoryName(groupManualPath)!);
        var memberProjectFile = Path.GetFullPath(memberManual.ProjectFile, Path.GetDirectoryName(memberManualPath)!);
        if (memberNative.NativeConversationId != memberManual.Session.NativeConversationId
            || memberManual.Session.AccountId != host.AccountId
            || !memberManual.SelfActorIds.SequenceEqual(groupManual.SelfActorIds, StringComparer.Ordinal)
            || memberManual.ProjectId != groupManual.ProjectId
            || memberManual.ProjectVersion != groupManual.ProjectVersion
            || !string.Equals(memberManual.ExpectedProjectSha256, groupManual.ExpectedProjectSha256, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(memberProjectFile, groupProjectFile, StringComparison.OrdinalIgnoreCase)
            || memberManual.OutboundBindings.CandidatePorts.Count == 0
            || memberManual.OutboundBindings.ReplyTargetPorts.Count != 0)
            throw new InvalidDataException("member_dm_template_binding_mismatch");
        try { _ = NativeObservationHostBinding.CreateManagedMemberDm(memberManual); }
        catch (ArgumentException exception) { throw new InvalidDataException("member_dm_template_binding_mismatch", exception); }
        try
        {
            ManualShadowCommand.ValidateRoles(pin, memberManual.OutboundBindings);
            NativeObservationHostBinding.ValidateManagedMemberDmSendWiring(pin, memberManual.OutboundBindings);
        }
        catch (InvalidDataException exception)
        {
            throw new InvalidDataException("member_dm_template_binding_mismatch", exception);
        }
        var groupBinding = NativeObservationHostBinding.Create(groupManual, host.SourceGroupJid,
            groupNative.GroupParticipants, live: true, allowExactGroupDelivery: groupLive.AllowExactGroupDelivery);
        NativeObservationHostBinding.ValidateGroupSendWiring(pin, groupManual.OutboundBindings);
        if (host.ForeignControlNamespace == groupManual.ConversationNamespace)
            throw new InvalidDataException("foreign_memory_control_must_be_separate");
        return new(fullConfigPath, host, groupLivePath, groupLive, groupNativePath, groupNative,
            groupManualPath, groupManual, projectPath, memberTemplate, memberManual.OutboundBindings, groupBinding, groupDirectory,
            stateRoot, readerToken, TimeSpan.FromMilliseconds(host.DiscoveryIntervalMilliseconds));
    }

    private static NativeConversationHostDependencies ProductionDependencies() => new(
        endpoint => new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(30) },
        async (livePath, _, input, output, token) => await NativeLiveCommand.RunAsync(
            ["--native-live", livePath, "--authorize-continuous-group"], input, output, token),
        async (livePath, _, context, input, output, token) => await NativeLiveCommand.RunManagedMemberDmAsync(
            ["--native-live", livePath, "--authorize-continuous-dm"], input, output, token, context));

    private static async Task<T> ReadJsonAsync<T>(string path, CancellationToken token) =>
        JsonSerializer.Deserialize<T>(await File.ReadAllTextAsync(path, token), Json) ?? throw new JsonException();

    private static async Task WriteNewJsonAsync<T>(string path, T value, CancellationToken token)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, Json);
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read,
            4096, FileOptions.WriteThrough);
        await stream.WriteAsync(bytes, token);
        await stream.FlushAsync(token);
    }

    private static async Task EmitAsync(TextWriter output, object value)
    {
        await output.WriteLineAsync(JsonSerializer.Serialize(value, Json));
        await output.FlushAsync();
    }

    private static async Task<int> FailAsync(TextWriter output, string diagnostic)
    {
        await EmitAsync(output, new { status = "FAIL", diagnostic });
        return 2;
    }

    private sealed record HostSetup(string HostPath, NativeConversationHostConfig HostConfig,
        string GroupLiveConfigPath, NativeLiveConfig GroupLive, string GroupNativePath, NativeShadowConfig GroupNative,
        string GroupManualPath, ManualShadowConfig GroupManual, string ProjectFile, NativeLiveConfig MemberTemplate,
        OutboundBindings MemberDmOutboundBindings, ShadowSessionBinding GroupBinding, string GroupStateDirectory,
        string MemberDmStateRoot, string ReaderToken,
        TimeSpan PollInterval);

    private sealed record ManagedWorker(ShadowSessionBinding Binding, NativeConversationSupervisor.NativeConversationWorker Worker);

    private sealed class PollCommandReader(TimeSpan interval) : TextReader
    {
        public override async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
        {
            await Task.Delay(interval, cancellationToken);
            return "/poll";
        }
    }
}
