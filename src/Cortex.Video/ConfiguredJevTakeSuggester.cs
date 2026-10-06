using Cortex.Inference.SystemOne;

namespace Cortex.Video;

public sealed class ConfiguredJevTakeSuggester(string endpoint, string credentialVariable) : ITakeSuggester
{
    public async Task<TakeEditPlan> SuggestAsync(TranscriptResult transcript, string goal, CancellationToken token)
    {
        var key = Environment.GetEnvironmentVariable(credentialVariable);
        if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("Configure a variável de ambiente " + credentialVariable + " para avaliar takes com Jev.");
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(60) };
        var client = new SystemOneClient(http, new Uri(endpoint), () => key, true, SystemOneProbabilityPolicy.Jev113TwoDecimals);
        return await new JevTakeSuggester(client).SuggestAsync(transcript, goal, token);
    }
}
