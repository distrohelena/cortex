namespace Filomena.Llm.LmStudio;

public interface ILmStudioChatClient
{
    Task<string> CompleteArbitrationJsonAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken);
}
