namespace Filomena.Llm.LmStudio;

public sealed class LmStudioOptions
{
    public const string SectionName = "LmStudio";

    public Uri Endpoint { get; set; } = new("http://localhost:1234/v1/");

    public string Model { get; set; } = "google/gemma-4-e4b";

    public double ArbitrationTemperature { get; set; } = 0.1;

    public int ArbitrationMaxTokens { get; set; } = 160;

    public int RequestTimeoutSeconds { get; set; } = 45;

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
        ArgumentOutOfRangeException.ThrowIfLessThan(RequestTimeoutSeconds, 1);
    }
}
