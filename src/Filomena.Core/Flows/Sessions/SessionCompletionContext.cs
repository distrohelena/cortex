namespace Filomena.Core.Flows.Sessions;

/// <summary>Captured execution identity, not an authorization grant.</summary>
public sealed record SessionCompletionContext(SessionKey Session, string ExecutionId, string BatchId,
    string ProjectId, long ProjectVersion, string ProjectRevision);

public interface ISessionFlowCompletionFactory
{
    IFlowCompletion Create(SessionCompletionContext context);
}
