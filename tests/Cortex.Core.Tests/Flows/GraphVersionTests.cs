using System.Text.Json;
using Cortex.Core.Flows;
using Cortex.Core.Flows.Sessions;
using Xunit;

namespace Cortex.Core.Tests.Flows;

public sealed class GraphVersionTests
{
    [Fact]
    public void TypedCompilerReportsReceivedAndRequiredGraphVersions()
    {
        var graph = JsonSerializer.SerializeToElement(new
        {
            version = 3,
            nodes = new[] { new { id = "dm", kind = "dm" } },
            edges = Array.Empty<object>(),
            controlEdges = Array.Empty<object>(),
        });

        var exception = Assert.Throws<TypedFlowException>(() => TypedFlowCompiler.Compile(graph, "dm"));

        Assert.Contains(exception.Issues, issue => issue.Port == "version" &&
            issue.Reason.Contains("Graph version 3", StringComparison.Ordinal) &&
            issue.Reason.Contains("unsupported", StringComparison.OrdinalIgnoreCase) &&
            issue.Reason.Contains("expected version 4", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task DurableProjectCompilerReportsReceivedAndRequiredGraphVersions()
    {
        await using var runtime = new SessionFlowRuntime(TimeProvider.System, new NoCompletion());
        var project = SessionSliceAcceptanceSupport.Project("legacy-v3", root =>
            SessionSliceAcceptanceSupport.Graph(SessionSliceAcceptanceSupport.States(root)[0])["version"] = 3);

        var exception = Assert.Throws<SessionFlowProjectException>(() => runtime.UpdateProject(project));

        Assert.Contains(exception.Issues, issue => issue.Port == "version" &&
            issue.Reason.Contains("Graph version 3", StringComparison.Ordinal) &&
            issue.Reason.Contains("unsupported", StringComparison.OrdinalIgnoreCase) &&
            issue.Reason.Contains("expected version 4", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class NoCompletion : IFlowCompletion
    {
        public Task<string> CompleteAsync(FlowCompletionRequest request, CancellationToken token) =>
            throw new InvalidOperationException("No completion expected.");
    }
}
