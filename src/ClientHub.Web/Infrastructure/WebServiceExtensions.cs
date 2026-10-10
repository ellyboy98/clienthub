using System.Globalization;
using ClientHub.Web.Data.Entities;
using ClientHub.Web.Services.Access;
using ClientHub.Web.Services.Audit;
using ClientHub.Web.Services.Email;
using Microsoft.AspNetCore.HostFiltering;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;

namespace ClientHub.Web.Infrastructure;

public static class WebServiceExtensions
{
    public const string FirmOnly = "FirmOnly";
    public const string LawyerOnly = "LawyerOnly";
    public const string ClientOnly = "ClientOnly";

    public static IServiceCollection AddClientHubWeb(this IServiceCollection services, IConfiguration config, IHostEnvironment env)
    {
        // Fails fast in Production when APP_BASE_URL is missing; email links never use the request Host.
        var baseUrl = AppLinks.Resolve(config, env);
        services.AddSingleton<IAppLinks, AppLinks>();
        services.PostConfigure<HostFilteringOptions>(o =>
        {
            // AllowedHosts from config wins; in Production default to the APP_BASE_URL host only.
            if (string.IsNullOrEmpty(config["AllowedHosts"]) && env.IsProduction())
                o.AllowedHosts = new List<string> { baseUrl.Host };
        });
        // Deactivated users and rotated stamps end open sessions within this interval.
        services.Configure<SecurityStampValidatorOptions>(o =>
            o.ValidationInterval = TimeSpan.FromSeconds(int.TryParse(config["SECURITY_STAMP_INTERVAL_SECONDS"], out var sec) ? sec : 60));

        services.AddHttpContextAccessor();
        services.AddLocalization(o => o.ResourcesPath = "Resources");

        services.AddScoped<IUserClaimsPrincipalFactory<AppUser>, AppClaimsPrincipalFactory>();
        services.AddIdentityCore<AppUser>().AddSignInManager<AppSignInManager>();

        services.AddAuthentication(o =>
            {
                o.DefaultScheme = IdentityConstants.ApplicationScheme;
                o.DefaultSignInScheme = IdentityConstants.ExternalScheme;
            })
            .AddIdentityCookies();
        services.ConfigureApplicationCookie(o =>
        {
            o.Cookie.Name = "clienthub.auth";
            o.Cookie.HttpOnly = true;
            o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            o.ExpireTimeSpan = TimeSpan.FromDays(14);
            o.SlidingExpiration = true;
            o.LoginPath = "/Account/Login";
            o.LogoutPath = "/Account/Logout";
            // R-008/R-156: a signed-in user without the right role gets the 404 page, never "forbidden".
            o.Events.OnRedirectToAccessDenied = ctx =>
            {
                ctx.Response.StatusCode = StatusCodes.Status404NotFound;
                return Task.CompletedTask;
            };
        });

        services.AddAuthorization(o =>
        {
            o.AddPolicy(FirmOnly, p => p.RequireRole(Roles.Firm));
            o.AddPolicy(LawyerOnly, p => p.RequireRole(Roles.Lawyer));
            o.AddPolicy(ClientOnly, p => p.RequireRole(Roles.Client));
        });

        services.AddRazorPages(o =>
            {
                o.Conventions.AuthorizeFolder("/Staff", FirmOnly);
                o.Conventions.AuthorizePage("/Staff/Settings", LawyerOnly);
                o.Conventions.AuthorizeFolder("/Portal", ClientOnly);
                o.Conventions.AuthorizeFolder("/Account/Manage");
            })
            .AddViewLocalization()
            .AddDataAnnotationsLocalization(o =>
                o.DataAnnotationLocalizerProvider = (_, f) => f.Create(typeof(SharedResource)));

        services.Configure<RequestLocalizationOptions>(o =>
        {
            var cultures = Cultures.Supported.Select(c => new CultureInfo(c)).ToList();
            o.DefaultRequestCulture = new RequestCulture(Cultures.English);
            o.SupportedCultures = cultures;
            o.SupportedUICultures = cultures;
            o.RequestCultureProviders = new List<IRequestCultureProvider>
            {
                new UserCultureProvider(),
                new CookieRequestCultureProvider()
            };
        });

        services.Configure<ForwardedHeadersOptions>(o =>
        {
            // Vercel terminates TLS in front of the container.
            o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            o.KnownIPNetworks.Clear();
            o.KnownProxies.Clear();
        });

        services.AddScoped<IAccessService, AccessService>();
        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<IEmailSender, SmtpEmailSender>();
        return services;
    }
}
