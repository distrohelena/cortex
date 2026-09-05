namespace Filomena.Llm.LmStudio;

public interface IResponseChatClient
{
    Task<string> CompleteResponseAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken);
}
