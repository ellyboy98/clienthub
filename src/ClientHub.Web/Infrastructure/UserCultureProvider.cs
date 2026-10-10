using ClientHub.Web.Services.Access;
using Microsoft.AspNetCore.Localization;

namespace ClientHub.Web.Infrastructure;

/// <summary>Signed-in users get their own language (users.language via claim); visitors fall through to the culture cookie.</summary>
public class UserCultureProvider : RequestCultureProvider
{
    public override Task<ProviderCultureResult?> DetermineProviderCultureResult(HttpContext httpContext)
    {
        var lang = httpContext.User.FindFirst(AppClaimsPrincipalFactory.LanguageClaim)?.Value;
        return Task.FromResult(string.IsNullOrEmpty(lang) ? null : new ProviderCultureResult(lang));
    }
}
