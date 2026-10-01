using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Filomena.App;
using Filomena.App.FlowRuntime;
using Cortex.Core.Conversations;
using Filomena.Core.Identity;
using Cortex.Inference.LmStudio;
using Cortex.OpenClaw.Bridge;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Filomena.Llm.Tests;

public sealed class DmFlowLiveTests
{
    private const string Peer = "5511944558218@s.whatsapp.net";
    private static JsonElement Project() => JsonSerializer.SerializeToElement(new
    {
        format = "filomena-local-project",
        project = new { version = 1, stateScope = "session", initialStateId = "s", states = new[] {
            new { id = "s", name = "Inicial", graph = new { version = 1, nodes = new object[] {
                new { id = "dm", kind = "dm", x = 0, y = 0 },
                new { id = "p", kind = "prompt", prompt = "Seja breve", x = 100, y = 0 },
                new { id = "send", kind = "send", x = 200, y = 0 }
            }, edges = new[] {
                new { source = "dm", sourcePort = "message", target = "p", targetPort = "message" },
                new { source = "p", sourcePort = "message", target = "send", targetPort = "message" },
                new { source = "dm", sourcePort = "from", target = "send", targetPort = "to" }
            } } }
        } }
    });
    private static InboundConversationEvent Message(string id, string text = "Oi", string peer = Peer, string account = "default", ConversationKind kind = ConversationKind.DirectMessage)
    {
        var native = kind == ConversationKind.Group ? "120363318973251043@g.us" : peer;
        var conversation = new ConversationInfo(new("whatsapp", account, native, FilomenaIdentity.WhatsAppConversationNamespace(native)), "observed-route:"+native, kind, null,null,null,null,null);
        return new(0,conversation.SessionKey,conversation,id,text,new("whatsapp",account,peer,FilomenaIdentity.WhatsAppActorStableId(peer)),DateTimeOffset.UtcNow,null,null,new Dictionary<string,object?>());
    }
    private static DmFlowLiveSession Session(Model model, Sender sender) => new(new DmFlowLiveOptions { AllowedDmNativeId = Peer }, new DmFlowExecutor(model,sender));

    [Fact]
    public async Task GateRejectsInactiveGroupsOtherDmAccountAndUnresolvedSender()
    {
        var model=new Model();var sender=new Sender();using var session=Session(model,sender);
        await session.ObserveAsync(Message("inactive"),TestContext.Current.CancellationToken);
        session.Activate(Project());
        foreach(var message in new[] { Message("group",kind:ConversationKind.Group), Message("other",peer:"5511999999999@s.whatsapp.net"), Message("account",account:"other"), Message("missing") with {Sender=null} })
            await session.ObserveAsync(message,TestContext.Current.CancellationToken);
        Assert.Empty(model.Inputs);Assert.Empty(sender.Sent);
        await session.ObserveAsync(Message("ok"),TestContext.Current.CancellationToken);
        Assert.Equal(new[]{"Oi"},model.Inputs);Assert.Equal("observed-route:"+Peer,sender.Sent.Single().Route);
    }

    [Fact]
    public async Task DuplicateIsNotReplayedAfterReactivationAndInvalidPlanDoesNotReplaceActive()
    {
        var model=new Model();var sender=new Sender();using var session=Session(model,sender);
        session.Activate(Project());await session.ObserveAsync(Message("same","First"),TestContext.Current.CancellationToken);
        session.Activate(Project());
        Assert.Throws<InvalidDataException>(()=>session.Activate(JsonSerializer.SerializeToElement(new{ invalid=true })));
        await session.ObserveAsync(Message("same","Replay"),TestContext.Current.CancellationToken);
        await session.ObserveAsync(Message("second","Second"),TestContext.Current.CancellationToken);
        Assert.Equal(new[]{"First","Second"},model.Inputs);Assert.Equal(2,sender.Sent.Count);
    }

