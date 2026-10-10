using ClientHub.Web.Data.Entities;
using ClientHub.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ClientHub.Web.Pages.Account;

/// <summary>EN | BM switch. Sets the culture cookie; for a signed-in user also saves users.language (R-105, R-106).</summary>
[AllowAnonymous]
public class CultureModel : PageModel
{
    private readonly UserManager<AppUser> _users;
    private readonly SignInManager<AppUser> _signIn;

    public CultureModel(UserManager<AppUser> users, SignInManager<AppUser> signIn) { _users = users; _signIn = signIn; }

    public async Task<IActionResult> OnGetAsync(string culture, string? returnUrl)
    {
        if (!Cultures.IsSupported(culture)) return NotFound();
        SetCookie(Response, culture);
        if (User.Identity?.IsAuthenticated == true && await _users.GetUserAsync(User) is { } user && user.Language != culture)
        {
            user.Language = culture;
            await _users.UpdateAsync(user);
            await _signIn.RefreshSignInAsync(user);
        }
        return !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl) : LocalRedirect("/");
    }

    public static void SetCookie(HttpResponse response, string culture) =>
        response.Cookies.Append(CookieRequestCultureProvider.DefaultCookieName,
            CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture)),
            new CookieOptions { Expires = DateTimeOffset.UtcNow.AddYears(1), IsEssential = true, HttpOnly = true, Secure = true, SameSite = SameSiteMode.Lax });
}
