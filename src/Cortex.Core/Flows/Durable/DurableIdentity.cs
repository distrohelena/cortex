using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Cortex.Core.Flows.Sessions;

namespace Cortex.Core.Flows.Durable;

public static class DurableIdentity
{
    public static string SessionWorkflowId(string installationId, SessionKey session) =>
        Hash("session", SessionParts(installationId, session));
    public static string ExecutionWorkflowId(ArtifactScope scope, string batchId) =>
        Hash("workflow", ScopeParts(scope).Append(batchId));
    public static string OperationId(StepKey key) => StepId("step", key);
    public static string FeedbackRequestId(StepKey key) => StepId("feedback", key);
    public static string BatchId(string installationId, SessionKey session, string projectRevision, long batchOrdinal)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(batchOrdinal);
        return Hash("batch", SessionParts(installationId, session).Append(projectRevision)
            .Append(batchOrdinal.ToString(System.Globalization.CultureInfo.InvariantCulture)));
    }
    public static string ExecutionId(string installationId, SessionKey session, string batchId, string purpose) =>
        Hash("execution", SessionParts(installationId, session).Append(batchId).Append(purpose));

    internal static void Validate(ArtifactScope scope) => _ = ScopeParts(scope).ToArray();
    internal static void Validate(ArtifactRef reference)
    {
        Validate(reference.Scope);
        Require(reference.Key);
        if (reference.Version != 1 || reference.Sha256.Length != 64 || reference.Sha256.Any(c => !char.IsAsciiHexDigit(c)))
            throw new InvalidDataException("Invalid artifact reference version or digest.");
    }
    internal static void Require(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsControl)) throw new ArgumentException("Identity fields must be non-empty and contain no control characters.");
    }
    internal static IEnumerable<string> ScopeParts(ArtifactScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var fields = SessionParts(scope.InstallationId, scope.Session).Append(scope.ExecutionId).Append(scope.ProjectRevision).ToArray();
        foreach (var field in fields) Require(field);
        return fields;
    }
    private static IEnumerable<string> SessionParts(string installationId, SessionKey session)
    {
        ArgumentNullException.ThrowIfNull(session);
        string[] parts = [installationId, session.RuntimeId, session.CharacterId, session.ChannelId, session.AccountId, session.NativeConversationId];
        foreach (var field in parts) Require(field);
        return parts;
    }
    private static string StepId(string kind, StepKey key)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(key.Ordinal);
        return Hash(kind, ScopeParts(key.Scope).Append(key.NodeId).Append(key.Ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture)));
    }
    internal static string Hash(string kind, IEnumerable<string> fields)
    {
        var parts = fields.ToArray();
        foreach (var field in parts) Require(field);
        return "cortex-v1-" + kind + "-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(parts))));
    }
}
