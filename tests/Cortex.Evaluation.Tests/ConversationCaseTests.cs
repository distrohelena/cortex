using System.Text.Json;
using Cortex.Evaluation.Execution;
using Xunit;

namespace Cortex.Evaluation.Tests;

public sealed class ConversationCaseTests
{
    [Fact]
    public void Batch_history_and_reply_use_runtime_message_serialization_without_rubrics()
    {
        var context = Json("""
        {"conversation":"group","history":[{"sender":{"ref":"person-a","name":"Helena"},"text":"Antes"}],
         "currentBatch":[{"sender":{"ref":"person-b","name":"Celso"},"text":"Agora","reply":{"senderRef":"person-a","text":"Antes"}}]}
        """);
        var input = ConversationCaseInput.Build("case-1", context, "filomena");
        Assert.Equal("grupo", input.GetProperty("tipo_conversa").GetString());
        Assert.Equal("Agora", input.GetProperty("mensagens")[0].GetProperty("texto").GetString());
        var prior = input.GetProperty("historico")[0].GetProperty("remetente");
        var quoted = input.GetProperty("mensagens")[0].GetProperty("reply").GetProperty("remetente");
        Assert.Equal(prior.GetProperty("ref").GetString(), quoted.GetProperty("ref").GetString());
        Assert.DoesNotContain("person-a", input.GetRawText());
        Assert.DoesNotContain("case-1", input.GetRawText());
        Assert.Equal(3, input.EnumerateObject().Count());
    }

    [Fact]
    public void Current_self_message_is_not_a_trusted_inbound_fixture()
    {
        var context = Json("""{"conversation":"group","history":[],"currentBatch":[{"sender":{"ref":"filomena","name":"Filomena"},"text":"Resposta pronta"}]}""");
        Assert.Throws<InvalidDataException>(() => ConversationCaseInput.Build("case", context, "filomena"));
    }

    [Fact]
    public void Historical_self_messages_remain_context_without_becoming_current_events()
    {
        var context = Json("""{"conversation":"dm","history":[{"sender":{"ref":"filomena","name":"Filomena"},"text":"Fala anterior"}],"currentBatch":[{"sender":{"ref":"helena","name":"Helena"},"text":"Continue"}]}""");
        var input = ConversationCaseInput.Build("case", context, "filomena");
        Assert.Single(input.GetProperty("mensagens").EnumerateArray());
        Assert.Equal("Filomena", input.GetProperty("historico")[0].GetProperty("remetente").GetProperty("nome").GetString());
    }

    [Fact]
    public void Missing_quote_text_remains_missing_not_reconstructed_from_other_history()
    {
        var context = Json("""{"conversation":"dm","history":[],"currentBatch":[{"sender":{"ref":"helena","name":"Helena"},"text":"Isso aqui","reply":{"senderRef":"unknown-person"}}]}""");
        var quote = ConversationCaseInput.Build("case", context, "filomena").GetProperty("mensagens")[0].GetProperty("reply");
        Assert.Equal(JsonValueKind.Null, quote.GetProperty("texto").ValueKind);
        Assert.Equal("", quote.GetProperty("remetente").GetProperty("nome").GetString());
    }

    [Fact]
    public void Unexpected_context_metadata_is_not_silently_sent_to_model()
    {
        var context = Json("""{"conversation":"dm","history":[],"currentBatch":[],"expected":{"answer":"leaked"}}""");
        Assert.Throws<InvalidDataException>(() => ConversationCaseInput.Build("case", context, "filomena"));
    }

    [Fact]
    public async Task All_development_cases_map_without_sending_grading_fields()
    {
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "conversation-cases.json"), TestContext.Current.CancellationToken));
        var cases = document.RootElement.GetProperty("cases");
        Assert.Equal(25, cases.GetArrayLength());
        foreach (var item in cases.EnumerateArray())
        {
            var input = ConversationCaseInput.Build(item.GetProperty("id").GetString()!, item.GetProperty("context"), "filomena");
            Assert.False(input.TryGetProperty("expected", out _));
            Assert.False(input.TryGetProperty("typedSystem1Expectations", out _));
            Assert.Equal(item.GetProperty("context").GetProperty("currentBatch").GetArrayLength(), input.GetProperty("mensagens").GetArrayLength());
        }
    }

    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();
}