    [Fact]
    public async Task DeactivateCancelsGenerationBeforeSend()
    {
        var model=new Model{Wait=true};var sender=new Sender();using var session=Session(model,sender);
        session.Activate(Project());var run=session.ObserveAsync(Message("waiting"),TestContext.Current.CancellationToken);
        await model.Started.Task.WaitAsync(TestContext.Current.CancellationToken);
        session.Deactivate();await run;
        Assert.Empty(sender.Sent);Assert.False(session.Active);
    }

    [Fact]
    public async Task HttpActivationRequiresExplicitLiveServicesAndSameOriginGuard()
    {
        using var http=new HttpClient();
        await using(var isolated=LocalPromptPlayground.Build(new(),http,0))
        {
            await isolated.StartAsync(TestContext.Current.CancellationToken);
            using var browser=new HttpClient{BaseAddress=new(isolated.Urls.Single())};
            var status=await browser.GetFromJsonAsync<JsonElement>("/api/flow/live",TestContext.Current.CancellationToken);
            Assert.False(status.GetProperty("available").GetBoolean());
        }
        var model=new Model();var sender=new Sender();using var session=Session(model,sender);
        await using var live=LocalPromptPlayground.Build(new(),http,0,configureLiveServices:services=>services.AddSingleton(session));
        await live.StartAsync(TestContext.Current.CancellationToken);
        using var client=new HttpClient{BaseAddress=new(live.Urls.Single())};
        using var denied=await client.PostAsJsonAsync("/api/flow/live/activate",Project(),TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
        client.DefaultRequestHeaders.Add("X-Filomena-Playground","1");
        using var accepted=await client.PostAsJsonAsync("/api/flow/live/activate",Project(),TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK,accepted.StatusCode);Assert.True(session.Active);
        using var invalid=await client.PostAsJsonAsync("/api/flow/live/activate",new { invalid=true },TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest,invalid.StatusCode);Assert.True(session.Active);
        using var stopped=await client.PostAsJsonAsync("/api/flow/live/deactivate",new{},TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK,stopped.StatusCode);Assert.False(session.Active);
        Assert.Empty(model.Inputs);Assert.Empty(sender.Sent);
    }

    [Fact]
    public async Task BridgeDedupDoesNotLetAnotherAccountSuppressAllowedInbound()
    {
        var receiver=new OpenClawInboundBridgeReceiver();
        JsonElement Payload(string account)=>JsonSerializer.SerializeToElement(new {
            schemaVersion=1, sessionKey="route-"+account, providerMessageId="same-provider-id", content="Oi", timestamp=DateTimeOffset.UtcNow,
            conversation=new {channel="whatsapp",accountId=account,nativeId=Peer,@namespace=FilomenaIdentity.WhatsAppConversationNamespace(Peer),kind="dm"},
            sender=new {channel="whatsapp",accountId=account,nativeId=Peer,stableId=FilomenaIdentity.WhatsAppActorStableId(Peer)}
        });
        Assert.Equal(InboundBridgeAcceptance.Accepted,await receiver.AcceptAsync(Payload("other"),TestContext.Current.CancellationToken));
        Assert.Equal(InboundBridgeAcceptance.Accepted,await receiver.AcceptAsync(Payload("default"),TestContext.Current.CancellationToken));
        Assert.Equal(InboundBridgeAcceptance.Duplicate,await receiver.AcceptAsync(Payload("default"),TestContext.Current.CancellationToken));
    }

    [Fact]
    public void LiveCompositionHasOnlyFlowConsumerAndRequiresCanonicalDm()
    {
        Assert.Throws<ArgumentException>(()=>new DmFlowLiveOptions().Validate());
        Assert.Throws<ArgumentException>(()=>new DmFlowLiveOptions{AllowedDmNativeId="120363318973251043@g.us"}.Validate());
        Assert.Throws<ArgumentException>(()=>new DmFlowLiveOptions{AllowedDmNativeId="+5511944558218"}.Validate());
        var configuration=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> {
            ["FlowLive:AllowedDmNativeId"]=Peer
        }).Build();
        using var http=new HttpClient();var services=new ServiceCollection();
        DmFlowLiveHosting.Configure(services,configuration,http);
        Assert.Contains(services,d=>d.ServiceType==typeof(IHostedService)&&d.ImplementationType==typeof(DmFlowLiveWorker));
        Assert.DoesNotContain(services,d=>d.ImplementationType==typeof(FilomenaTurnWorker)||d.ServiceType==typeof(ConversationTurnManager));
        // Inspect registration only: do not instantiate a transport or launch MCP.
    }

