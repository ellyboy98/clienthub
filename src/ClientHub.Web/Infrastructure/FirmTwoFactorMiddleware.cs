using ClientHub.Web.Data.Entities;
using ClientHub.Web.Services.Access;

namespace ClientHub.Web.Infrastructure;

/// <summary>
/// Q-15: a Lawyer or Office Manager without 2FA is sent to 2FA setup on every page until it is enabled.
/// Turned off only by REQUIRE_FIRM_2FA=false (dev manual checks).
/// </summary>
public class FirmTwoFactorMiddleware
{
    public const string SetupPath = "/Account/Manage/TwoFactor";
    private readonly RequestDelegate _next;
    private readonly bool _required;

    public FirmTwoFactorMiddleware(RequestDelegate next, IConfiguration config)
    {
        _next = next;
        _required = !string.Equals(config["REQUIRE_FIRM_2FA"], "false", StringComparison.OrdinalIgnoreCase);
    }

    public Task InvokeAsync(HttpContext ctx)
    {
        var u = ctx.User;
        if (_required && u.Identity?.IsAuthenticated == true
            && (u.IsInRole(Roles.Lawyer) || u.IsInRole(Roles.OfficeManager))
            && u.FindFirst(AppClaimsPrincipalFactory.TwoFactorClaim)?.Value != "1"
            && !IsExempt(ctx.Request.Path))
        {
            ctx.Response.Redirect(SetupPath);
            return Task.CompletedTask;
        }
        return _next(ctx);
    }

    private static bool IsExempt(PathString p) =>
        p.StartsWithSegments("/Account") || p.StartsWithSegments("/NotFound") || p.StartsWithSegments("/health")
        || p.StartsWithSegments("/css") || p.StartsWithSegments("/js") || p.StartsWithSegments("/img");
}
