// Offline operator utility. No sender, credentials, model, Graphiti or transport dependencies.
using System.Text.Json;
using Filomena.App;
using Filomena.Core.Flows.Sessions;
using Filomena.Core.Shadow;

var transferCheckpoint = args.Length == 9 && args[8] == "--checkpoint";
if (args.Length != 8 && !transferCheckpoint)
    throw new ArgumentException("native-config target-empty-private-directory audit target-project target-sha256 inbox-sha256 outbox-sha256 audit-sha256");
var nativeFile = Path.GetFullPath(args[0]);
using var native = JsonDocument.Parse(File.ReadAllBytes(nativeFile));
var root = Path.GetDirectoryName(nativeFile)!;
var manualFile = Path.GetFullPath(native.RootElement.GetProperty("manualConfigFile").GetString()!, root);
using var manual = JsonDocument.Parse(File.ReadAllBytes(manualFile));
var config = manual.RootElement;
var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
var session = config.GetProperty("session").Deserialize<SessionKey>(json)!;
var hostConfig = config.Deserialize<ManualShadowConfig>(json) ?? throw new InvalidDataException("invalid_manual_config");
var participants = native.RootElement.TryGetProperty("groupParticipants", out var roster)
    ? roster.Deserialize<Dictionary<string, string>>(json) : null;
var binding = NativeObservationHostBinding.Create(hostConfig,
    native.RootElement.GetProperty("nativeConversationId").GetString()!, participants,
    live: true, allowExactGroupDelivery: session.NativeConversationId.EndsWith("@g.us", StringComparison.Ordinal));
var sourceProject = Path.GetFullPath(config.GetProperty("projectFile").GetString()!, Path.GetDirectoryName(manualFile)!);
var oldPin = PinnedShadowProject.FromBytes(File.ReadAllBytes(sourceProject), config.GetProperty("projectId").GetString()!,
    config.GetProperty("projectVersion").GetInt64(), config.GetProperty("expectedProjectSha256").GetString()!);
var pin = PinnedShadowProject.FromBytes(File.ReadAllBytes(args[3]), oldPin.Project.ProjectId, oldPin.Project.ProjectVersion, args[4]);
var source = Path.GetFullPath(native.RootElement.GetProperty("journalDirectory").GetString()!, root);
if (transferCheckpoint && !Path.GetFullPath(args[2]).Equals(Path.Combine(source, "observation-live-context.json"), StringComparison.OrdinalIgnoreCase))
    throw new InvalidDataException("Expected source checkpoint path.");
var snapshot = transferCheckpoint
    ? LiveContextMigration.TransferCheckpoint(source, Path.GetFullPath(args[1]), binding, oldPin, pin, args[5], args[6], args[7])
    : LiveContextMigration.Import(source, Path.GetFullPath(args[1]), Path.GetFullPath(args[2]), binding, oldPin, pin, args[5], args[6], args[7]);
Console.WriteLine(JsonSerializer.Serialize(new { status = "context_imported", snapshot.HistoryCount, snapshot.CurrentStateId,
    snapshot.ProjectRevision, executions = 0, sends = 0, senderAvailable = false }));
