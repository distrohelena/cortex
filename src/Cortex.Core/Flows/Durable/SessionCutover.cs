using System.Text.Json;
using System.Security.Cryptography;
using Cortex.Core.Shadow;
using Cortex.Core.Outbound;
using Cortex.Core.Flows.Sessions;

namespace Cortex.Core.Flows.Durable;

public sealed record SessionCutoverManifest(int Version, string CutoverId, string SourceBackend,
    string InstallationId, SessionSnapshot Source, string SourceEpoch, long SourceCursor,
    string JournalFingerprint, ArtifactRef ProjectRef, ArtifactRef HistoryRef, ArtifactRef DedupeRef);

/// <summary>Idle hydration only. Admission ownership must be paused externally throughout capture/import.</summary>
public static class SessionCutover
{
    private static ArtifactScope OwnershipScope(string installation, SessionKey session, string revision) => new(installation, session, "cutover-ownership", revision);

    public static async Task RecordVerifiedImportAsync(SessionCutoverManifest manifest, SessionSnapshot imported, IExecutionArtifactStore artifacts, CancellationToken token)
    {
        if (imported != manifest.Source) throw new InvalidDataException("Unverified imported state.");
        await ReadRequestAsync(manifest, artifacts, manifest.JournalFingerprint, token).ConfigureAwait(false);
        await artifacts.PutOnceAsync(manifest.ProjectRef.Scope, "target-import-verified", JsonSerializer.SerializeToElement(imported), token).ConfigureAwait(false);
        await artifacts.PutOnceAsync(OwnershipScope(manifest.InstallationId, imported.Session, imported.ProjectRevision), "verified-import", JsonSerializer.SerializeToElement(manifest), token).ConfigureAwait(false);
    }

    public static async Task<SessionCutoverManifest?> ReadVerifiedImportAsync(string installation, SessionKey session, string revision, IExecutionArtifactStore artifacts, CancellationToken token)
    {
        var reference = await artifacts.FindAsync(OwnershipScope(installation, session, revision), "verified-import", token).ConfigureAwait(false);
        if (reference is null) return null;
        var manifest = (await artifacts.ReadAsync(reference, token).ConfigureAwait(false)).Deserialize<SessionCutoverManifest>() ?? throw new InvalidDataException("Missing verified manifest.");
        if (manifest.InstallationId != installation || manifest.Source.Session != session || manifest.Source.ProjectRevision != revision) throw new InvalidDataException("Foreign cutover marker.");
        await ReadRequestAsync(manifest, artifacts, manifest.JournalFingerprint, token).ConfigureAwait(false);
        var proof = await artifacts.FindAsync(manifest.ProjectRef.Scope, "target-import-verified", token).ConfigureAwait(false) ?? throw new InvalidDataException("Unverified target import.");
        if ((await artifacts.ReadAsync(proof, token).ConfigureAwait(false)).Deserialize<SessionSnapshot>() != manifest.Source) throw new InvalidDataException("Invalid import proof.");
        return manifest;
    }

    public static async Task CheckHostActivationAsync(SessionCutoverManifest manifest, string sourceDirectory, IExecutionArtifactStore artifacts, CancellationToken token)
    {
        var scope = OwnershipScope(manifest.InstallationId, manifest.Source.Session, manifest.Source.ProjectRevision);
        var activated = await artifacts.FindAsync(scope, "host-activated", token).ConfigureAwait(false);
        if (activated is null) ValidateFingerprint(manifest, FingerprintLegacySource(sourceDirectory));
        else if ((await artifacts.ReadAsync(activated, token).ConfigureAwait(false)).GetString() != manifest.CutoverId) throw new InvalidDataException("Foreign activated cutover.");
    }

    public static async Task RecordHostActivationAsync(SessionCutoverManifest manifest, SessionSnapshot current, IExecutionArtifactStore artifacts, CancellationToken token)
    {
        if (current.Session != manifest.Source.Session || current.ProjectId != manifest.Source.ProjectId || current.ProjectVersion != manifest.Source.ProjectVersion || current.ProjectRevision != manifest.Source.ProjectRevision
            || current.AdmissionWatermark < manifest.Source.AdmissionWatermark || current.StateVersion < manifest.Source.StateVersion)
            throw new InvalidDataException("Temporal context does not preserve the verified import.");
        await artifacts.PutOnceAsync(OwnershipScope(manifest.InstallationId, current.Session, current.ProjectRevision), "host-activated", JsonSerializer.SerializeToElement(manifest.CutoverId), token).ConfigureAwait(false);
    }

    public static readonly IReadOnlyList<string> LegacySourceFiles = Array.AsReadOnly(new[] { "observation-live-context.json", "observation-live-inbound.jsonl", "observation-live-outbound.jsonl" });

