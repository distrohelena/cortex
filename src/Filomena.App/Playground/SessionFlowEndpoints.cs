using System.Collections.Concurrent;
using System.Text.Json;
using Filomena.Core.Flows;
using Filomena.Core.Flows.Sessions;
using Filomena.Core.Memory;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Filomena.App.Playground;

// Test ingress and fake completion. Persistent read-only memory is explicitly opt-in.
internal static class SessionFlowEndpoints
{
    private const string Prefix = "/api/flow/typed/sessions";
    public sealed record StartRequest(JsonElement Project, Dictionary<string, string[]>? Responses,
        MemoryFixture[]? Memories = null, bool UsePersistentMemory = false);
    public sealed record MemoryFixture(string Id, string ConversationNamespace, string Content, string Category, string[] Actors);
    public sealed record ProjectRequest(JsonElement Project);
    public sealed record SessionRequest(SessionRoute Session);
    public sealed record ReceiveRequest(SessionRoute Session, MessageFixture Message);
    public sealed record SessionRoute(string Channel, string AccountId, string NativeId, string Kind);
    public sealed record MessageFixture(string MessageId, DateTimeOffset Timestamp, string Direction, JsonElement Value);

    public static void Map(WebApplication app)
    {
        var store = app.Services.GetRequiredService<SessionFlowHarnessStore>();
        app.MapPost("/api/flow/typed/project/validate", async (ProjectRequest request) =>
        {
            await using var runtime = new SessionFlowRuntime(TimeProvider.System, new SessionFixtureCompletion(null));
            try
            {
                runtime.UpdateProject(new("validation", 1, "validation", request.Project));
                return Results.Ok(new { valid = true, issues = Array.Empty<SessionFlowProjectIssue>() });
            }
            catch (SessionFlowProjectException e) { return Results.Ok(new { valid = false, issues = e.Issues }); }
            catch (Exception e) when (InvalidInput(e)) { return InvalidRequest(); }
        });
        app.MapPost(Prefix, async (StartRequest request) =>
        {
            try
            {
                var id = await store.CreateAsync(request.Project, request.Responses, request.Memories, request.UsePersistentMemory);
                return Results.Ok(new { runtimeId = id, fake = true });
            }
            catch (SessionFlowProjectException e) { return InvalidProject(e); }
            catch (Exception e) when (InvalidInput(e)) { return InvalidRequest(); }
        });
        app.MapGet(Prefix + "/{id}", (string id) =>
        {
            if (!store.TryGet(id, out var harness)) return Results.NotFound();
            try { return Results.Ok(harness.Snapshot()); }
            catch (ObjectDisposedException) { return Stopped(); }
        });
        app.MapPost(Prefix + "/{id}/receive", async (string id, ReceiveRequest request, CancellationToken token) =>
        {
            if (!store.TryGet(id, out var harness)) return Results.NotFound();
            try
            {
                var key = Key(id, request.Session);
                var fixture = request.Message ?? throw new InvalidDataException();
                var direction = fixture.Direction switch
                {
                    "inbound" => SessionMessageDirection.Inbound,
                    "outbound" => SessionMessageDirection.Outbound,
                    _ => throw new InvalidDataException()
                };
                var envelope = new SessionMessageEnvelope(fixture.MessageId, key.ChannelId, key.AccountId,
                    key.NativeConversationId, fixture.Timestamp, direction, Kind(request.Session.Kind), fixture.Value);
                var result = await harness.Runtime.ReceiveAsync(key, envelope, token);
                if (result.Status == SessionAdmissionStatus.Rejected) return Results.BadRequest(new { accepted = false, reason = result.Reason });
                harness.Sessions.TryAdd(key, 0);
                return Results.Ok(new { accepted = result.Status == SessionAdmissionStatus.Accepted, duplicate = result.Status == SessionAdmissionStatus.Duplicate, result.AdmissionWatermark });
            }
            catch (ObjectDisposedException) { return Stopped(); }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { return Stopped(); }
            catch (Exception e) when (InvalidInput(e)) { return InvalidRequest(); }
        });
        app.MapPost(Prefix + "/{id}/drain", async (string id, SessionRequest request, CancellationToken token) =>
        {
            if (!store.TryGet(id, out var harness)) return Results.NotFound();
            try
            {
                var key = Key(id, request.Session);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                await harness.Runtime.WaitForIdleAsync(key, timeout.Token);
                return Results.Ok(new { drained = true, session = harness.Runtime.ReadSession(key) });
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                return !store.TryGet(id, out _) ? Stopped()
                    : Results.Json(new { error = "A continuação ainda está ativa." }, statusCode: 408);
            }
            catch (ObjectDisposedException) { return Stopped(); }
            catch (Exception e) when (InvalidInput(e)) { return InvalidRequest(); }
        });
        app.MapPost(Prefix + "/{id}/project", (string id, ProjectRequest request) =>
        {
            if (!store.TryGet(id, out var harness)) return Results.NotFound();
            try { return Results.Ok(harness.UpdateProject(request.Project)); }
            catch (ObjectDisposedException) { return Stopped(); }
            catch (SessionFlowProjectException e) { return InvalidProject(e); }
            catch (Exception e) when (InvalidInput(e)) { return InvalidRequest(); }
        });
        app.MapPost(Prefix + "/{id}/stop", async (string id) =>
            await store.RemoveAsync(id) ? Results.Ok(new { stopped = true }) : Results.NotFound());
    }

