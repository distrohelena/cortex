using System.Text.Json;
using Cortex.Core.Flows;

namespace Cortex.Video;

public static class TakeSuggestionNode
{
    private static readonly FlowType Text = new StringType();
    private static readonly FlowType Number = new NumberType();
    private static ObjectType Object(params (string Name, FlowType Type)[] fields) => new(fields.ToDictionary(field => field.Name, field => new FlowField(field.Type, true)));
    public static ObjectType EditPlanType { get; } = Object(
        ("version", Number), ("status", new EnumType(["draft"])), ("timebase", new EnumType(["sourceSeconds"])),
        ("assessmentBasis", new EnumType(["transcriptOnly"])), ("goal", Text), ("sourceTranscriptHash", Text), ("provider", Text), ("model", Text),
        ("candidates", new ArrayType(Object(("id", Text), ("groupId", Text), ("source", Object(("start", Number), ("end", Number), ("segmentIds", new ArrayType(Number)))),
            ("text", Text), ("assessment", Object(("deliveryScore", Number), ("contentScore", Number))), ("keep", new BooleanType()), ("reason", Text)))),
        ("groups", new ArrayType(Object(("id", Text), ("candidateIds", new ArrayType(Text)), ("suggestedId", Text)))),
        ("repeatEvidence", new ArrayType(Object(("firstId", Text), ("secondId", Text), ("sameSpeech", new BooleanType()), ("probability", Number)))),
        ("proposedTimeline", new ArrayType(Object(("candidateId", Text), ("sourceStart", Number), ("sourceEnd", Number), ("start", Number), ("end", Number)))));

    public static FlowNodeHandler Create(ITakeSuggester suggester) => new(
        new NodeDefinition("suggestTakes", 1, FlowEffect.Model,
            dataInputs: [new("transcript", VideoNodes.TranscriptType, Required: true)], dataOutputs: [new("editPlan", EditPlanType)],
            controlInputs: [new("exec")], controlOutputs: [new("then")],
            configFields: [new("goal", Text, Required: true) { Label = "Objetivo editorial" }]) { DisplayName = "Sugerir Takes" },
        async (config, inputs, token) =>
        {
            var transcript = inputs["transcript"].Value.Deserialize<TranscriptResult>(VideoNodes.JsonOptions) ?? throw new InvalidDataException("Transcrição indisponível.");
            var plan = await suggester.SuggestAsync(transcript, config.GetProperty("goal").GetString()!, token);
            return new Dictionary<string, JsonElement> { ["editPlan"] = JsonSerializer.SerializeToElement(plan, VideoNodes.JsonOptions) };
        });
}
