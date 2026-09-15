using System.Text.Json;
using Filomena.Core.Flows;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Filomena.App.Playground;

internal static class TypedFlowEndpoints
{
    public sealed record Request(JsonElement Graph, string EntryNodeId, JsonElement Message, Dictionary<string, string[]>? Responses);
    public static void Map(WebApplication app)
    {
        app.MapPost("/api/flow/typed/validate", (Request request) =>
        {
            try
            {
                var flow = TypedFlowCompiler.Compile(request.Graph, request.EntryNodeId);
                return Results.Ok(new
                {
                    valid = true,
                    issues = System.Array.Empty<TypedFlowIssue>(),
                    contracts = flow.Nodes.Values.Where(n => n.Source.Kind == "prompt").Select(n => new
                    {
                        nodeId = n.Source.Id,
                        system = Filomena.Core.Boundaries.BoundaryEnforcementProfile.Enabled(n.Source.Config)
                            ? Filomena.Core.Boundaries.BoundaryDetectionTemplates.Read(n.Source.Config).SemanticContractDefinition
                            : n.Source.Config.GetProperty("prompt").GetString(),
                        inputs = n.Inputs.Select(p => new { p.Id, p.Name, type = p.Type.ToJsonSchema(), p.Required }),
                        responseSchema = TypedFlowRunner.ResponseSchema(n),
                    })
                });
            }
            catch (TypedFlowException e) { return Results.Ok(new { valid = false, issues = e.Issues }); }
        });
        app.MapPost("/api/flow/typed/run", async (Request request, CancellationToken token) =>
        {
            try
            {
                var flow = TypedFlowCompiler.Compile(request.Graph, request.EntryNodeId);
                var fake = new FixtureCompletion(request.Responses ?? []);
                var result = await TypedFlowRunner.RunAsync(flow, request.Message, fake, token);
                return Results.Ok(new
                {
                    result.Success,
                    result.Steps,
                    outputs = result.Outputs.ToDictionary(n => n.Key,
                    n => n.Value.ToDictionary(p => p.Key, p => new { type = p.Value.Type.ToJsonSchema(), value = p.Value.Value })),
                    calls = fake.Calls
                });
            }
            catch (TypedFlowException e) { return Results.BadRequest(new { error = "Graph não executável.", issues = e.Issues }); }
        });
    }

    // No transport or LLM dependency: this endpoint can only execute supplied fixtures.
    private sealed class FixtureCompletion(Dictionary<string, string[]> responses) : IFlowCompletion
    {
        private readonly Dictionary<string, int> positions = [];
        public List<object> Calls { get; } = [];
        public Task<string> CompleteAsync(FlowCompletionRequest request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var index = positions.GetValueOrDefault(request.NodeId);
            if (!responses.TryGetValue(request.NodeId, out var outputs) || index >= outputs.Length) throw new InvalidDataException("Configure a resposta fake do node " + request.NodeId);
            positions[request.NodeId] = index + 1;
            Calls.Add(new { request.NodeId, request.Inputs, request.ResponseSchema, request.Correction });
            return Task.FromResult(outputs[index]);
        }
    }
}
