using System.ComponentModel.DataAnnotations;
using System.Text;
using ClientHub.Web.Data.Entities;
using ClientHub.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Localization;

namespace ClientHub.Web.Pages.Account;

/// <summary>Opened from the account-creation email and the reset email (R-026, R-133). Token lifetime: Identity default, 1 day (Q-20).</summary>
[AllowAnonymous]
public class SetPasswordModel : PageModel
{
    private readonly UserManager<AppUser> _users;
    private readonly IStringLocalizer<SharedResource> _l;

    public SetPasswordModel(UserManager<AppUser> users, IStringLocalizer<SharedResource> l) { _users = users; _l = l; }

    [BindProperty(SupportsGet = true)] public Guid UserId { get; set; }
    [BindProperty(SupportsGet = true)] public string Code { get; set; } = "";

    [BindProperty, Required(ErrorMessage = "Validation.Required")]
    public string Password { get; set; } = "";
    [BindProperty, Required(ErrorMessage = "Validation.Required"), Compare(nameof(Password), ErrorMessage = "Validation.PasswordMismatch")]
    public string ConfirmPassword { get; set; } = "";

    public string? Error { get; set; }

    public IActionResult OnGet() => string.IsNullOrEmpty(Code) || UserId == Guid.Empty ? NotFound() : Page();

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();
        var user = await _users.FindByIdAsync(UserId.ToString());
        if (user is null || !user.IsActive) { Error = _l["Identity.InvalidToken"]; return Page(); }

        string token;
        try { token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(Code)); }
        catch (FormatException) { Error = _l["Identity.InvalidToken"]; return Page(); }

        var result = await _users.ResetPasswordAsync(user, token, Password);
        if (result.Succeeded)
        {
            if (!user.EmailConfirmed) { user.EmailConfirmed = true; await _users.UpdateAsync(user); }
            return RedirectToPage("./Login", new { notice = "reset" });
        }
        foreach (var e in result.Errors)
            ModelState.AddModelError(e.Code == "InvalidToken" ? "" : nameof(Password), IdentityErrors.Describe(_l, e));
        if (result.Errors.Any(e => e.Code == "InvalidToken")) Error = _l["Identity.InvalidToken"];
        return Page();
    }
}
