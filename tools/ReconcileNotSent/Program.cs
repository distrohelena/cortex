// Explicit operator reconciliation. No sender, runtime, memory client or Rearm.
using System.Security.Cryptography;
using System.Text.Json;
using Cortex.Core.Flows.Sessions;
using Cortex.Core.Outbound;
using Cortex.Core.Shadow;

if (args.Length != 5 || args[4] is not ("--check" or "--apply"))
    throw new ArgumentException("native-config attempt-ref expected-journal-sha256 evidence-ref --check|--apply");
var nativeFile = Path.GetFullPath(args[0]);
using var native = JsonDocument.Parse(File.ReadAllBytes(nativeFile));
var root = Path.GetDirectoryName(nativeFile)!;
var manualFile = Path.GetFullPath(native.RootElement.GetProperty("manualConfigFile").GetString()!, root);
using var manual = JsonDocument.Parse(File.ReadAllBytes(manualFile));
var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
var session = manual.RootElement.GetProperty("session").Deserialize<SessionKey>(json)!;
var bindings = manual.RootElement.GetProperty("outboundBindings").Deserialize<OutboundBindings>(json)!;
var projectFile = Path.GetFullPath(manual.RootElement.GetProperty("projectFile").GetString()!, Path.GetDirectoryName(manualFile)!);
var pin = PinnedShadowProject.FromBytes(File.ReadAllBytes(projectFile),
    manual.RootElement.GetProperty("projectId").GetString()!,
    manual.RootElement.GetProperty("projectVersion").GetInt64(),
    manual.RootElement.GetProperty("expectedProjectSha256").GetString()!);
var directory = Path.GetFullPath(native.RootElement.GetProperty("journalDirectory").GetString()!, root);
var journal = Path.Combine(directory, "observation-live-outbound.jsonl");
var before = File.ReadAllBytes(journal); // Must exist, never replace the ledger.
if (Convert.ToHexString(SHA256.HashData(before)) != args[2]) throw new InvalidDataException("journal_changed");
ArgumentException.ThrowIfNullOrWhiteSpace(args[3]);
var entries = File.ReadLines(journal).Select(line => JsonSerializer.Deserialize<JsonElement>(line)).ToArray();
var groups = entries.GroupBy(e => e.GetProperty("AttemptRef").GetString()).ToArray();
if (groups.Any(g => g.Last().GetProperty("Status").GetString() == "pending"))
    throw new InvalidDataException("unrelated_pending_attempt");
var target = groups.Single(g => g.Key == args[1]).Last();
var sessionRef = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(session)));
if (target.GetProperty("Status").GetString() != "indeterminate"
    || target.GetProperty("SessionRef").GetString() != sessionRef)
    throw new InvalidDataException("attempt_or_session_mismatch");
if (args[4] == "--check")
{
    Console.WriteLine("{\"eligible\":true,\"mutations\":0,\"senderAvailable\":false}");
    return;
}
// Backup inherits the existing private directory's restricted ACL.
var backupPath = journal + "." + args[1] + ".before-reconciliation";
if (File.Exists(backupPath))
{
    if (!File.ReadAllBytes(backupPath).AsSpan().SequenceEqual(before)) throw new InvalidDataException("backup_mismatch");
}
else
{
    using var backup = new FileStream(backupPath, FileMode.CreateNew, FileAccess.Write);
    backup.Write(before);
    backup.Flush(flushToDisk: true);
}
using (var host = new ShadowOutboundHost(journal, pin.Project.Document, bindings, [session], TransportSafetyCapabilities.Unproven))
{
    // Our domain owner already holds the exclusive writer. A concurrent reader
    // must allow that existing write handle on Windows; this grants no second writer.
    using (var current = new FileStream(journal, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
    using (var snapshot = new MemoryStream())
    {
        current.CopyTo(snapshot);
        if (!snapshot.ToArray().AsSpan().SequenceEqual(before)) throw new InvalidDataException("journal_changed");
    }
    if (host.EffectiveMode != OutboundMode.Disabled) throw new InvalidOperationException("must_remain_disabled");
    host.Reconcile(session, args[1], ReconciliationVerdict.NotSent, args[3]);
    if (host.RequiresReconciliation(session)) throw new InvalidDataException("other_unresolved_attempt");
}
var after = File.ReadAllBytes(journal);
if (!after.AsSpan(0, before.Length).SequenceEqual(before)) throw new InvalidDataException("journal_prefix_changed");
Console.WriteLine(JsonSerializer.Serialize(new {
    status = "not_sent", attemptRef = args[1], evidenceRef = args[3],
    beforeSha256 = args[2], afterSha256 = Convert.ToHexString(SHA256.HashData(after)),
    originalEntriesPreserved = true, senderAvailable = false, sends = 0
}));
