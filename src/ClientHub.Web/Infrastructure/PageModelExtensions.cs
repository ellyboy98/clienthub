using ClientHub.Web.Data.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Security.Claims;

namespace ClientHub.Web.Infrastructure;

public static class PageModelExtensions
{
    /// <summary>Redirects to a local return URL, else to the user's home area.</summary>
    public static IActionResult RedirectToLocalOrHome(this PageModel page, string? returnUrl, ClaimsPrincipal? user = null)
    {
        if (!string.IsNullOrEmpty(returnUrl) && page.Url.IsLocalUrl(returnUrl) && returnUrl != "/")
            return page.LocalRedirect(returnUrl);
        return page.LocalRedirect(HomeFor(user ?? page.User));
    }

    public static string HomeFor(ClaimsPrincipal user)
    {
        if (user.Identity?.IsAuthenticated != true) return "/Account/Login";
        return user.IsInRole(Roles.Client) ? "/Portal" : "/Staff";
    }
}
