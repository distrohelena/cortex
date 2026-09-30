namespace Filomena.Llm.LmStudio;

public interface IResponseChatClient
{
    Task<string> CompleteResponseAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken);
}

public interface IModelResponseChatClient : IResponseChatClient
{
    Task<string> CompleteResponseAsync(string systemPrompt, string userPrompt, string model, CancellationToken cancellationToken);
}
