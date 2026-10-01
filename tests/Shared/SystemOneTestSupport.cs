using System.Net;
using System.Text;
using System.Text.Json;

namespace Cortex.TestSupport;

internal static class SystemOneTestSupport
{
    internal static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();
    internal const string Questions = """
        {"action":{"type":"choice","instructions":"Choose an action","criteria":{"reply":"Respond","wait":"Wait"}},
         "care":{"type":"noul","instructions":"Does this need care?"},
         "priority":{"type":"score","instructions":"Urgency","criteria":["none","low","high"]}}
        """;
    internal const string Response = """
        {"model":"laya-rl-agent","answers":{
          "action":{"type":"choice","choice":"reply","probabilities":{"reply":0.8,"wait":0.2},"confidence":0.8},
          "care":{"type":"noul","noul":0.7},
          "priority":{"type":"score","score":1.5,"legend":{"0":"none","1":"low","2":"high"},"probabilities":{"0":0.1,"1":0.3,"2":0.6},"confidence":0.6}},
          "usage":{"input_tokens":42,"output_tokens":0},"routing":{"model":"typed-decisions"}}
        """;

    internal sealed class Handler(string response, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public Uri? RequestUri { get; private set; }
        public string? Authorization { get; private set; }
        public JsonElement Body { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Calls++;
            RequestUri = request.RequestUri;
            Authorization = request.Headers.Authorization?.ToString();
            Body = Json(await request.Content!.ReadAsStringAsync(token));
            return new(status) { Content = new StringContent(response, Encoding.UTF8, "application/json") };
        }
    }
}
