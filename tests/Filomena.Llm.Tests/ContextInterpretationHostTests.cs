using System.Text.Json;
using Filomena.App;
using Filomena.Core.Flows.Sessions;
using Filomena.Core.Identity;
using Filomena.Core.Shadow;
using Xunit;

namespace Filomena.Llm.Tests;

public sealed class ContextInterpretationHostTests
{
    [Theory]
    [InlineData("valid")]
    [InlineData("live")]
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
        var config = new ContextInterpretationHostConfig("contextual-interpretation:v1", SessionParticipationReviewTests.Definition(),
            ContextInterpretationAnalyzerTests.Settings, 150, 2, 10, true);
        config = mutation switch
        {
            "version" => config with { Version = "future" },
            "revision" => config with { Participation = config.Participation with { ProjectRevision = "other" } },
            "limits" => config with { AttemptBudget = 0 },
            "model" => config with { Model = config.Model with { Model = "" } },
            "missing-node" => config with { Participation = config.Participation with { PromptNodeIds = ["absent"] } },
            _ => config
        };
        var error = Record.Exception(() => ContextInterpretationHost.Validate(config, pin, binding, mutation == "live"));
        if (mutation == "valid") Assert.Null(error);
        else Assert.IsType<InvalidDataException>(error);
        ContextInterpretationHost.Validate(null, pin, binding, true); // Existing hosts remain unchanged.
    }
}
