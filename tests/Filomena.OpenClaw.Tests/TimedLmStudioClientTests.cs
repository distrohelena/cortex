using Filomena.App;
using Filomena.Llm.LmStudio;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Filomena.OpenClaw.Tests;

public sealed class TimedLmStudioClientTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LogsStartThenCompletionWithDurationWithoutPromptOrResponse(bool arbitration)
    {
        var model = new Model();
        var log = new CapturedLog();
        var client = new TimedLmStudioClient(model, model, log);
        var pending = arbitration
            ? client.CompleteArbitrationJsonAsync("secret prompt", "secret content", TestContext.Current.CancellationToken)
            : client.CompleteResponseAsync("secret prompt", "secret content", TestContext.Current.CancellationToken);
        Assert.Single(log.Messages);
        Assert.Contains("started", log.Messages[0]);
        model.Result.SetResult("secret result");
        Assert.Equal("secret result", await pending);
        Assert.Equal(2, log.Messages.Count);
        Assert.Contains("completed", log.Messages[1]);
        Assert.Contains("elapsedMs=", log.Messages[1]);
        Assert.Contains(arbitration ? "arbitration" : "generation", log.Messages[1]);
        Assert.DoesNotContain("secret", string.Join('\n', log.Messages));
    }

    private sealed class Model : ILmStudioChatClient, IResponseChatClient
    {
        public TaskCompletionSource<string> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<string> CompleteArbitrationJsonAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken) => Result.Task.WaitAsync(cancellationToken);
        public Task<string> CompleteResponseAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken) => Result.Task.WaitAsync(cancellationToken);
    }

    private sealed class CapturedLog : ILogger<TimedLmStudioClient>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }
}
