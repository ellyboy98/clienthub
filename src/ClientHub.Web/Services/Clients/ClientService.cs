using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text;
using ClientHub.Web.Data;
using ClientHub.Web.Data.Entities;
using ClientHub.Web.Infrastructure;
using ClientHub.Web.Services.Access;
using ClientHub.Web.Services.Email;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace ClientHub.Web.Services.Clients;

/// <summary>Client form (R-029, R-030). Error messages are resx keys, localised by the data-annotations localizer.</summary>
public class ClientInput : IValidatableObject
{
    [Required(ErrorMessage = "Validation.Required")]
    public string ClientType { get; set; } = Data.Entities.ClientType.Person;
    [Required(ErrorMessage = "Validation.Required"), StringLength(200, ErrorMessage = "Validation.TooLong")]
    public string? Name { get; set; }
    [Required(ErrorMessage = "Validation.Required"), StringLength(50, ErrorMessage = "Validation.TooLong")]
    public string? IcOrRegNo { get; set; }
    [Required(ErrorMessage = "Validation.Required"), StringLength(30, ErrorMessage = "Validation.TooLong")]
    public string? Phone { get; set; }
    [Required(ErrorMessage = "Validation.Required"), EmailAddress(ErrorMessage = "Validation.Email"), StringLength(256, ErrorMessage = "Validation.TooLong")]
    public string? Email { get; set; }
    [Required(ErrorMessage = "Validation.Required"), StringLength(500, ErrorMessage = "Validation.TooLong")]
    public string? Address { get; set; }
    [StringLength(200, ErrorMessage = "Validation.TooLong")]
    public string? ContactPersonName { get; set; }
    [StringLength(30, ErrorMessage = "Validation.TooLong")]
    public string? ContactPersonPhone { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        // IValidatableObject messages are not run through the data-annotations localizer; localise here when MVC supplies one.
        var l = context.GetService(typeof(IStringLocalizer<SharedResource>)) as IStringLocalizer;
        var required = l?["Validation.Required"].Value ?? "Validation.Required";
        if (ClientType is not (Data.Entities.ClientType.Person or Data.Entities.ClientType.Company))
            yield return new ValidationResult(required, new[] { nameof(ClientType) });
        if (ClientType == Data.Entities.ClientType.Company)
        {
            if (string.IsNullOrWhiteSpace(ContactPersonName))
                yield return new ValidationResult(required, new[] { nameof(ContactPersonName) });
            if (string.IsNullOrWhiteSpace(ContactPersonPhone))
                yield return new ValidationResult(required, new[] { nameof(ContactPersonPhone) });
        }
    }

    public static ClientInput From(Client c) => new()
    {
        ClientType = c.ClientType, Name = c.Name, IcOrRegNo = c.IcOrRegNo, Phone = c.Phone, Email = c.Email,
        Address = c.Address, ContactPersonName = c.ContactPersonName, ContactPersonPhone = c.ContactPersonPhone
    };

    internal void ApplyTo(Client c)
    {
        c.ClientType = ClientType;
        c.Name = Name!.Trim();
        c.IcOrRegNo = IcOrRegNo!.Trim();
        c.Phone = Phone!.Trim();
        c.Email = Email!.Trim();
        c.Address = Address!.Trim();
        // Person clients have no person-in-charge fields (R-030).
        var company = ClientType == Data.Entities.ClientType.Company;
        c.ContactPersonName = company ? ContactPersonName!.Trim() : null;
        c.ContactPersonPhone = company ? ContactPersonPhone!.Trim() : null;
    }
}

public enum ClientError { None, EmailTaken, EmailChangeNotAllowed, Failed }

/// <summary>EmailSent: whether the set-password email went out (false = it failed; null = none was needed).</summary>
public record ClientResult(Client? Client, ClientError Error, bool? EmailSent);

/// <summary>
/// Client records and their one login (R-014, R-021). Callers check access and IsLawyerOrOfficeManager first.
/// </summary>
public interface IClientService
{
    /// <summary>Creates the client, its Client login (no password) and emails a set-password link.</summary>
    Task<ClientResult> CreateAsync(ClientInput input, CancellationToken ct = default);

    /// <summary>
    /// Saves details; a changed email also changes the login email and rotates the security stamp, so links sent to the
    /// old address stop working. A login without a password yet gets a fresh set-password link at the new address.
    /// canChangeEmail: Lawyer/OM only (Fiqri, overrides Q-14); otherwise an email change returns EmailChangeNotAllowed.
    /// </summary>
    Task<ClientResult> UpdateAsync(Client client, ClientInput input, bool canChangeEmail, CancellationToken ct = default);

    /// <summary>Deactivate or reactivate the client's login (R-019, Q-01, Q-02). Data stays.</summary>
    Task SetLoginActiveAsync(Client client, bool active, CancellationToken ct = default);
}

