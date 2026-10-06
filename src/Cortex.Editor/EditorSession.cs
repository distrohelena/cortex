using System.Security.Cryptography;
using System.Text;

namespace Cortex.Editor;

public sealed class EditorSession
{
    private readonly Uri? _publicOrigin;
    public EditorSession(string? publicOrigin = null)
    {
        if (publicOrigin is null) return;
        if (!Uri.TryCreate(publicOrigin, UriKind.Absolute, out var origin) || origin.Scheme != "https" ||
            origin.UserInfo.Length != 0 || origin.AbsolutePath != "/" || origin.Query.Length != 0 || origin.Fragment.Length != 0)
            throw new ArgumentException("Public origin must be an HTTPS origin without credentials, path, query or fragment.", nameof(publicOrigin));
        _publicOrigin = origin;
    }
    internal string Token { get; } = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
    public string BootstrapUrl(string url) => url + "/#session=" + Token;
    public bool Authorized(HttpContext context)
    {
        var publicHost = _publicOrigin is not null && string.Equals(context.Request.Host.Value, _publicOrigin.Authority, StringComparison.OrdinalIgnoreCase);
        if (!publicHost && context.Request.Host.Host is not ("127.0.0.1" or "localhost" or "::1" or "[::1]")) return false;
        var supplied = context.Request.Headers["X-Cortex-Session"].ToString();
        if (supplied.Length != Token.Length || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(supplied), Encoding.ASCII.GetBytes(Token))) return false;
        if (HttpMethods.IsGet(context.Request.Method)) return true;
        var expectedOrigin = publicHost ? _publicOrigin!.GetLeftPart(UriPartial.Authority) : context.Request.Scheme + "://" + context.Request.Host;
        return context.Request.Headers.Origin.ToString() == expectedOrigin;
    }
}
