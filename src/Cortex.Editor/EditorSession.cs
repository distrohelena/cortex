using System.Security.Cryptography;
using System.Text;

namespace Cortex.Editor;

public sealed class EditorSession
{
    internal string Token { get; } = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
    public string BootstrapUrl(string url) => url + "/#session=" + Token;
    public bool Authorized(HttpContext context)
    {
        if (context.Request.Host.Host is not ("127.0.0.1" or "localhost" or "::1" or "[::1]")) return false;
        var supplied = context.Request.Headers["X-Cortex-Session"].ToString();
        if (supplied.Length != Token.Length || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(supplied), Encoding.ASCII.GetBytes(Token))) return false;
        if (HttpMethods.IsGet(context.Request.Method)) return true;
        return context.Request.Headers.Origin.ToString() == context.Request.Scheme + "://" + context.Request.Host;
    }
}
