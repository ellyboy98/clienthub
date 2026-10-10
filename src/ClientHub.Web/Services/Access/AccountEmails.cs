using System.Globalization;
using System.Text;
using ClientHub.Web.Data.Entities;
using ClientHub.Web.Infrastructure;
using ClientHub.Web.Services.Email;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Localization;

namespace ClientHub.Web.Services.Access;

/// <summary>Set-password email for a new login (R-016, R-021, R-133). Links come only from IAppLinks (APP_BASE_URL).</summary>
public interface IAccountEmails
{
    /// <summary>
    /// kind: resx prefix under Email.* ("AccountCreated" for clients, "StaffAccountCreated" for firm users); the body takes
    /// {0} = display name, {1} = link. Returns false when sending failed (the caller warns; Forgot password is the fallback).
    /// </summary>
    Task<bool> SendSetPasswordAsync(AppUser user, string kind, CancellationToken ct = default);
}

public class AccountEmails : IAccountEmails
{
    public const string ClientKind = "AccountCreated";
    public const string StaffKind = "StaffAccountCreated";

    private readonly UserManager<AppUser> _users;
    private readonly IEmailSender _email;
    private readonly IAppLinks _links;
    private readonly IStringLocalizer<SharedResource> _l;
    private readonly ILogger<AccountEmails> _log;

    public AccountEmails(UserManager<AppUser> users, IEmailSender email, IAppLinks links, IStringLocalizer<SharedResource> l, ILogger<AccountEmails> log)
    {
        _users = users; _email = email; _links = links; _l = l; _log = log;
    }

    public async Task<bool> SendSetPasswordAsync(AppUser user, string kind, CancellationToken ct = default)
    {
        var token = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(await _users.GeneratePasswordResetTokenAsync(user)));
        var link = _links.SetPassword(user.Id, token);
        var previous = CultureInfo.CurrentUICulture;
        string subject, body;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo(user.Language);
            subject = _l[$"Email.{kind}.Subject"].Value;
            body = string.Format(_l[$"Email.{kind}.Body"].Value, user.DisplayName, link);
        }
        finally { CultureInfo.CurrentUICulture = previous; }
        try { await _email.SendAsync(user.Email!, subject, body, ct); return true; }
        catch (Exception ex) { _log.LogError(ex, "Set-password email failed for user {UserId}", user.Id); return false; }
    }
}
