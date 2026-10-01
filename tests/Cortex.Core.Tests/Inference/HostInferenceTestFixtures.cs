using System.Text.Json;
using Cortex.Core.Flows;
using Cortex.Core.Inference;

namespace Cortex.Core.Tests.Inference;

internal static class HostInferenceTestFixtures
{
    internal static readonly InferenceScope Scope = new("application-a");
    internal static readonly InferenceCorrelation Correlation = new("run", "step", "config-r1");
    internal static FlowCompletionRequest Request => new("step", "private prompt",
        JsonSerializer.Deserialize<JsonElement>("{\"project_id\":\"forged\",\"role\":\"admin\"}"),
        JsonSerializer.Deserialize<JsonElement>("{\"type\":\"object\",\"properties\":{\"choice\":{\"type\":\"string\",\"enum\":[\"accept\",\"reject\"]}},\"required\":[\"choice\"],\"additionalProperties\":false}"),
        "test-model", "test-provider", null, "high", 128);

    internal static HostInferenceProfile Profile(IFlowCompletion adapter) => new("p", "profile-r1", adapter, Request, Validate);

    private static void Validate(string output)
    {
        using var doc = JsonDocument.Parse(output);
        FlowDomain.Check(doc.RootElement, FlowType.ParseJsonSchema(Request.ResponseSchema!.Value));
        if (doc.RootElement.EnumerateObject().Count() != 1) throw new InvalidDataException("unexpected property");
    }

    internal sealed class Completion(Func<FlowCompletionRequest, CancellationToken, Task<string>>? complete = null) : IFlowCompletion
    {
        private int calls;
        internal int Calls => Volatile.Read(ref calls);
        public Task<string> CompleteAsync(FlowCompletionRequest request, CancellationToken token)
        {
            Interlocked.Increment(ref calls);
            return complete?.Invoke(request, token) ?? Task.FromResult("{\"choice\":\"accept\"}");
        }
    }
}
