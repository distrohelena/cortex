using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using Filomena.App.FlowRuntime;
using Filomena.Core.Conversations;
using Filomena.Llm.LmStudio;
using Xunit;

namespace Filomena.Llm.Tests;

public sealed class DmFlowExecutorTests
{
    [Fact]
    public async Task CompiledDmFlowSendsTheModelResultToTheObservedDmRoute()
    {
        using var document = JsonDocument.Parse("""
        {
          "format": "filomena-local-project",
          "project": {
            "version": 1,
            "stateScope": "session",
            "initialStateId": "state-1",
            "states": [{
              "id": "state-1",
              "name": "Inicial",
              "graph": {
                "version": 1,
                "nodes": [
                  {"id": "dm", "kind": "dm", "x": 0, "y": 0},
                  {"id": "p1", "kind": "prompt", "prompt": "Responda", "provider": "lmstudio", "model": "modelo-a", "x": 200, "y": 0},
                  {"id": "send", "kind": "send", "x": 400, "y": 0}
                ],
                "edges": [
                  {"source": "dm", "sourcePort": "message", "target": "p1", "targetPort": "message"},
                  {"source": "p1", "sourcePort": "message", "target": "send", "targetPort": "message"},
                  {"source": "dm", "sourcePort": "from", "target": "send", "targetPort": "to"}
                ]
              }
            }]
          }
        }
        """);
        var model = new FakeModel("Resposta");
        var sender = new FakeSender("receipt-1");
        var executor = new DmFlowExecutor(model, sender);

        var result = await executor.ExecuteAsync(
            DmFlowCompiler.Compile(document.RootElement),
            Message(),
            send: true,
            TestContext.Current.CancellationToken);

        Assert.Equal("Resposta", result.Text);
        Assert.Equal("receipt-1", result.OutboundMessageId);
        Assert.Equal(("agent:main:whatsapp:default:direct:5511944558218@s.whatsapp.net", "Resposta"), sender.LastCall);
        Assert.Equal(("Responda", "Oi", "modelo-a"), model.LastCall);
    }

    [Fact]
    public async Task SendFalseGeneratesTextWithoutCallingTheSender()
    {
        using var document = ProjectDocument();
        var model = new FakeModel("Resposta");
        var sender = new FakeSender("receipt-1");

        var result = await new DmFlowExecutor(model, sender).ExecuteAsync(
            DmFlowCompiler.Compile(document.RootElement), Message(), false,
            TestContext.Current.CancellationToken);

        Assert.Equal("Resposta", result.Text);
        Assert.Null(result.OutboundMessageId);
        Assert.Empty(sender.Calls);
    }

    [Fact]
    public async Task CancellationBeforeInferenceCallsNeitherModelNorSender()
    {
        using var document = ProjectDocument();
        var model = new FakeModel("Resposta");
        var sender = new FakeSender("receipt-1");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => new DmFlowExecutor(model, sender).ExecuteAsync(
            DmFlowCompiler.Compile(document.RootElement), Message(), true, cancellation.Token));

