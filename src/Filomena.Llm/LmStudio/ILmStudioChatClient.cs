namespace Filomena.Llm.LmStudio;

public interface ILmStudioChatClient
{
    Task<string> CompleteArbitrationJsonAsync(
        string systemPrompt, string userPrompt, System.Text.Json.JsonElement schema,
        CancellationToken cancellationToken) => CompleteArbitrationJsonAsync(systemPrompt, userPrompt, cancellationToken);

    Task<string> CompleteArbitrationJsonAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken);
}
