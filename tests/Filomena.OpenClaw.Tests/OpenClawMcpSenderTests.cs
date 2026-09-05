using System.Text.Json;
using Filomena.OpenClaw.Mcp;
using Xunit;

namespace Filomena.OpenClaw.Tests;

public sealed class OpenClawMcpSenderTests
{
    [Fact]
    public async Task ReusesOneConnectionForMultipleSends()
    {
        var client = new Client();
        var connections = 0;
        await using var sender = new OpenClawMcpSender(_ =>
        {
            connections++;
            return Task.FromResult<IMcpToolClient>(client);
        });
        await sender.SendTextAsync("a", "oi", TestContext.Current.CancellationToken);
        await sender.SendTextAsync("b", "ola", TestContext.Current.CancellationToken);
        Assert.Equal(1, connections);
        Assert.Equal(2, client.Calls);
    }

    [Fact]
    public async Task ReconnectsForNextTurnButNeverReplaysFailedSend()
    {
        var failed = new Client { Fail = true };
        var working = new Client();
        var connections = 0;
        await using var sender = new OpenClawMcpSender(_ =>
            Task.FromResult<IMcpToolClient>(++connections == 1 ? failed : working));
        await Assert.ThrowsAsync<IOException>(() => sender.SendTextAsync("a", "uncertain", TestContext.Current.CancellationToken));
        Assert.Equal(1, connections);
        Assert.Equal(1, failed.Calls);
        await sender.SendTextAsync("b", "new turn", TestContext.Current.CancellationToken);
        Assert.Equal(2, connections);
        Assert.Equal(1, working.Calls);
        Assert.True(failed.Disposed);
    }

    private sealed class Client : IMcpToolClient
    {
        public int Calls { get; private set; }
        public bool Fail { get; init; }
        public bool Disposed { get; private set; }
        public Task<IReadOnlyList<McpToolDescriptor>> ListToolsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<McpToolDescriptor>>([]);
        public Task<JsonElement> CallToolAsync(string name, IReadOnlyDictionary<string, object?> arguments, CancellationToken cancellationToken)
        {
            Calls++;
            if (Fail) throw new IOException("disconnected after dispatch");
            return Task.FromResult(JsonSerializer.SerializeToElement(new { result = new { messageId = "sent" } }));
        }
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }

    [Fact]
    public async Task FailedSendDoesNotDisposeConnectionWhileAnotherChatIsSending()
    {
        var client = new ConcurrentClient();
        await using var sender = new OpenClawMcpSender(_ => Task.FromResult<IMcpToolClient>(client));
        var first = sender.SendTextAsync("a", "one", TestContext.Current.CancellationToken);
        var second = sender.SendTextAsync("b", "two", TestContext.Current.CancellationToken);
        await client.BothEntered.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        client.First.SetException(new IOException("first failed"));
        await Assert.ThrowsAsync<IOException>(() => first);
        Assert.False(client.Disposed);
        client.Second.SetResult(JsonSerializer.SerializeToElement(new { result = new { messageId = "second-sent" } }));
        Assert.Equal("second-sent", (await second).ProviderMessageId);
        Assert.True(client.Disposed);
    }

    private sealed class ConcurrentClient : IMcpToolClient
    {
        private int calls;
        public bool Disposed { get; private set; }
        public TaskCompletionSource BothEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<JsonElement> First { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<JsonElement> Second { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<IReadOnlyList<McpToolDescriptor>> ListToolsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<McpToolDescriptor>>([]);
        public Task<JsonElement> CallToolAsync(string name, IReadOnlyDictionary<string, object?> arguments, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref calls) == 2) BothEntered.SetResult();
            return ((string?)arguments["session_key"] == "a" ? First.Task : Second.Task).WaitAsync(cancellationToken);
        }
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }
}