    [Fact]
    public async Task InboundQueuedWhileInactiveCannotBecomeExecutableAfterActivation()
    {
        var model=new Model();var sender=new Sender();using var session=Session(model,sender);
        var buffered=session.PrepareInbound(Message("before-activation"));
        session.Activate(Project());
        Assert.Equal("inactive",(await session.ObserveAsync(buffered,TestContext.Current.CancellationToken)).Outcome);
        var previousSnapshot=session.PrepareInbound(Message("previous-snapshot"));
        session.Activate(Project());
        Assert.Equal("inactive",(await session.ObserveAsync(previousSnapshot,TestContext.Current.CancellationToken)).Outcome);
        Assert.Empty(model.Inputs);Assert.Empty(sender.Sent);
    }

    [Fact]
    public async Task BridgeToWorkerUsesObservedRouteAndDeduplicatesWithoutRealServices()
    {
        var model=new Model();var sender=new Sender();using var session=Session(model,sender);
        var receiver=new OpenClawInboundBridgeReceiver(session.PrepareInbound);
        using var worker=new DmFlowLiveWorker(receiver,session,NullLogger<DmFlowLiveWorker>.Instance);
        session.Activate(Project());
        await worker.StartAsync(TestContext.Current.CancellationToken);
        var payload=JsonSerializer.SerializeToElement(new {
            schemaVersion=1, sessionKey="structural-dm-route", providerMessageId="bridge-one", content="Oi pelo bridge", timestamp=DateTimeOffset.UtcNow,
            conversation=new {channel="whatsapp",accountId="default",nativeId=Peer,@namespace=FilomenaIdentity.WhatsAppConversationNamespace(Peer),kind="dm"},
            sender=new {channel="whatsapp",accountId="default",nativeId=Peer,stableId=FilomenaIdentity.WhatsAppActorStableId(Peer)}
        });
        Assert.Equal(InboundBridgeAcceptance.Accepted,await receiver.AcceptAsync(payload,TestContext.Current.CancellationToken));
        await sender.Completed.Task.WaitAsync(TestContext.Current.CancellationToken);
        Assert.Equal(InboundBridgeAcceptance.Duplicate,await receiver.AcceptAsync(payload,TestContext.Current.CancellationToken));
        await worker.StopAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Oi pelo bridge",Assert.Single(model.Inputs));
        Assert.Equal("structural-dm-route",Assert.Single(sender.Sent).Route);
    }

    private sealed class Model : IModelResponseChatClient
    {
        public List<string> Inputs {get;}=[];
        public bool Wait {get;init;}
        public TaskCompletionSource Started {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<string> CompleteResponseAsync(string systemPrompt,string userPrompt,CancellationToken cancellationToken)
        {
            Inputs.Add(userPrompt);Started.TrySetResult();
            if(Wait)await Task.Delay(Timeout.Infinite,cancellationToken);
            return "Resposta: "+userPrompt;
        }
        public Task<string> CompleteResponseAsync(string systemPrompt,string userPrompt,string model,CancellationToken cancellationToken)=>CompleteResponseAsync(systemPrompt,userPrompt,cancellationToken);
    }
    private sealed class Sender : IConversationSender
    {
        public List<(string Route,string Text)> Sent {get;}=[];
        public TaskCompletionSource Completed {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<SendReceipt> SendTextAsync(string sessionKey,string text,CancellationToken cancellationToken)
        { cancellationToken.ThrowIfCancellationRequested();Sent.Add((sessionKey,text));Completed.TrySetResult();return Task.FromResult(new SendReceipt("out-"+Sent.Count)); }
    }
}