    private static SessionKey Key(string runtimeId, SessionRoute route)
    {
        if (route is null || new[] { route.Channel, route.AccountId, route.NativeId }.Any(v => string.IsNullOrWhiteSpace(v) || v.Length > 256 || v.Any(char.IsControl)))
            throw new InvalidDataException();
        _ = Kind(route.Kind);
        return new(runtimeId, "playground", route.Channel, route.AccountId, route.NativeId);
    }

    private static SessionEntryKind Kind(string value) => value switch
    {
        "dm" => SessionEntryKind.Dm,
        "group" => SessionEntryKind.Group,
        _ => throw new InvalidDataException()
    };
    private static bool InvalidInput(Exception error) => error is InvalidDataException or JsonException or ArgumentException or InvalidOperationException or KeyNotFoundException;
    private static IResult InvalidRequest() => Results.BadRequest(new { error = "Configuração ou evento de simulação inválido." });
    private static IResult Stopped() => Results.Json(new { error = "A simulação foi encerrada." }, statusCode: 410);
    private static IResult InvalidProject(SessionFlowProjectException error) => Results.BadRequest(new { error = "Projeto não executável.", issues = error.Issues });
}

internal sealed class SessionFlowHarnessStore(IMemoryReader? persistentMemory = null) : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, SessionFlowHarness> harnesses = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim administration = new(1, 1);
    private int reserved;
    private int disposed;

    public async Task<string> CreateAsync(JsonElement project, Dictionary<string, string[]>? responses,
        SessionFlowEndpoints.MemoryFixture[]? memories = null, bool usePersistentMemory = false)
    {
        await administration.WaitAsync();
        try { return await CreateCoreAsync(project, responses, memories, usePersistentMemory); }
        finally { administration.Release(); }
    }

    private async Task<string> CreateCoreAsync(JsonElement project, Dictionary<string, string[]>? responses,
        SessionFlowEndpoints.MemoryFixture[]? memories, bool usePersistentMemory)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        if (Interlocked.Increment(ref reserved) > 8)
        {
            Interlocked.Decrement(ref reserved);
            throw new InvalidDataException("Encerre uma simulação antes de abrir outra.");
        }
        SessionFlowHarness? harness = null;
        try
        {
            if (usePersistentMemory && memories is not null) throw new InvalidDataException("Choose fake OR persistent memory.");
            IMemoryReader reader = usePersistentMemory ? persistentMemory ?? throw new InvalidDataException("Persistent memory is not configured.")
                : memories is not null ? new FixtureMemoryReader(memories) : new UnavailableMemoryReader();
            harness = new(new SessionFlowRuntime(TimeProvider.System, new SessionFixtureCompletion(responses), reader));
            harness.UpdateProject(project);
            var id = Guid.NewGuid().ToString("N");
            if (!harnesses.TryAdd(id, harness)) throw new InvalidOperationException();
            return id;
        }
        catch
        {
            Interlocked.Decrement(ref reserved);
            if (harness is not null) await harness.Runtime.DisposeAsync();
            throw;
        }
    }

    public bool TryGet(string id, out SessionFlowHarness harness) => harnesses.TryGetValue(id, out harness!);

    public async Task<bool> RemoveAsync(string id)
    {
        if (!harnesses.TryRemove(id, out var harness)) return false;
        Interlocked.Decrement(ref reserved);
        await harness.Runtime.DisposeAsync();
        return true;
    }

    public async ValueTask DisposeAsync()
    {
        await administration.WaitAsync();
        try
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            foreach (var id in harnesses.Keys) await RemoveAsync(id);
        }
        finally { administration.Release(); }
    }
}

internal sealed class FixtureMemoryReader(SessionFlowEndpoints.MemoryFixture[] fixtures) : IMemoryReader
{
    public Task<IReadOnlyList<MemoryItem>> QueryAsync(MemoryQuery query, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<MemoryItem>>(fixtures.Where(f => f.ConversationNamespace == query.ConversationNamespace).Select(f =>
            new MemoryItem(f.Id, f.ConversationNamespace, f.Content, MemoryCategories.Parse(f.Category),
                f.Actors.Select(a => query.Persons.FirstOrDefault(p => p.ActorStableId == a) ?? MemoryIdentity.Person(query.ConversationNamespace, a, null)).ToArray(), "fixture")).ToArray());
    }
}

internal sealed class SessionFlowHarness(SessionFlowRuntime runtime)
{
    private long version;
    private readonly object projectGate = new();
    public SessionFlowRuntime Runtime { get; } = runtime;
    public ConcurrentDictionary<SessionKey, byte> Sessions { get; } = new();

    public SessionFlowProjectSnapshot UpdateProject(JsonElement project)
    {
        // Only administrative publication is serialized. Conversation execution does not take this gate.
        lock (projectGate)
        {
            var next = version + 1;
            var snapshot = Runtime.UpdateProject(new("playground", next, "revision-" + next, project));
            version = next;
            return snapshot;
        }
    }

    public object Snapshot()
    {
        var keys = Sessions.Keys.ToArray();
        return new
        {
            sessions = keys.Select(Runtime.ReadSession).Where(s => s is not null).ToArray(),
            // Deliberately omit outputs, message content, model inputs, prompts and raw provider data.
            executions = keys.SelectMany(Runtime.ReadOutcomes).Select(o => new
            {
                o.ExecutionId,
                o.BatchId,
                o.Session,
                o.ProjectVersion,
                o.ProjectRevision,
                o.StateId,
                o.StateVersion,
                closeReason = o.CloseReason.ToString(),
                o.ProviderMessageIds,
                o.StartedAt,
                o.CompletedAt,
                o.Success,
                o.TransitionTargetStateId,
                o.TransitionApplied
            }).ToArray(),
            trace = keys.SelectMany(Runtime.ReadTrace).ToArray()
        };
    }
}
