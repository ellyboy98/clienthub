using System.ComponentModel.DataAnnotations;
using ClientHub.Web.Data;
using ClientHub.Web.Data.Entities;
using ClientHub.Web.Infrastructure;
using ClientHub.Web.Services.Access;
using ClientHub.Web.Services.Email;
using ClientHub.Web.Services.Storage;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ClientHub.Web.Services.Settings;

/// <summary>Settings form (R-111). The SMTP password field is write only: blank keeps the stored one.</summary>
public class SettingsInput
{
    [Required(ErrorMessage = "Validation.Required"), StringLength(200, ErrorMessage = "Validation.TooLong")]
    public string? FirmName { get; set; }
    [StringLength(200, ErrorMessage = "Validation.TooLong")] public string? BankName { get; set; }
    [StringLength(50, ErrorMessage = "Validation.TooLong")] public string? BankAccountNo { get; set; }
    [StringLength(200, ErrorMessage = "Validation.TooLong")] public string? BankAccountName { get; set; }
    [StringLength(255, ErrorMessage = "Validation.TooLong")] public string? SmtpHost { get; set; }
    [Range(1, 65535, ErrorMessage = "Validation.Port")] public int? SmtpPort { get; set; }
    [StringLength(255, ErrorMessage = "Validation.TooLong")] public string? SmtpUser { get; set; }
    [StringLength(500, ErrorMessage = "Validation.TooLong")] public string? SmtpPassword { get; set; }
    /// <summary>Removes the stored password (a blank password field keeps it).</summary>
    public bool ClearSmtpPassword { get; set; }
    [EmailAddress(ErrorMessage = "Validation.Email"), StringLength(256, ErrorMessage = "Validation.TooLong")]
    public string? SmtpFrom { get; set; }

    public static SettingsInput From(FirmSettings s) => new()
    {
        FirmName = s.FirmName, BankName = s.BankName, BankAccountNo = s.BankAccountNo, BankAccountName = s.BankAccountName,
        SmtpHost = s.SmtpHost, SmtpPort = s.SmtpPort, SmtpUser = s.SmtpUser, SmtpFrom = s.SmtpFrom
    };
}

public class StaffAccountInput
{
    [Required(ErrorMessage = "Validation.Required"), StringLength(200, ErrorMessage = "Validation.TooLong")]
    public string? DisplayName { get; set; }
    [Required(ErrorMessage = "Validation.Required"), EmailAddress(ErrorMessage = "Validation.Email"), StringLength(256, ErrorMessage = "Validation.TooLong")]
    public string? Email { get; set; }
    [Required(ErrorMessage = "Validation.Required")]
    public string Role { get; set; } = Roles.Staff;
}

/// <summary>What bills show: bank details and a short-lived QR image URL (R-084, R-147, R-154).</summary>
public record BankView(string FirmName, string? BankName, string? AccountNo, string? AccountName, string? QrUrl);

public record StaffAccountRow(Guid Id, string DisplayName, string Email, string Role, bool TwoFactorEnabled, bool IsActive);

public enum StaffAccountError { None, EmailTaken, InvalidRole, NotFound, Self, Failed }

/// <summary>EmailSent false: the account exists but the set-password email failed.</summary>
public record StaffAccountResult(StaffAccountError Error, AppUser? User = null, bool EmailSent = false);

/// <summary>
/// Firm settings (one row) and firm user accounts (R-016..R-018, R-111, R-112). Callers check CanManageSettings first.
/// The SMTP password is protected with Data Protection (purpose SmtpEmailSender.ProtectorPurpose) and never returned.
/// </summary>
public interface IFirmSettingsService
{
    Task<FirmSettings> GetAsync(CancellationToken ct = default);
    Task SaveAsync(SettingsInput input, CancellationToken ct = default);
    Task<BankView> BankViewAsync(CancellationToken ct = default);
    /// <summary>Completion of a SettingsQr upload: points firm_settings at the new image (the old object stays). Returns the settings id.</summary>
    Task<Guid> RecordQrAsync(UploadTicket ticket, CancellationToken ct = default);
    /// <summary>Settings id when the QR already points at key (repeated completion), else null.</summary>
    Task<Guid?> QrRecordedAsync(string key, CancellationToken ct = default);

    Task<List<StaffAccountRow>> StaffAccountsAsync(CancellationToken ct = default);
    Task<StaffAccountResult> CreateStaffAccountAsync(StaffAccountInput input, CancellationToken ct = default);
    /// <summary>Deactivate (R-018) or reactivate (Q-02) a firm account. A Lawyer cannot deactivate their own account.</summary>
    Task<StaffAccountError> SetStaffActiveAsync(Guid userId, bool active, Guid actorId, CancellationToken ct = default);
}

public class FirmSettingsService : IFirmSettingsService
{
    public static readonly TimeSpan QrUrlExpiry = TimeSpan.FromMinutes(5);
    private readonly AppDbContext _db;
    private readonly IDataProtector _protector;
    private readonly IStorageService _storage;
    private readonly UserManager<AppUser> _users;
    private readonly IAccountEmails _emails;

