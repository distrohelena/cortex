namespace Filomena.App;

public sealed class InboundBridgeOptions
{
    public const string SectionName = "InboundBridge";

    public Uri ListenUrl { get; set; } = new("http://127.0.0.1:5147/");

    public string Path { get; set; } = "/v1/openclaw/events/inbound";

    public void Validate()
    {
        if (!ListenUrl.IsAbsoluteUri ||
            ListenUrl.Scheme != Uri.UriSchemeHttp ||
            !ListenUrl.IsLoopback)
        {
            throw new InvalidOperationException("InboundBridge:ListenUrl must be a loopback HTTP URL.");
        }

        if (!Path.StartsWith("/", StringComparison.Ordinal) || Path.Length < 2)
        {
            throw new InvalidOperationException("InboundBridge:Path must be an absolute HTTP path.");
        }
    }
}