public class ClientService : IClientService
{
    private readonly AppDbContext _db;
    private readonly UserManager<AppUser> _users;
    private readonly IEmailSender _email;
    private readonly IAppLinks _links;
    private readonly IStringLocalizer<SharedResource> _l;
    private readonly ILogger<ClientService> _log;

    public ClientService(AppDbContext db, UserManager<AppUser> users, IEmailSender email, IAppLinks links,
        IStringLocalizer<SharedResource> l, ILogger<ClientService> log)
    {
        _db = db; _users = users; _email = email; _links = links; _l = l; _log = log;
    }

    public static bool SameEmail(string? a, string? b) =>
        string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);

    public async Task<ClientResult> CreateAsync(ClientInput input, CancellationToken ct = default)
    {
        var email = input.Email!.Trim();
        if (await _users.FindByEmailAsync(email) is not null) return new(null, ClientError.EmailTaken, null);

        var user = new AppUser
        {
            UserName = email, Email = email, EmailConfirmed = true, DisplayName = input.Name!.Trim(),
            Role = Roles.Client, Language = Cultures.English, IsActive = true
        };
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var res = await _users.CreateAsync(user);
        if (!res.Succeeded)
            return new(null, res.Errors.Any(e => e.Code is "DuplicateUserName" or "DuplicateEmail") ? ClientError.EmailTaken : ClientError.Failed, null);

        var client = new Client { UserId = user.Id };
        input.ApplyTo(client);
        _db.Clients.Add(client);
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        var sent = await SendAccountCreatedAsync(user, ct);
        return new(client, ClientError.None, sent);
    }

    public async Task<ClientResult> UpdateAsync(Client client, ClientInput input, bool canChangeEmail, CancellationToken ct = default)
    {
        var newEmail = input.Email!.Trim();
        var user = client.UserId is { } uid ? await _users.FindByIdAsync(uid.ToString()) : null;
        var current = user?.Email ?? client.Email;
        var emailChanged = !string.Equals(current, newEmail, StringComparison.Ordinal);
        if (emailChanged && !canChangeEmail)
        {
            if (!SameEmail(current, newEmail)) return new(client, ClientError.EmailChangeNotAllowed, null);
            input.Email = current; // casing-only difference: keep the stored address
            emailChanged = false;
        }
        if (user is not null && emailChanged && !SameEmail(user.Email, newEmail))
        {
            var other = await _users.FindByEmailAsync(newEmail);
            if (other is not null && other.Id != user.Id) return new(client, ClientError.EmailTaken, null);
        }

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        if (user is not null)
        {
            if (emailChanged)
            {
                user.Email = newEmail;
                user.UserName = newEmail; // UpdateAsync re-normalises both
            }
            user.DisplayName = input.Name!.Trim();
            var res = await _users.UpdateAsync(user);
            if (!res.Succeeded)
                return new(client, res.Errors.Any(e => e.Code is "DuplicateUserName" or "DuplicateEmail") ? ClientError.EmailTaken : ClientError.Failed, null);
            // Old set-password / reset links (and sessions) are bound to the stamp: rotating it voids them.
            if (emailChanged) await _users.UpdateSecurityStampAsync(user);
        }
        input.ApplyTo(client);
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        bool? sent = null;
        if (user is not null && emailChanged && user.IsActive && !await _users.HasPasswordAsync(user))
            sent = await SendAccountCreatedAsync(user, ct);
        return new(client, ClientError.None, sent);
    }

    public async Task SetLoginActiveAsync(Client client, bool active, CancellationToken ct = default)
    {
        if (client.UserId is not { } uid) return;
        var user = await _users.FindByIdAsync(uid.ToString());
        if (user is null || user.IsActive == active) return;
        if (active)
        {
            user.IsActive = true;
            await _users.UpdateAsync(user);
        }
        else
        {
            await _users.DeactivateAsync(user);
        }
    }

    /// <summary>Returns false when sending failed (the caller shows a warning; Forgot password is the fallback).</summary>
    private async Task<bool> SendAccountCreatedAsync(AppUser user, CancellationToken ct)
    {
        var token = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(await _users.GeneratePasswordResetTokenAsync(user)));
        var link = _links.SetPassword(user.Id, token);
        var previous = CultureInfo.CurrentUICulture;
        string subject, body;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo(user.Language);
            subject = _l["Email.AccountCreated.Subject"].Value;
            body = string.Format(_l["Email.AccountCreated.Body"].Value, user.DisplayName, link);
        }
        finally { CultureInfo.CurrentUICulture = previous; }
        // Email failure must not undo the client record; the creator is warned and can use Forgot password.
        try { await _email.SendAsync(user.Email!, subject, body, ct); return true; }
        catch (Exception ex) { _log.LogError(ex, "Account-creation email failed for user {UserId}", user.Id); return false; }
    }
}
