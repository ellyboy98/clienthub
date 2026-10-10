using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text;
using ClientHub.Web.Data.Entities;
using ClientHub.Web.Infrastructure;
using ClientHub.Web.Services.Email;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Localization;

namespace ClientHub.Web.Pages.Account;

[AllowAnonymous]
public class ForgotPasswordModel : PageModel
{
    private readonly UserManager<AppUser> _users;
    private readonly IEmailSender _email;
    private readonly IStringLocalizer<SharedResource> _l;
    private readonly ILogger<ForgotPasswordModel> _log;
    private readonly IAppLinks _links;

    public ForgotPasswordModel(UserManager<AppUser> users, IEmailSender email, IStringLocalizer<SharedResource> l, ILogger<ForgotPasswordModel> log, IAppLinks links)
    {
        _users = users; _email = email; _l = l; _log = log; _links = links;
    }

    [BindProperty, Required(ErrorMessage = "Validation.Required"), EmailAddress(ErrorMessage = "Validation.Email")]
    public string Email { get; set; } = "";
    public bool Sent { get; set; }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();
        var user = await _users.FindByEmailAsync(Email.Trim());
        if (user is { IsActive: true })
        {
            var token = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(await _users.GeneratePasswordResetTokenAsync(user)));
            var link = _links.SetPassword(user.Id, token);
            // The email goes out in the user's own language (R-106), not the visitor's.
            var previous = CultureInfo.CurrentUICulture;
            CultureInfo.CurrentUICulture = new CultureInfo(user.Language);
            var subject = _l["Email.ResetPassword.Subject"].Value;
            var body = string.Format(_l["Email.ResetPassword.Body"].Value, user.DisplayName, link);
            CultureInfo.CurrentUICulture = previous;
            try { await _email.SendAsync(user.Email!, subject, body); }
            catch (Exception ex) { _log.LogError(ex, "Password reset email failed for user {UserId}", user.Id); }
        }
        // Same answer whether or not the account exists.
        Sent = true;
        return Page();
    }
}