    public FirmSettingsService(AppDbContext db, IDataProtectionProvider dp, IStorageService storage, UserManager<AppUser> users, IAccountEmails emails)
    {
        _db = db; _protector = dp.CreateProtector(SmtpEmailSender.ProtectorPurpose); _storage = storage; _users = users; _emails = emails;
    }

    public async Task<FirmSettings> GetAsync(CancellationToken ct = default)
    {
        var s = await _db.FirmSettings.OrderBy(x => x.CreatedAt).FirstOrDefaultAsync(ct);
        if (s is not null) return s;
        s = new FirmSettings { FirmName = "" };
        _db.FirmSettings.Add(s);
        await _db.SaveChangesAsync(ct);
        return s;
    }

    public async Task SaveAsync(SettingsInput input, CancellationToken ct = default)
    {
        var s = await GetAsync(ct);
        s.FirmName = input.FirmName!.Trim();
        s.BankName = Clean(input.BankName);
        s.BankAccountNo = Clean(input.BankAccountNo);
        s.BankAccountName = Clean(input.BankAccountName);
        s.SmtpHost = Clean(input.SmtpHost);
        s.SmtpPort = input.SmtpPort;
        s.SmtpUser = Clean(input.SmtpUser);
        s.SmtpFrom = Clean(input.SmtpFrom);
        if (input.ClearSmtpPassword) s.SmtpPassword = null;
        else if (!string.IsNullOrEmpty(input.SmtpPassword)) s.SmtpPassword = _protector.Protect(input.SmtpPassword);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<BankView> BankViewAsync(CancellationToken ct = default)
    {
        var s = await _db.FirmSettings.AsNoTracking().OrderBy(x => x.CreatedAt).FirstOrDefaultAsync(ct) ?? new FirmSettings();
        var qr = s.QrStorageKey is { } key ? _storage.PresignGet(key, "duitnow-qr" + Path.GetExtension(key), QrUrlExpiry) : null;
        return new BankView(s.FirmName, s.BankName, s.BankAccountNo, s.BankAccountName, qr);
    }

    public async Task<Guid> RecordQrAsync(UploadTicket ticket, CancellationToken ct = default)
    {
        var s = await GetAsync(ct);
        s.QrStorageKey = ticket.Key;
        await _db.SaveChangesAsync(ct);
        return s.Id;
    }

    public Task<Guid?> QrRecordedAsync(string key, CancellationToken ct = default) =>
        _db.FirmSettings.Where(s => s.QrStorageKey == key).Select(s => (Guid?)s.Id).FirstOrDefaultAsync(ct);

    public Task<List<StaffAccountRow>> StaffAccountsAsync(CancellationToken ct = default) =>
        _db.Users.Where(u => u.Role != Roles.Client)
            .OrderBy(u => u.Role == Roles.Lawyer ? 0 : u.Role == Roles.OfficeManager ? 1 : 2).ThenBy(u => u.DisplayName)
            .Select(u => new StaffAccountRow(u.Id, u.DisplayName, u.Email!, u.Role, u.TwoFactorEnabled, u.IsActive))
            .ToListAsync(ct);

    public async Task<StaffAccountResult> CreateStaffAccountAsync(StaffAccountInput input, CancellationToken ct = default)
    {
        if (!Roles.Firm.Contains(input.Role)) return new(StaffAccountError.InvalidRole);
        var email = input.Email!.Trim();
        if (await _users.FindByEmailAsync(email) is not null) return new(StaffAccountError.EmailTaken);
        var user = new AppUser
        {
            UserName = email, Email = email, EmailConfirmed = true, DisplayName = input.DisplayName!.Trim(),
            Role = input.Role, Language = Cultures.English, IsActive = true
        };
        var res = await _users.CreateAsync(user); // no password: the user sets it from the emailed link
        if (!res.Succeeded)
            return new(res.Errors.Any(e => e.Code is "DuplicateUserName" or "DuplicateEmail") ? StaffAccountError.EmailTaken : StaffAccountError.Failed);
        var sent = await _emails.SendSetPasswordAsync(user, AccountEmails.StaffKind, ct);
        return new(StaffAccountError.None, user, sent);
    }

    public async Task<StaffAccountError> SetStaffActiveAsync(Guid userId, bool active, Guid actorId, CancellationToken ct = default)
    {
        var user = await _users.FindByIdAsync(userId.ToString());
        if (user is null || user.Role == Roles.Client) return StaffAccountError.NotFound;
        if (!active && userId == actorId) return StaffAccountError.Self;
        if (user.IsActive == active) return StaffAccountError.None;
        if (active)
        {
            user.IsActive = true;
            var res = await _users.UpdateAsync(user);
            return res.Succeeded ? StaffAccountError.None : StaffAccountError.Failed;
        }
        var r = await _users.DeactivateAsync(user);
        return r.Succeeded ? StaffAccountError.None : StaffAccountError.Failed;
    }

    private static string? Clean(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
}
