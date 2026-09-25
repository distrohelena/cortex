using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Filomena.Core.Flows.Sessions;
using Filomena.Core.Identity;

namespace Filomena.App;

// Deployment-owned, never read from the project or model. No automatic defaults for authority.
public sealed record NativeMemoryIngestConfig(string Endpoint, string GatewayTokenEnvironmentVariable,
    string CapabilityEnvironmentVariable, string BindingRef, SessionKey Session, string ConversationNamespace,
    string Epoch, long AfterSequence, DateTimeOffset NotBefore, string StoreDirectory);

public sealed record NativeMemoryRequest(string Version, string BindingRef, JsonElement Message)
{
    [JsonIgnore] public string EpisodeUuid => FilomenaIdentity.InboundEpisodeUuid(
        Message.GetProperty("conversation").GetProperty("channel").GetString()!,
        Message.GetProperty("conversation").GetProperty("nativeId").GetString()!,
        Message.GetProperty("providerMessageId").GetString()!).ToString();
    [JsonIgnore] public string RequestRef => Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
        new[] { Message.GetProperty("conversation").GetProperty("accountId").GetString()!, EpisodeUuid }, NativeMemoryIngestClient.Json)));
}
public sealed record NativeMemoryReceipt(string Version, string RequestRef, string EpisodeUuid, string State,
    string RetryDisposition, string? Diagnostic = null);

public sealed class NativeMemoryIngestClient : IDisposable
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true, RespectNullableAnnotations = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
    private readonly HttpClient http;
    private readonly bool ownsHttp;
    private readonly Uri endpoint;
    private readonly string gatewayToken, capability;

    public NativeMemoryIngestClient(Uri endpoint, string gatewayToken, string capability, HttpClient? http = null)
    {
        if (!endpoint.IsAbsoluteUri || !endpoint.IsLoopback || endpoint.Scheme != "http" || endpoint.Query.Length != 0
            || endpoint.Fragment.Length != 0 || endpoint.UserInfo.Length != 0
            || endpoint.AbsolutePath != "/plugins/memory-graphiti/native-ingest/v1"
            || string.IsNullOrWhiteSpace(gatewayToken) || string.IsNullOrWhiteSpace(capability))
            throw new InvalidDataException("native_memory_endpoint_invalid");
        this.endpoint = endpoint; this.gatewayToken = gatewayToken; this.capability = capability;
        ownsHttp = http is null;
        this.http = http ?? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(30) };
    }
    public Task<NativeMemoryReceipt> SubmitAsync(NativeMemoryRequest request, CancellationToken token) =>
        CallAsync(endpoint, HttpMethod.Post, request, request, token);
    public Task<NativeMemoryReceipt> GetStatusAsync(NativeMemoryRequest request, CancellationToken token) =>
        CallAsync(StatusUri(request), HttpMethod.Get, null, request, token);
    public Task<NativeMemoryReceipt> ReconcileAsync(NativeMemoryRequest request, CancellationToken token) =>
        CallAsync(StatusUri(request), HttpMethod.Post, null, request, token);
    private Uri StatusUri(NativeMemoryRequest request) => new(endpoint.AbsoluteUri + "/status?bindingRef="
        + Uri.EscapeDataString(request.BindingRef) + "&requestRef=" + request.RequestRef);
    private async Task<NativeMemoryReceipt> CallAsync(Uri uri, HttpMethod method, NativeMemoryRequest? body,
        NativeMemoryRequest expected, CancellationToken token)
    {
        using var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", gatewayToken);
        request.Headers.Add("x-native-memory-capability", capability);
        if (body is not null) request.Content = JsonContent.Create(body, options: Json);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException("native_memory_ingest_rejected");
        await response.Content.LoadIntoBufferAsync(16384, token);
        var receipt = JsonSerializer.Deserialize<NativeMemoryReceipt>(await response.Content.ReadAsStringAsync(token), Json);
        if (receipt is null || receipt.Version != "native-memory-ingest:v1" || receipt.RequestRef != expected.RequestRef
            || receipt.EpisodeUuid != expected.EpisodeUuid || receipt.State is not ("pending" or "queued" or "persisted" or "rejected" or "failed")
            || receipt.RetryDisposition is not ("safe" or "reconcile" or "none")
            || receipt.State == "persisted" && receipt.RetryDisposition != "none")
            throw new InvalidDataException("native_memory_receipt_invalid");
        return receipt;
    }
    public void Dispose() { if (ownsHttp) http.Dispose(); }
}
