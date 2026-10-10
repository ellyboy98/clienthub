namespace ClientHub.Web.Infrastructure;

/// <summary>
/// Absolute links that go into emails. Always built from APP_BASE_URL, never from the request Host header
/// (prevents password-reset poisoning). Used by Forgot password and the account-creation email.
/// </summary>
public interface IAppLinks
{
    string BaseUrl { get; }
    string SetPassword(Guid userId, string encodedToken);
    string Absolute(string path);
}

public class AppLinks : IAppLinks
{
    public const string ConfigKey = "APP_BASE_URL";
    private const string DevFallback = "http://localhost:5000";

    public AppLinks(IConfiguration config, IHostEnvironment env)
    {
        BaseUrl = Resolve(config, env).ToString().TrimEnd('/');
    }

    public string BaseUrl { get; }

    public string Absolute(string path) => BaseUrl + "/" + path.TrimStart('/');

    public string SetPassword(Guid userId, string encodedToken) =>
        Absolute($"Account/SetPassword?userId={userId}&code={Uri.EscapeDataString(encodedToken)}");

    /// <summary>Production requires APP_BASE_URL (absolute http/https URL); other environments fall back to localhost.</summary>
    public static Uri Resolve(IConfiguration config, IHostEnvironment env)
    {
        var raw = config[ConfigKey];
        if (string.IsNullOrWhiteSpace(raw))
        {
            if (env.IsProduction()) throw new InvalidOperationException($"{ConfigKey} must be set in Production.");
            raw = DevFallback;
        }
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new InvalidOperationException($"{ConfigKey} must be an absolute http(s) URL.");
        return uri;
    }
}
