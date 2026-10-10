using System.ComponentModel.DataAnnotations;
using ClientHub.Web.Data.Entities;
using ClientHub.Web.Infrastructure;
using ClientHub.Web.Services.Audit;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace ClientHub.Web.Pages.Account;

[AllowAnonymous]
public class LoginModel : PageModel
{
    private readonly SignInManager<AppUser> _signIn;
    private readonly UserManager<AppUser> _users;
    private readonly IAuditService _audit;
    private readonly IStringLocalizer<SharedResource> _l;

    public LoginModel(SignInManager<AppUser> signIn, UserManager<AppUser> users, IAuditService audit, IStringLocalizer<SharedResource> l)
    {
        _signIn = signIn; _users = users; _audit = audit; _l = l;
    }

    [BindProperty] public InputData Input { get; set; } = new();
    [BindProperty(SupportsGet = true)] public string? ReturnUrl { get; set; }
    public string? Error { get; set; }
    public string? Notice { get; set; }

    public class InputData
    {
        [Required(ErrorMessage = "Validation.Required"), EmailAddress(ErrorMessage = "Validation.Email")]
        public string Email { get; set; } = "";
        [Required(ErrorMessage = "Validation.Required")]
        public string Password { get; set; } = "";
    }

    public async Task<IActionResult> OnGetAsync(string? notice = null)
    {
        if (User.Identity?.IsAuthenticated == true) return this.RedirectToLocalOrHome(ReturnUrl);
        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
        Notice = notice switch
        {
            "reset" => _l["Account.Login.PasswordSet"].Value,
            _ => null
        };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();

        var user = await _users.FindByEmailAsync(Input.Email.Trim());
        if (user is null)
        {
            await _audit.LogAsync(AuditActions.UserLoginFailed, "User", null, new { email = Input.Email.Trim(), reason = "UnknownEmail" });
            Error = _l["Account.Login.Invalid"];
            return Page();
        }

        var result = await _signIn.PasswordSignInAsync(user, Input.Password, isPersistent: true, lockoutOnFailure: true);
        if (result.Succeeded)
        {
            await _audit.LogAsync(AuditActions.UserLogin, "User", user.Id, userId: user.Id);
            return this.RedirectToLocalOrHome(ReturnUrl, await _signIn.CreateUserPrincipalAsync(user));
        }
        if (result.RequiresTwoFactor)
            return RedirectToPage("./LoginWith2fa", new { ReturnUrl });

        var reason = result.IsLockedOut ? "LockedOut" : result.IsNotAllowed ? "NotAllowed" : "BadPassword";
        await _audit.LogAsync(AuditActions.UserLoginFailed, "User", user.Id, new { reason }, user.Id);
        Error = result.IsLockedOut ? _l["Account.Login.LockedOut"] : _l["Account.Login.Invalid"];
        return Page();
    }
}
