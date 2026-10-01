using System.Text.Json.Nodes;
using Cortex.Core.Flows.Sessions;
using Xunit;

namespace Cortex.Core.Tests.Flows;

public sealed class InstructionContextTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task ContextIsExplicitAndComposedOnlyAtInstruction(bool personality, bool guidance)
    {
        var completion = new RecordingCompletion([SessionSliceAcceptanceSupport.Response("engajada")]);
        await using var runtime = new SessionFlowRuntime(TimeProvider.System, completion);
        runtime.UpdateProject(Project("v1", personality, guidance));
        var session = SessionSliceAcceptanceSupport.Key("a");
        await SessionSliceAcceptanceSupport.ReceiveAsync(runtime, session, "m1", SessionEntryKind.Group);
        await runtime.WaitForIdleAsync(session, TestContext.Current.CancellationToken);
        var request = Assert.Single(completion.Requests);
        Assert.Equal(personality, request.System.Contains("CORE-V1", StringComparison.Ordinal));
        Assert.Equal(guidance, request.System.Contains("GUIDANCE-V1", StringComparison.Ordinal));
        Assert.Contains("RESPONSIBILITY", request.System);
        Assert.DoesNotContain("CORE-V1", request.Inputs.GetRawText());
        Assert.DoesNotContain("GUIDANCE-V1", request.Inputs.GetRawText());
        Assert.True(Assert.Single(runtime.ReadOutcomes(session)).Success);
        Assert.DoesNotContain("CORE-V1", System.Text.Json.JsonSerializer.Serialize(runtime.ReadTrace(session)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InterestsAreIndependentExplicitInstructionContext(bool enabled)
    {
        var completion = new RecordingCompletion([SessionSliceAcceptanceSupport.Response("engajada")]);
        await using var runtime = new SessionFlowRuntime(TimeProvider.System, completion);
        runtime.UpdateProject(Project("interests", false, false, interests: enabled));
        var session = SessionSliceAcceptanceSupport.Key("interests");
        await SessionSliceAcceptanceSupport.ReceiveAsync(runtime, session, "m1", SessionEntryKind.Group);
        await runtime.WaitForIdleAsync(session, TestContext.Current.CancellationToken);
        var request = Assert.Single(completion.Requests);
        Assert.Equal(enabled, request.System.Contains("INTERESTS-INTERESTS", StringComparison.Ordinal));
        Assert.DoesNotContain("CORE-", request.System);
        Assert.DoesNotContain("GUIDANCE-", request.System);
        Assert.DoesNotContain("INTERESTS-", request.Inputs.GetRawText());
        Assert.DoesNotContain("INTERESTS-", System.Text.Json.JsonSerializer.Serialize(runtime.ReadTrace(session)));
    }

    [Fact]
    public async Task OpenBatchKeepsItsPromptContextAcrossAdministrativePublication()
    {
        var completion = new RecordingCompletion([SessionSliceAcceptanceSupport.Response("engajada"), SessionSliceAcceptanceSupport.Response("engajada")]);
        var clock = new FakeTimeProvider();
        await using var runtime = new SessionFlowRuntime(clock, completion);
        runtime.UpdateProject(Project("v1", true, true, maxMessages: 2, maxWait: 2, interests: true));
        var session = SessionSliceAcceptanceSupport.Key("snapshot");
        await SessionSliceAcceptanceSupport.ReceiveAsync(runtime, session, "old", SessionEntryKind.Group);
        runtime.UpdateProject(Project("v2", true, true, version: 2, maxMessages: 1, interests: true));
        await SessionSliceAcceptanceSupport.ReceiveAsync(runtime, session, "new", SessionEntryKind.Group);
        await runtime.WaitForIdleAsync(session, TestContext.Current.CancellationToken);
        // Administrative versions intentionally do NOT merge their batches.
        var request = Assert.Single(completion.Requests);
        Assert.Contains("CORE-V2", request.System);
        Assert.Contains("INTERESTS-V2", request.System);
        Assert.DoesNotContain("CORE-V1", request.System);
        clock.Advance(TimeSpan.FromSeconds(2));
        await runtime.WaitForIdleAsync(session, TestContext.Current.CancellationToken);
        Assert.Equal(2, completion.Requests.Count);
        var original = completion.Requests.Last();
        Assert.Contains("CORE-V1", original.System);
        Assert.Contains("INTERESTS-V1", original.System);
        Assert.DoesNotContain("INTERESTS-V2", original.System);
        Assert.Contains("GUIDANCE-V1", original.System);
        Assert.DoesNotContain("CORE-V2", original.System);
    }

    [Theory]
    [InlineData("personalityCore")]
    [InlineData("participationGuidance")]
    [InlineData("usePersonalityCore")]
    [InlineData("useStateGuidance")]
    [InlineData("interestProfile")]
    [InlineData("useInterestProfile")]
    public async Task MalformedContextConfigurationCannotExecute(string field)
    {
        await using var runtime = new SessionFlowRuntime(TimeProvider.System, new RecordingCompletion([]));
        var project = SessionSliceAcceptanceSupport.Project("bad", root =>
        {
            SessionSliceAcceptanceSupport.Configure(root, null, 1, null);
            if (field is "personalityCore" or "interestProfile") root["project"]![field] = 123;
            else foreach (var state in SessionSliceAcceptanceSupport.States(root))
                if (field == "participationGuidance") state[field] = 123;
                else SessionSliceAcceptanceSupport.Node(SessionSliceAcceptanceSupport.Graph(state), "ai")[field] = "true";
        });
        Assert.Throws<SessionFlowProjectException>(() => runtime.UpdateProject(project));
    }

    private static SessionFlowProject Project(string revision, bool personality, bool guidance, long version = 1, int maxMessages = 1, double? maxWait = null, bool interests = false) =>
        SessionSliceAcceptanceSupport.Project(revision, root =>
        {
            SessionSliceAcceptanceSupport.Configure(root, null, maxMessages, maxWait, prompt: "RESPONSIBILITY");
            root["project"]!["personalityCore"] = "CORE-" + revision.ToUpperInvariant();
            root["project"]!["interestProfile"] = "INTERESTS-" + revision.ToUpperInvariant();
            foreach (var state in SessionSliceAcceptanceSupport.States(root))
            {
                state["participationGuidance"] = "GUIDANCE-" + revision.ToUpperInvariant();
                var prompt = SessionSliceAcceptanceSupport.Node(SessionSliceAcceptanceSupport.Graph(state), "ai");
                prompt["usePersonalityCore"] = personality;
                prompt["useStateGuidance"] = guidance;
                prompt["useInterestProfile"] = interests;
            }
        }, version);
}
