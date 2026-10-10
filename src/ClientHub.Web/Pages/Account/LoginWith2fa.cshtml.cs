using System.ComponentModel.DataAnnotations;
using ClientHub.Web.Data.Entities;
using ClientHub.Web.Infrastructure;
using ClientHub.Web.Services.Audit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace ClientHub.Web.Pages.Account;

[AllowAnonymous]
public class LoginWith2faModel : PageModel
{
    private readonly SignInManager<AppUser> _signIn;
    private readonly IAuditService _audit;
    private readonly IStringLocalizer<SharedResource> _l;

    public LoginWith2faModel(SignInManager<AppUser> signIn, IAuditService audit, IStringLocalizer<SharedResource> l)
    {
        _signIn = signIn; _audit = audit; _l = l;
    }

    [BindProperty, Required(ErrorMessage = "Validation.Required")]
    public string Code { get; set; } = "";
    [BindProperty(SupportsGet = true)] public string? ReturnUrl { get; set; }
    public string? Error { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (await _signIn.GetTwoFactorAuthenticationUserAsync() is null) return RedirectToPage("./Login");
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var user = await _signIn.GetTwoFactorAuthenticationUserAsync();
        if (user is null) return RedirectToPage("./Login");
        if (!ModelState.IsValid) return Page();

        var code = Code.Replace(" ", "").Replace("-", "").Trim();
        // 6 digits = authenticator code; anything else is tried as a recovery code.
        var result = code.Length == 6 && code.All(char.IsDigit)
            ? await _signIn.TwoFactorAuthenticatorSignInAsync(code, isPersistent: true, rememberClient: false)
            : await _signIn.TwoFactorRecoveryCodeSignInAsync(Code.Trim());

        if (result.Succeeded)
        {
            await _audit.LogAsync(AuditActions.UserLogin, "User", user.Id, new { twoFactor = true }, user.Id);
            return this.RedirectToLocalOrHome(ReturnUrl, await _signIn.CreateUserPrincipalAsync(user));
        }
        await _audit.LogAsync(AuditActions.UserLoginFailed, "User", user.Id, new { reason = result.IsLockedOut ? "LockedOut" : "BadTwoFactorCode" }, user.Id);
        Error = result.IsLockedOut ? _l["Account.Login.LockedOut"] : _l["Account.LoginWith2fa.Invalid"];
        return Page();
    }
}