    public static string FingerprintLegacySource(string sourceDirectory) => Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(LegacySourceFiles.Select(name =>
        new { Name = name, Sha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(sourceDirectory, name)))) }).ToArray())));

    public static async Task<SessionCutoverManifest> CaptureLegacyAsync(string sourceDirectory, string installationId,
        ShadowSessionBinding binding, SessionFlowProject project, string sourceEpoch, long sourceCursor,
        IExecutionArtifactStore artifacts, CancellationToken token)
    {
        var paths = LegacySourceFiles.Select(name => Path.Combine(Path.GetFullPath(sourceDirectory), name)).ToArray();
        var leases = new List<FileStream>();
        try
        {
            // Deny a concurrent writer while checking fingerprints and capturing the exact private checkpoint.
            foreach (var path in paths)
            {
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Source cannot use reparse points.");
                leases.Add(new(path, FileMode.Open, FileAccess.Read, FileShare.Read));
            }
            var fingerprint = FingerprintLegacySource(sourceDirectory);
            var pin = new PinnedShadowProject(project);
            var context = new LiveSessionContextStore(sourceDirectory, binding, pin).Load() ?? throw new InvalidDataException("Source has no idle context.");
            if (context.Snapshot.Session != binding.Session) throw new InvalidDataException("Foreign source context.");
            var anchor = ShadowInboundJournal.ReadSourceAnchor(paths[1]);
            using var inbox = ShadowInboundJournal.InspectReadOnly(paths[1], binding, project.ProjectRevision, anchor.Epoch, anchor.InitialTail);
            using var outbound = new OutboundJournal(paths[2], readOnly: true);
            if (sourceEpoch != anchor.Epoch || sourceCursor != inbox.CurrentCursor) throw new InvalidDataException("Source epoch/cursor changed.");
            return await CaptureAsync("local", installationId, project, context.Snapshot, context.History, context.SeenProviderIds,
                sourceEpoch, sourceCursor, fingerprint, inbox.ReconciliationRequired, outbound.RequiresReconciliation(binding.OpaqueRef), artifacts, token).ConfigureAwait(false);
        }
        finally { foreach (var lease in leases) lease.Dispose(); }
    }

    public static async Task<SessionCutoverManifest> CaptureAsync(string sourceBackend, string installationId,
        SessionFlowProject project, SessionSnapshot source, IReadOnlyList<SessionHistoryEntry> history,
        IReadOnlyList<string> seenProviderIds, string sourceEpoch, long sourceCursor, string journalFingerprint,
        bool hasUnresolvedInbox, bool hasUnresolvedOutbound, IExecutionArtifactStore artifacts, CancellationToken token)
    {
        DurableIdentity.Require(installationId); DurableIdentity.Require(sourceEpoch);
        if (sourceBackend != "local") throw new InvalidDataException("Only a local idle source is supported.");
        if (source.ProjectId != project.ProjectId || source.ProjectVersion != project.ProjectVersion || source.ProjectRevision != project.ProjectRevision)
            throw new InvalidDataException("Cutover project does not match the source.");
        // Reuse the production restoration validator without executing retained work.
        await using (var validator = new SessionFlowRuntime(TimeProvider.System, new NoCompletion()))
        {
            validator.UpdateProject(project);
            validator.RestoreContext(source.Session, new(source, history, seenProviderIds), history.FirstOrDefault()?.EntryKind ?? SessionEntryKind.Dm);
        }
        var projectPayload = JsonSerializer.SerializeToElement(project);
        var historyPayload = JsonSerializer.SerializeToElement(history);
        var dedupePayload = JsonSerializer.SerializeToElement(seenProviderIds.Order(StringComparer.Ordinal).ToArray());
        var id = DurableIdentity.Hash("cutover", [installationId, DurableIdentity.SessionWorkflowId(installationId, source.Session), sourceBackend,
            sourceEpoch, sourceCursor.ToString(System.Globalization.CultureInfo.InvariantCulture), journalFingerprint,
            FileExecutionArtifactStore.Digest(JsonSerializer.SerializeToElement(source)), FileExecutionArtifactStore.Digest(projectPayload),
            FileExecutionArtifactStore.Digest(historyPayload), FileExecutionArtifactStore.Digest(dedupePayload)]);
        var scope = new ArtifactScope(installationId, source.Session, id, source.ProjectRevision);
        var placeholder = new ArtifactRef(1, scope, "pending", new string('0', 64));
        var manifest = new SessionCutoverManifest(1, id, sourceBackend, installationId, source, sourceEpoch, sourceCursor, journalFingerprint, placeholder, placeholder, placeholder);
        Validate(manifest, source, hasUnresolvedInbox, hasUnresolvedOutbound);
        manifest = manifest with
        {
            ProjectRef = await artifacts.PutOnceAsync(scope, "source-project", projectPayload, token).ConfigureAwait(false),
            HistoryRef = await artifacts.PutOnceAsync(scope, "source-history", historyPayload, token).ConfigureAwait(false),
            DedupeRef = await artifacts.PutOnceAsync(scope, "source-dedupe", dedupePayload, token).ConfigureAwait(false),
        };
        await artifacts.PutOnceAsync(scope, "cutover-manifest", JsonSerializer.SerializeToElement(manifest), token).ConfigureAwait(false);
        return manifest;
    }

    public static void Validate(SessionCutoverManifest manifest, SessionSnapshot source, bool hasUnresolvedInbox, bool hasUnresolvedOutbound)
    {
        if (manifest.Version != 1 || manifest.SourceBackend != "local" || manifest.Source != source || source.OpenBufferCount != 0
            || source.PendingExecutionCount != 0 || hasUnresolvedInbox || hasUnresolvedOutbound || manifest.SourceCursor < 0)
            throw new InvalidDataException("Cutover requires the exact idle, reconciled source.");
        DurableIdentity.Require(manifest.CutoverId); DurableIdentity.Require(manifest.InstallationId); DurableIdentity.Require(manifest.SourceEpoch);
        ValidateFingerprint(manifest, manifest.JournalFingerprint);
        foreach (var reference in new[] { manifest.ProjectRef, manifest.HistoryRef, manifest.DedupeRef })
        {
            DurableIdentity.Validate(reference);
            if (reference.Scope != new ArtifactScope(manifest.InstallationId, source.Session, manifest.CutoverId, source.ProjectRevision))
                throw new InvalidDataException("Foreign cutover artifact.");
        }
    }

    public static void ValidateFingerprint(SessionCutoverManifest manifest, string currentFingerprint)
    {
        if (manifest.JournalFingerprint.Length != 64 || manifest.JournalFingerprint.Any(c => !char.IsAsciiHexDigit(c))
            || !string.Equals(manifest.JournalFingerprint, currentFingerprint, StringComparison.Ordinal))
            throw new InvalidDataException("Source journal changed since capture.");
    }

    public static void ValidateRollback(SessionCutoverManifest manifest, SessionSnapshot target)
    {
        if (target != manifest.Source) throw new InvalidOperationException("Target progressed; rollback requires reconciliation and a new ownership decision.");
    }

    public static async Task<SessionSnapshot> ImportAsync(SessionCutoverManifest manifest, IExecutionArtifactStore artifacts,
        ISessionExecutionBackend target, string currentJournalFingerprint, CancellationToken token)
    {
        var request = await ReadRequestAsync(manifest, artifacts, currentJournalFingerprint, token).ConfigureAwait(false);
        var project = (await artifacts.ReadAsync(manifest.ProjectRef, token).ConfigureAwait(false)).Deserialize<SessionFlowProject>() ?? throw new InvalidDataException("Missing source project.");
        await target.PublishAsync(project, token).ConfigureAwait(false);
        var result = await target.ImportIdleAsync(request, token).ConfigureAwait(false);
        if (result != manifest.Source) throw new InvalidDataException("Imported snapshot differs from the source.");
        return result;
    }

    public static async Task<SessionImportRequest> ReadRequestAsync(SessionCutoverManifest manifest, IExecutionArtifactStore artifacts,
        string currentJournalFingerprint, CancellationToken token)
    {
        Validate(manifest, manifest.Source, false, false); ValidateFingerprint(manifest, currentJournalFingerprint);
        var scope = manifest.ProjectRef.Scope;
        var saved = await artifacts.FindAsync(scope, "cutover-manifest", token).ConfigureAwait(false) ?? throw new InvalidDataException("Uncommitted cutover manifest.");
        if ((await artifacts.ReadAsync(saved, token).ConfigureAwait(false)).Deserialize<SessionCutoverManifest>() != manifest)
            throw new InvalidDataException("Manifest does not match its captured private identity.");
        var project = (await artifacts.ReadAsync(manifest.ProjectRef, token).ConfigureAwait(false)).Deserialize<SessionFlowProject>() ?? throw new InvalidDataException("Missing project.");
        var history = (await artifacts.ReadAsync(manifest.HistoryRef, token).ConfigureAwait(false)).Deserialize<List<SessionHistoryEntry>>() ?? throw new InvalidDataException("Missing history.");
        var dedupe = (await artifacts.ReadAsync(manifest.DedupeRef, token).ConfigureAwait(false)).Deserialize<List<string>>() ?? throw new InvalidDataException("Missing dedupe.");
        await using var validator = new SessionFlowRuntime(TimeProvider.System, new NoCompletion());
        validator.UpdateProject(project); validator.RestoreContext(manifest.Source.Session, new(manifest.Source, history, dedupe), history.FirstOrDefault()?.EntryKind ?? SessionEntryKind.Dm);
        return new(manifest.CutoverId, manifest.InstallationId, manifest.Source, history, dedupe, manifest.SourceEpoch, manifest.SourceCursor, manifest.JournalFingerprint);
    }

    private sealed class NoCompletion : IFlowCompletion
    {
        public Task<string> CompleteAsync(FlowCompletionRequest request, CancellationToken cancellationToken) => throw new InvalidOperationException("Cutover cannot execute inference.");
    }
}
