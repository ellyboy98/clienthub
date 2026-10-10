using System.Net;
using System.Text.RegularExpressions;
using ClientHub.Web.Data;
using ClientHub.Web.Data.Entities;
using ClientHub.Web.Data.Seed;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using ClientHub.Web.Services.Email;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ClientHub.Tests.Support;

/// <summary>The real app on a fresh database, seeded. REQUIRE_FIRM_2FA stays on (production default).</summary>
public class AppFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string Password = "Test-Password-123!";
    public const string BaseUrl = "https://portal.firm.test";
    private readonly TestDatabase _db = new();
    public FakeEmailSender Emails { get; } = new();

    /// <summary>Production default. <see cref="FirmAppFactory"/> turns it off so Lawyer/OM page tests need no TOTP.</summary>
    protected virtual bool RequireFirm2fa => true;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", _db.ConnectionString);
        builder.UseSetting("REQUIRE_FIRM_2FA", RequireFirm2fa ? "true" : "false");
        builder.UseSetting("APP_BASE_URL", BaseUrl);
        builder.UseSetting("SECURITY_STAMP_INTERVAL_SECONDS", "0");
        // Test bucket; presigned URLs are signed for the endpoint the tests can reach (no browser-only localhost:9000).
        var storage = TestStorage.Options();
        builder.UseSetting("S3_ENDPOINT", storage.Endpoint);
        builder.UseSetting("S3_PUBLIC_ENDPOINT", storage.Endpoint);
        builder.UseSetting("S3_REGION", storage.Region);
        builder.UseSetting("S3_BUCKET", storage.Bucket);
        builder.UseSetting("S3_ACCESS_KEY_ID", storage.AccessKeyId);
        builder.UseSetting("S3_SECRET_ACCESS_KEY", storage.SecretAccessKey);
        builder.ConfigureLogging(l => l.ClearProviders());
        builder.ConfigureTestServices(s => s.AddSingleton<IEmailSender>(Emails));
    }

    public async Task InitializeAsync()
    {
        await _db.CreateAsync();
        using var scope = Services.CreateScope(); // starts the host, which runs migrations
        await SeedData.RunAsync(scope.ServiceProvider, Password);
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await _db.DisposeAsync();
    }

    public HttpClient NewClient() => CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        BaseAddress = new Uri("https://localhost")
    });

    public async Task<AppUser> CreateUserAsync(string role, bool active = true)
    {
        using var scope = Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var email = $"{role.ToLowerInvariant()}-{Guid.NewGuid():N}@clienthub.test";
        var u = new AppUser { UserName = email, Email = email, EmailConfirmed = true, DisplayName = role, Role = role, IsActive = active };
        var res = await users.CreateAsync(u, Password);
        if (!res.Succeeded) throw new InvalidOperationException(string.Join("; ", res.Errors.Select(e => e.Code)));
        return u;
    }

    public T WithDb<T>(Func<AppDbContext, T> f)
    {
        using var scope = Services.CreateScope();
        return f(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    public async Task<T> WithDbAsync<T>(Func<AppDbContext, Task<T>> f)
    {
        using var scope = Services.CreateScope();
        return await f(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    /// <summary>Runs f with a service from a fresh scope (services that hold a DbContext).</summary>
    public async Task<T> WithServiceAsync<TService, T>(Func<TService, Task<T>> f) where TService : notnull
    {
        using var scope = Services.CreateScope();
        return await f(scope.ServiceProvider.GetRequiredService<TService>());
    }

    public static string Token(string html)
    {
        var m = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        if (!m.Success) throw new InvalidOperationException("No antiforgery token in page.");
        return m.Groups[1].Value;
    }

    /// <summary>Posts the login form. Returns the POST response (302 on success).</summary>
    public static async Task<HttpResponseMessage> LoginAsync(HttpClient client, string email, string password = Password)
    {
        var page = await client.GetStringAsync("/Account/Login");
        return await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Email"] = email,
            ["Input.Password"] = password,
            ["__RequestVerificationToken"] = Token(page)
        }));
    }

    public async Task<HttpClient> SignedInAsync(string email)
    {
        var client = NewClient();
        var res = await LoginAsync(client, email);
        if (res.StatusCode != HttpStatusCode.Redirect) throw new InvalidOperationException($"Login failed for {email}: {res.StatusCode}");
        return client;
    }
}

/// <summary>Same app with REQUIRE_FIRM_2FA=false, for page tests signed in as Lawyer or Office Manager.</summary>
public class FirmAppFactory : AppFactory
{
    protected override bool RequireFirm2fa => false;
}
