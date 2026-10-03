namespace Cortex.Inference.LmStudio;

public sealed class LmStudioOptions
{
    public const string SectionName = "LmStudio";

    public Uri Endpoint { get; set; } = new("http://localhost:1234/v1/");

    public string Model { get; set; } = "google/gemma-4-e4b";

    public double ArbitrationTemperature { get; set; } = 0.1;

    public int ArbitrationMaxTokens { get; set; } = 8192;

    public double GenerationTemperature { get; set; } = 0.7;

    public int GenerationMaxTokens { get; set; } = 8192;

    public string? GenerationReasoningEffort { get; set; }

    public int RequestTimeoutSeconds { get; set; } = 180;

    public void Validate()
    {
        if (!Endpoint.IsAbsoluteUri || Endpoint.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException("LmStudio:Endpoint must be an absolute HTTP(S) URI.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(Model);
        if (ArbitrationTemperature is < 0 or > 2)
        {
            throw new InvalidOperationException("LmStudio:ArbitrationTemperature must be between 0 and 2.");
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(ArbitrationMaxTokens, 1);
        if (!double.IsFinite(GenerationTemperature) || GenerationTemperature is < 0 or > 2)
        {
            throw new InvalidOperationException("LmStudio:GenerationTemperature must be between 0 and 2.");
        }
        ArgumentOutOfRangeException.ThrowIfLessThan(GenerationMaxTokens, 1);
        if (GenerationReasoningEffort is not (null or "none" or "minimal" or "low" or "medium" or "high" or "xhigh"))
            throw new InvalidOperationException("LmStudio:GenerationReasoningEffort is invalid.");
        if (RequestTimeoutSeconds != Timeout.Infinite) ArgumentOutOfRangeException.ThrowIfLessThan(RequestTimeoutSeconds, 1);
    }
}