        Assert.Empty(model.Calls);
        Assert.Empty(sender.Calls);
    }

    [Fact]
    public async Task DivergentSenderIsRejectedBeforeInference()
    {
        using var document = ProjectDocument();
        var model = new FakeModel("Resposta");
        var sender = new FakeSender("receipt-1");

        await Assert.ThrowsAsync<InvalidDataException>(() => new DmFlowExecutor(model, sender).ExecuteAsync(
            DmFlowCompiler.Compile(document.RootElement), Message("5511987654321@s.whatsapp.net"), true,
            TestContext.Current.CancellationToken));

        Assert.Empty(model.Calls);
        Assert.Empty(sender.Calls);
    }

    [Fact]
    public void ScalarProjectDocumentIsRejectedAsInvalidData()
    {
        using var document = JsonDocument.Parse("\"not-an-object\"");

        Assert.Throws<InvalidDataException>(() => DmFlowCompiler.Compile(document.RootElement));
    }

    [Fact]
    public void ScalarNodeIsRejectedAsInvalidData()
    {
        using var document = JsonDocument.Parse("""
        {
          "format": "filomena-local-project",
          "project": {
            "version": 1,
            "stateScope": "session",
            "initialStateId": "state-1",
            "states": [{
              "id": "state-1",
              "graph": {
                "version": 1,
                "nodes": [1],
                "edges": []
              }
            }]
          }
        }
        """);

        Assert.Throws<InvalidDataException>(() => DmFlowCompiler.Compile(document.RootElement));
    }

    [Theory]
    [InlineData("missing-to")]
    [InlineData("missing-message")]
    [InlineData("incompatible-port")]
    [InlineData("duplicate-wire")]
    [InlineData("cycle")]
    [InlineData("disconnected")]
    [InlineData("unsupported-node")]
    [InlineData("unsupported-provider")]
    public void MalformedGraphIsRejectedBeforeItCanBeExecuted(string defect)
    {
        using var document = ProjectDocument(defect, defect == "cycle" || defect == "disconnected" ? 2 : 1);

        Assert.Throws<InvalidDataException>(() => DmFlowCompiler.Compile(document.RootElement));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EmptyOrFailedModelResultNeverSends(bool modelFails)
    {
        using var document = ProjectDocument();
        var model = new FakeModel(modelFails ? "unused" : "", modelFails ? new InvalidOperationException("model failed") : null);
        var sender = new FakeSender("receipt-1");

        var exception = await Record.ExceptionAsync(() => new DmFlowExecutor(model, sender).ExecuteAsync(
            DmFlowCompiler.Compile(document.RootElement), Message(), true,
            TestContext.Current.CancellationToken));

        Assert.Equal(modelFails ? typeof(InvalidOperationException) : typeof(InvalidDataException), exception?.GetType());
        Assert.Empty(sender.Calls);
    }

    [Fact]
    public async Task UncertainSendFailsOnceWithoutRetry()
    {
        using var document = ProjectDocument();
        var model = new FakeModel("Resposta");
        var sender = new FakeSender("receipt-1", new IOException("delivery uncertain"));

        await Assert.ThrowsAsync<IOException>(() => new DmFlowExecutor(model, sender).ExecuteAsync(
            DmFlowCompiler.Compile(document.RootElement), Message(), true,
            TestContext.Current.CancellationToken));

        Assert.Single(sender.Calls);
    }

    [Fact]
    public async Task ConcurrentExecutionsKeepPromptInputsAndModelSelectionIsolated()
    {
        using var document = ProjectDocument(promptCount: 2);
        var model = new ConcurrentModel();
        var sender = new FakeSender("unused");
        var executor = new DmFlowExecutor(model, sender);
        var flow = DmFlowCompiler.Compile(document.RootElement);

        var first = executor.ExecuteAsync(flow, Message("5511944558218@s.whatsapp.net", "one", "message-one"), false,
            TestContext.Current.CancellationToken);
        await model.FirstCallStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        var second = executor.ExecuteAsync(flow, Message("5511944558218@s.whatsapp.net", "two", "message-two"), false,
            TestContext.Current.CancellationToken);
        await model.SecondCallStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        model.ReleaseFirstPrompts.TrySetResult();

        var results = await Task.WhenAll(first, second);

        Assert.Contains(results, result => result.Text == "model-b:model-a:one");
        Assert.Contains(results, result => result.Text == "model-b:model-a:two");
        Assert.Equal(4, model.Calls.Count);
        Assert.Equal(2, model.Calls.Count(call => call.Model == "model-a"));
        Assert.Equal(2, model.Calls.Count(call => call.Model == "model-b"));
        Assert.Contains(model.Calls, call => call.Model == "model-a" && call.UserPrompt == "one");
        Assert.Contains(model.Calls, call => call.Model == "model-a" && call.UserPrompt == "two");
        Assert.Contains(model.Calls, call => call.Model == "model-b" && call.UserPrompt == "model-a:one");
        Assert.Contains(model.Calls, call => call.Model == "model-b" && call.UserPrompt == "model-a:two");
        Assert.Empty(sender.Calls);
    }

    [Fact]
    public async Task GroupNativeSuffixIsRejectedBeforeInferenceEvenWhenKindSaysDirectMessage()
    {
        using var document = ProjectDocument();
        var model = new FakeModel("Resposta");
        var sender = new FakeSender("receipt-1");
        var message = Message(
            senderNativeId: "12345@g.us",
            providerMessageId: "group-shaped-message",
            conversationNativeId: "12345@g.us",
            kind: ConversationKind.DirectMessage,
            conversationNamespace: "whatsapp-group-12345-g-us");

        await Assert.ThrowsAsync<InvalidDataException>(() => new DmFlowExecutor(model, sender).ExecuteAsync(
            DmFlowCompiler.Compile(document.RootElement), message, true,
            TestContext.Current.CancellationToken));

        Assert.Empty(model.Calls);
        Assert.Empty(sender.Calls);
    }

    private static JsonDocument ProjectDocument(string? defect = null, int promptCount = 1)
    {
        var nodes = new JsonArray
        {
            new JsonObject { ["id"] = "dm", ["kind"] = "dm" }
        };
        for (var index = 1; index <= promptCount; index++)
        {
            nodes.Add(new JsonObject
            {
                ["id"] = $"p{index}",
                ["kind"] = "prompt",
                ["prompt"] = $"Prompt {index}",
                ["provider"] = "lmstudio",
                ["model"] = index == 1 ? "model-a" : "model-b"
            });
        }

        nodes.Add(new JsonObject { ["id"] = "send", ["kind"] = "send" });
        var edges = new JsonArray
        {
            new JsonObject { ["source"] = "dm", ["sourcePort"] = "message", ["target"] = "p1", ["targetPort"] = "message" }
        };
        for (var index = 1; index < promptCount; index++)
        {
            edges.Add(new JsonObject
            {
                ["source"] = $"p{index}", ["sourcePort"] = "message",
                ["target"] = $"p{index + 1}", ["targetPort"] = "message"
            });
        }

        edges.Add(new JsonObject
        {
            ["source"] = $"p{promptCount}", ["sourcePort"] = "message",
            ["target"] = "send", ["targetPort"] = "message"
        });
        edges.Add(new JsonObject { ["source"] = "dm", ["sourcePort"] = "from", ["target"] = "send", ["targetPort"] = "to" });

        if (defect is not null)
        {
            switch (defect)
            {
                case "missing-to":
                    edges[edges.Count - 1] = new JsonObject { ["source"] = "dm", ["target"] = "send" };
                    break;
                case "missing-message":
                    edges.RemoveAt(promptCount);
                    break;
                case "incompatible-port":
                    edges[0]!["sourcePort"] = "from";
                    break;
                case "duplicate-wire":
                    edges.Add(edges[0]!.DeepClone());
                    break;
                case "cycle":
                    edges[1]!["target"] = "p1";
                    break;
                case "disconnected":
                    edges[1]!["target"] = "send";
                    break;
                case "unsupported-node":
                    nodes[1]!["kind"] = "branch";
                    break;
                case "unsupported-provider":
                    nodes[1]!["provider"] = "other-provider";
                    break;
            }
        }

        var root = new JsonObject
        {
            ["format"] = "filomena-local-project",
            ["project"] = new JsonObject
            {
                ["version"] = 1,
                ["stateScope"] = "session",
                ["initialStateId"] = "state-1",
                ["states"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["id"] = "state-1",
                        ["name"] = "Inicial",
                        ["graph"] = new JsonObject { ["version"] = 1, ["nodes"] = nodes, ["edges"] = edges }
                    }
                }
            }
        };
        return JsonDocument.Parse(root.ToJsonString());
    }

    private static InboundConversationEvent Message(
        string? senderNativeId = null,
        string content = "Oi",
        string providerMessageId = "message-1",
        string? conversationNativeId = null,
        ConversationKind kind = ConversationKind.DirectMessage,
        string? conversationNamespace = null)
    {
        const string nativeId = "5511944558218@s.whatsapp.net";
        conversationNativeId ??= nativeId;
        senderNativeId ??= conversationNativeId;
        conversationNamespace ??= "whatsapp-dm-5511944558218-s-whatsapp-net";
        var conversationId = new ConversationId(
            "whatsapp", "default", conversationNativeId, conversationNamespace);
        return new InboundConversationEvent(
            1,
            "agent:main:whatsapp:default:direct:5511944558218@s.whatsapp.net",
            new ConversationInfo(conversationId, "agent:main:whatsapp:default:direct:5511944558218@s.whatsapp.net", kind, null, null, null, null, DateTimeOffset.UtcNow),
            providerMessageId,
            content,
            new ActorRef("whatsapp", "default", senderNativeId, "whatsapp:" + senderNativeId),
            DateTimeOffset.UtcNow,
            null,
            1,
            new Dictionary<string, object?>());
    }

    private sealed class FakeModel(string response, Exception? failure = null) : IModelResponseChatClient
    {
        public (string SystemPrompt, string UserPrompt, string Model) LastCall { get; private set; }
        public List<(string SystemPrompt, string UserPrompt, string? Model)> Calls { get; } = [];

        public Task<string> CompleteResponseAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken) =>
            Complete(systemPrompt, userPrompt, null);

        public Task<string> CompleteResponseAsync(string systemPrompt, string userPrompt, string model, CancellationToken cancellationToken)
        {
            LastCall = (systemPrompt, userPrompt, model);
            return Complete(systemPrompt, userPrompt, model);
        }

        private Task<string> Complete(string systemPrompt, string userPrompt, string? model)
        {
            Calls.Add((systemPrompt, userPrompt, model));
            if (failure is not null) return Task.FromException<string>(failure);
            return Task.FromResult(response);
        }
    }

    private sealed class FakeSender(string receiptId, Exception? failure = null) : IConversationSender
    {
        public (string SessionKey, string Text) LastCall { get; private set; }
        public List<(string SessionKey, string Text)> Calls { get; } = [];

        public Task<SendReceipt> SendTextAsync(string sessionKey, string text, CancellationToken cancellationToken)
        {
            LastCall = (sessionKey, text);
            Calls.Add((sessionKey, text));
            if (failure is not null) return Task.FromException<SendReceipt>(failure);
            return Task.FromResult(new SendReceipt(receiptId));
        }
    }

    private sealed class ConcurrentModel : IModelResponseChatClient
    {
        private int callNumber;

        public ConcurrentQueue<(string Model, string UserPrompt)> Calls { get; } = [];
        public TaskCompletionSource FirstCallStarted { get; } = NewSignal();
        public TaskCompletionSource SecondCallStarted { get; } = NewSignal();
        public TaskCompletionSource ReleaseFirstPrompts { get; } = NewSignal();

        public Task<string> CompleteResponseAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("A model must be selected per prompt.");

        public async Task<string> CompleteResponseAsync(string systemPrompt, string userPrompt, string model, CancellationToken cancellationToken)
        {
            Calls.Enqueue((model, userPrompt));
            switch (Interlocked.Increment(ref callNumber))
            {
                case 1:
                    FirstCallStarted.TrySetResult();
                    break;
                case 2:
                    SecondCallStarted.TrySetResult();
                    break;
            }

            if (Calls.Count <= 2) await ReleaseFirstPrompts.Task.WaitAsync(cancellationToken);
            return $"{model}:{userPrompt}";
        }

        private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
