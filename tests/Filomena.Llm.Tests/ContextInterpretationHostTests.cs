using System.Text.Json;
using System.Text.Json.Serialization;
using Filomena.App;
using Filomena.Core.Flows.Sessions;
using Filomena.Core.Identity;
using Filomena.Core.Shadow;
using Xunit;

namespace Filomena.Llm.Tests;

public sealed class ContextInterpretationHostTests
{
    internal static ContextInterpretationHostConfig ReadOperationalConfig(bool preview) =>
        JsonSerializer.Deserialize<NativeShadowConfig>("""
        {
          "manualConfigFile":"fixture.json", "observationEndpoint":"http://127.0.0.1:1",
          "observationTokenEnvironmentVariable":"FIXTURE_UNUSED", "observationSessionRef":"fixture",
          "nativeConversationId":"fixture-group@g.us", "approvedRevision":"gateway-fixture",
          "journalDirectory":"fixture-unused",
          "contextInterpretation": {
            "version":"contextual-interpretation:v1",
            "participation": {
              "projectRevision":"review-v3",
              "promptNodeIds":["group-situation","group-presence"],
              "boundaries":[
                {"nodeId":"group-direct-involvement","exit":"direct_involvement_rule","decision":0},
                {"nodeId":"group-action","exit":"case-responder","decision":0},
                {"nodeId":"group-action","exit":"case-esperar","decision":1},
                {"nodeId":"group-action","exit":"case-calar","decision":2}
              ]
            },
            "model": {
              "model":"gpt-6-luna","provider":"openai","reasoningEffort":"medium",
              "maxOutputTokens":2048,"beforeMessages":8,"afterMessages":8,"maxTextCharacters":2000
            },
            "messageHorizon":150,"attemptBudget":2,"capacity":10,"previewResponses":true
          }
        }
        """, new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            RespectRequiredConstructorParameters = true,
            RespectNullableAnnotations = true
        })!.ContextInterpretation! with
        { PreviewResponses = preview };

    [Theory]
    [InlineData("valid")]
    [InlineData("live")]
    [InlineData("live-preview")]
    [InlineData("shadow-send")]
    [InlineData("dm")]
    [InlineData("version")]
    [InlineData("revision")]
    [InlineData("limits")]
    [InlineData("model")]
    [InlineData("missing-node")]
    public void Host_config_pins_shadow_group_scope_and_rejects_unsupported_authority(string mutation)
    {
        var pin = new PinnedShadowProject(new("review-project", 1, "review-v3", JsonSerializer.SerializeToElement(DirectParticipationRuleTests.Project())));
        var session = new SessionKey("fixture", "filomena", "whatsapp", "fixture-account", mutation == "dm" ? "5511000000000@s.whatsapp.net" : "fixture-group@g.us");
        var binding = new ShadowSessionBinding(session, FilomenaIdentity.WhatsAppConversationNamespace(session.NativeConversationId),
            ["self"], ["peer"], mutation == "dm" ? SessionEntryKind.Dm : SessionEntryKind.Group);
        var config = ReadOperationalConfig(mutation != "live");
        if (mutation == "shadow-send") config = config with { RespondToReassessedMessages = true };
        config = mutation switch
        {
            "version" => config with { Version = "future" },
            "revision" => config with { Participation = config.Participation with { ProjectRevision = "other" } },
            "limits" => config with { AttemptBudget = 0 },
            "model" => config with { Model = config.Model with { Model = "" } },
            "missing-node" => config with { Participation = config.Participation with { PromptNodeIds = ["absent"] } },
            _ => config
        };
        var error = Record.Exception(() => ContextInterpretationHost.Validate(config, pin, binding, mutation is "live" or "live-preview"));
        if (mutation is "valid" or "live") Assert.Null(error);
        else Assert.IsType<InvalidDataException>(error);
        ContextInterpretationHost.Validate(null, pin, binding, true); // Existing hosts remain unchanged.
    }
}
