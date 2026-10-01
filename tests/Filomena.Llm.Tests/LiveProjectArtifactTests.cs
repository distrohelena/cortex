using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Cortex.Core.Shadow;
using Xunit;

namespace Filomena.Llm.Tests;

public sealed class LiveProjectArtifactTests
{
    [Fact]
    public void Optional_group_reply_artifact_compiles_without_changing_other_brain_nodes()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Filomena.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        var baseline = JsonNode.Parse(File.ReadAllBytes(Path.Combine(root.FullName, "docs/manual-group-live-project-v3-punctuation-v2.json")))!;
        var file = Path.Combine(root.FullName, "docs/manual-group-live-project-v3-optional-reply-v1.json");
        Assert.True(File.Exists(file));
        var bytes = File.ReadAllBytes(file);
        var document = JsonNode.Parse(bytes)!;
        var changes = 0;
        foreach (var state in document["project"]!["states"]!.AsArray())
            foreach (var node in state!["graph"]!["nodes"]!.AsArray())
                if (node!["id"]!.GetValue<string>() is "group-generation" or "group-regeneration")
                {
                    var baselineNode = baseline["project"]!["states"]!.AsArray()
                        .Single(s => s!["id"]!.GetValue<string>() == state["id"]!.GetValue<string>())!["graph"]!["nodes"]!.AsArray()
                        .Single(n => n!["id"]!.GetValue<string>() == node["id"]!.GetValue<string>())!;
                    Assert.StartsWith(baselineNode["prompt"]!.GetValue<string>(), node["prompt"]!.GetValue<string>());
                    var fields = node["responseFields"]!.AsArray();
                    Assert.Equal(2, fields.Count);
                    Assert.Equal("replyTo", fields[1]!["id"]!.GetValue<string>());
                    Assert.Equal("text?", fields[1]!["type"]!.GetValue<string>());
                    fields.RemoveAt(1);
                    node["outputMode"] = "text";
                    node["prompt"] = baselineNode["prompt"]!.DeepClone();
                    changes++;
                }
        Assert.Equal(6, changes);
        Assert.True(JsonNode.DeepEquals(baseline, document));
        _ = PinnedShadowProject.FromBytes(bytes, "group-optional-reply-v1", 3, Convert.ToHexString(SHA256.HashData(bytes)));
    }

    [Fact]
    public void Group_punctuation_revision_changes_only_generation_and_regeneration_style()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Filomena.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        var baseline = JsonNode.Parse(File.ReadAllBytes(Path.Combine(root.FullName, "docs/manual-group-shadow-project-v3-gpt6-luna.json")))!;
        var file = Path.Combine(root.FullName, "docs/manual-group-live-project-v3-punctuation-v2.json");
        Assert.True(File.Exists(file));
        var bytes = File.ReadAllBytes(file);
        var document = JsonNode.Parse(bytes)!;
        var changed = 0;
        const string after = "\n\nPONTUAÇÃO DE CONVERSA\n\nNão use travessão na fala comum. Prefira ponto, vírgula ou quebra de linha. Não substitua travessões sistematicamente por ponto e vírgula. Preserve a voz, o sentido e a precisão, sem forçar frases picotadas. Mantenha a pontuação necessária em código e citações.";
        foreach (var state in document["project"]!["states"]!.AsArray())
            foreach (var node in state!["graph"]!["nodes"]!.AsArray())
                if (node!["id"]!.GetValue<string>() is "group-generation" or "group-regeneration")
                {
                    var prompt = node["prompt"]!.GetValue<string>();
                    Assert.EndsWith(after, prompt);
                    node["prompt"] = prompt[..^after.Length];
                    changed++;
                }
        Assert.Equal(6, changed);
        Assert.True(JsonNode.DeepEquals(baseline, document));
        _ = PinnedShadowProject.FromBytes(bytes, "group-punctuation-v2", 3, Convert.ToHexString(SHA256.HashData(bytes)));
    }

    [Fact]
    public void Conversational_variant_only_adds_DM_punctuation_guidance_and_remains_executable()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Filomena.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        var file = Path.Combine(root.FullName, "docs/manual-live-project-v3-conversational.json");
        Assert.True(File.Exists(file), "Approved conversational-punctuation variant must exist.");
        var baseline = JsonNode.Parse(File.ReadAllBytes(Path.Combine(root.FullName, "docs/manual-live-project-v3-continuity.json")))!;
        var bytes = File.ReadAllBytes(file);
        var document = JsonNode.Parse(bytes)!;
        const string clause = "\n\nPONTUAÇÃO DE CONVERSA\n\nPrefira frases curtas, vírgulas e quebras de linha naturais, como numa conversa por mensagem. Evite usar travessões como pausa padrão. Use-os só quando realmente fizerem sentido. Não os substitua sistematicamente por ponto e vírgula, que também tende a soar formal. Preserve a voz, o sentido e a precisão, sem forçar frases picotadas. Mantenha a pontuação necessária em código e citações.";
        var changed = 0;
        foreach (var state in document["project"]!["states"]!.AsArray())
            foreach (var node in state!["graph"]!["nodes"]!.AsArray())
                if (node!["id"]!.GetValue<string>() is "dm-generation" or "dm-regeneration")
                {
                    var prompt = node["prompt"]!.GetValue<string>();
                    Assert.EndsWith(clause, prompt);
                    node["prompt"] = prompt[..^clause.Length];
                    changed++;
                }
        Assert.Equal(6, changed);
        // Catches accidental changes to model, grounding, FSM, group paths,
        // enforcement, context limits or send routing while editing prompts.
        Assert.True(JsonNode.DeepEquals(baseline, document));
        _ = PinnedShadowProject.FromBytes(bytes, "filomena-v3-live-conversational", 3,
            Convert.ToHexString(SHA256.HashData(bytes)));
    }

    [Fact]
    public void Live_artifact_only_connects_existing_DM_transitions_to_confirmed_sent()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Filomena.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        var frozen = File.ReadAllBytes(Path.Combine(root.FullName, "docs/manual-shadow-project-v3.json"));
        Assert.Equal("47D1D4521BCF013E919DDCB3DE5900485DDA7F8BACEFA7437FEDFBDFFC9BC55E", Convert.ToHexString(SHA256.HashData(frozen)));
        var live = File.ReadAllBytes(Path.Combine(root.FullName, "docs/manual-live-project-v3-sent.json"));
        var document = JsonNode.Parse(live)!;
        var changed = 0;
        foreach (var state in document["project"]!["states"]!.AsArray())
            foreach (var edge in state!["graph"]!["controlEdges"]!.AsArray())
                if (edge!["source"]!.GetValue<string>() is "dm-transition-responder-shadow-send" or "dm-accepted-1-shadow-send" or "dm-accepted-2-shadow-send")
                {
                    Assert.Equal("sent", edge["sourcePort"]!.GetValue<string>());
                    edge["sourcePort"] = "shadowed";
                    changed++;
                }
        Assert.Equal(9, changed);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(frozen), document));
        _ = PinnedShadowProject.FromBytes(live, "filomena-v3-live-sent", 3, Convert.ToHexString(SHA256.HashData(live)));
    }

    [Fact]
    public void Continuity_variant_only_removes_the_three_DM_age_cutoffs()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Filomena.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        var baseline = JsonNode.Parse(File.ReadAllBytes(Path.Combine(root.FullName, "docs/manual-live-project-v3-sent.json")));
        var bytes = File.ReadAllBytes(Path.Combine(root.FullName, "docs/manual-live-project-v3-continuity.json"));
        var document = JsonNode.Parse(bytes)!;
        var changed = 0;
        foreach (var state in document["project"]!["states"]!.AsArray())
        {
            var history = state!["graph"]!["nodes"]!.AsArray().Single(n => n!["id"]!.GetValue<string>() == "dm-history")!;
            Assert.Null(history["settings"]!["maxAgeMinutes"]);
            Assert.Equal(20, history["settings"]!["maxMessages"]!.GetValue<int>());
            Assert.Equal(5000, history["settings"]!["maxCharacters"]!.GetValue<int>());
            history["settings"]!["maxAgeMinutes"] = 15;
            changed++;
        }
        Assert.Equal(3, changed);
        Assert.True(JsonNode.DeepEquals(baseline, document));
        _ = PinnedShadowProject.FromBytes(bytes, "filomena-v3-live-continuity", 3, Convert.ToHexString(SHA256.HashData(bytes)));
    }
}
