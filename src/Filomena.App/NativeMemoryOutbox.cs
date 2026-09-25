using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Filomena.Core.Identity;
using Filomena.Core.Shadow;

namespace Filomena.App;

public sealed record NativeMemoryWork(string Digest, NativeMemoryRequest Request, NativeMemoryReceipt? Receipt = null, string? Diagnostic = null);

/// <summary>Single host writer; independent of conversation execution and outbound journals.</summary>
public sealed class NativeMemoryOutbox : IDisposable
{
    private readonly FileStream owner;
    private readonly NativeMemoryIngestConfig config;
    private readonly ShadowSessionBinding binding;
    private readonly Dictionary<string, NativeMemoryWork> work = new(StringComparer.Ordinal);
    private int nextPending;
    private readonly object sync = new();
    public IReadOnlyList<NativeMemoryWork> Pending { get { lock (sync) return work.Values.Where(w => w.Receipt?.State != "persisted").ToArray(); } }
    public NativeMemoryOutbox(NativeMemoryIngestConfig config, ShadowSessionBinding binding)
    {
        if (config.Session != binding.Session || config.ConversationNamespace != binding.ConversationNamespace
            || binding.Session.ChannelId != "whatsapp" || string.IsNullOrWhiteSpace(config.BindingRef)
            || string.IsNullOrWhiteSpace(config.Epoch) || config.AfterSequence < 0 || config.NotBefore == default
            || !Path.IsPathFullyQualified(config.StoreDirectory) || !Directory.Exists(config.StoreDirectory))
            throw new InvalidDataException("native_memory_host_binding_invalid");
        this.config = config; this.binding = binding;
        owner = new FileStream(Path.Combine(config.StoreDirectory, "owner.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        try
        {
            var authority = Hash(new { config.BindingRef, config.Session, config.ConversationNamespace, config.Epoch, config.AfterSequence,
                config.NotBefore, binding.SelfActorIds });
            var authorityFile = Path.Combine(config.StoreDirectory, "authority.json");
            if (File.Exists(authorityFile))
            { if (JsonSerializer.Deserialize<string>(File.ReadAllText(authorityFile)) != authority) throw new InvalidDataException("native_memory_store_binding_changed"); }
            else SaveFile(authorityFile, authority);
            foreach (var file in Directory.EnumerateFiles(config.StoreDirectory, "*.work.json"))
            {
                var item = JsonSerializer.Deserialize<NativeMemoryWork>(File.ReadAllBytes(file), NativeMemoryIngestClient.Json)
                    ?? throw new InvalidDataException("native_memory_store_invalid");
                if (item.Digest != Hash(item.Request) || item.Request.BindingRef != config.BindingRef
                    || Path.GetFileName(file) != item.Request.RequestRef + ".work.json") throw new InvalidDataException("native_memory_store_invalid");
                if (item.Receipt is { } r && (r.RequestRef != item.Request.RequestRef || r.EpisodeUuid != item.Request.EpisodeUuid
                    || r.Version != "native-memory-ingest:v1")) throw new InvalidDataException("native_memory_store_invalid");
                work.Add(item.Request.RequestRef, item);
            }
        }
        catch { owner.Dispose(); throw; }
    }

    public IReadOnlyList<string> StageBatch(IReadOnlyList<CanonicalShadowInbound> inputs)
    {
        lock (sync)
        {
        var staged = new List<string>();
        foreach (var input in inputs)
        {
            if (!binding.Accepts(input) || input.StreamEpoch != config.Epoch) throw new InvalidDataException("native_memory_input_binding_mismatch");
            if (input.FromMe || binding.IsSelf(input.ActorId) || input.SkipNonText || input.SuppressionReason is not null
                || input.Sequence <= config.AfterSequence || input.Message.Timestamp < config.NotBefore) continue;
            var actor = FilomenaIdentity.NormalizeWhatsAppActorNativeId(input.ActorId);
            if (!System.Text.RegularExpressions.Regex.IsMatch(actor, @"\A[0-9]+@s\.whatsapp\.net\z"))
                throw new InvalidDataException("native_memory_actor_unresolved");
            var text = input.Message.Message.GetProperty("texto").GetString();
            if (string.IsNullOrWhiteSpace(text)) throw new InvalidDataException("native_memory_content_invalid");
            var request = new NativeMemoryRequest("native-memory-ingest:v1", config.BindingRef, JsonSerializer.SerializeToElement(new
            {
                direction = "inbound", providerLabel = "WhatsApp", providerMessageId = input.Message.ProviderMessageId,
                conversation = new { channel = binding.Session.ChannelId, accountId = binding.Session.AccountId,
                    nativeId = binding.Session.NativeConversationId, kind = binding.Kind == Core.Flows.Sessions.SessionEntryKind.Group ? "group" : "dm",
                    @namespace = binding.ConversationNamespace },
                content = text, referenceTime = input.Message.Timestamp.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'"),
                originalTimestamp = input.Message.Timestamp.ToUnixTimeMilliseconds(), timestampSource = "provider",
                sender = new { channel = binding.Session.ChannelId, accountId = binding.Session.AccountId, nativeId = actor,
                    stableId = "whatsapp:" + actor, actorKind = "person", bindAsPerson = true,
                    identitySource = "trusted_native_observation", unresolvedAlternate = false }
            }, NativeMemoryIngestClient.Json));
            if (input.Message.Message.TryGetProperty("reply", out var quote) && quote.ValueKind == JsonValueKind.Object)
            {
                var id = quote.GetProperty("id").GetString();
                if (string.IsNullOrWhiteSpace(id)) throw new InvalidDataException("native_memory_quote_invalid");
                var reply = new JsonObject { ["providerMessageId"] = id, ["isQuote"] = true };
                if (quote.TryGetProperty("texto", out var quotedText) && quotedText.ValueKind == JsonValueKind.String)
                    reply["content"] = quotedText.GetString();
                var payload = JsonNode.Parse(request.Message.GetRawText())!;
                payload["replyTo"] = reply;
                request = request with { Message = JsonSerializer.SerializeToElement(payload, NativeMemoryIngestClient.Json) };
            }
            var item = new NativeMemoryWork(Hash(request), request);
            if (work.TryGetValue(request.RequestRef, out var previous))
            { if (previous.Digest != item.Digest) throw new InvalidDataException("native_memory_identity_conflict"); }
            else Save(item);
            staged.Add(request.RequestRef);
        }
        return staged;
        }
    }

    public async Task ReconcileAsync(NativeMemoryIngestClient client, CancellationToken token)
    {
        var pending = Pending;
        if (pending.Count == 0) return;
        var start = nextPending % pending.Count;
        var selected = pending.Skip(start).Concat(pending.Take(start)).Take(32).ToArray();
        nextPending = (start + selected.Length) % pending.Count;
        foreach (var item in selected)
        {
            token.ThrowIfCancellationRequested();
            NativeMemoryWork updated;
            try
            {
                // Re-submission is safe at the host route, whose durable claim prevents duplicate backend extraction.
                var receipt = item.Receipt is null || item.Receipt.RetryDisposition == "safe" ? await client.SubmitAsync(item.Request, token)
                    : await client.ReconcileAsync(item.Request, token);
                updated = item with { Receipt = receipt, Diagnostic = receipt.Diagnostic };
            }
            catch (Exception e) when (e is HttpRequestException or InvalidDataException or JsonException || e is OperationCanceledException && !token.IsCancellationRequested)
            { updated = item with { Diagnostic = "memory_backend_unavailable" }; }
            Save(updated);
        }
    }
    private void Save(NativeMemoryWork item)
    {
        lock (sync)
        {
        SaveFile(Path.Combine(config.StoreDirectory, item.Request.RequestRef + ".work.json"), item);
        work[item.Request.RequestRef] = item;
        }
    }
    private static string Hash(object value) => Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value, NativeMemoryIngestClient.Json)));
    private static void SaveFile<T>(string path, T value)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            { JsonSerializer.Serialize(file, value, NativeMemoryIngestClient.Json); file.Flush(true); }
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public void Dispose() => owner.Dispose();
}
