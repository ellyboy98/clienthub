using ClientHub.Web.Services.Access;
using ClientHub.Web.Services.Settings;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace ClientHub.Web.Pages.Staff.Settings;

/// <summary>
/// Settings (R-111, R-112, R-149): firm, bank, DuitNow QR, SMTP and the staff accounts list. Lawyer only: the folder policy
/// re-routes everyone else to 404, and every handler asks CanManageSettings again. "Send test email" (R-113) is cut.
/// </summary>
public class IndexModel : PageModel
{
    private readonly IFirmSettingsService _settings;
    private readonly IAccessService _access;
    private readonly IStringLocalizer<SharedResource> _l;

    public IndexModel(IFirmSettingsService settings, IAccessService access, IStringLocalizer<SharedResource> l)
    {
        _settings = settings; _access = access; _l = l;
    }

    [BindProperty] public SettingsInput Input { get; set; } = new();
    public bool HasSmtpPassword { get; private set; }
    public string? QrUrl { get; private set; }
    public List<StaffAccountRow> Accounts { get; private set; } = new();
    public Guid Me { get; private set; }
    [TempData] public string? Notice { get; set; }
    [TempData] public string? Warning { get; set; }

    private async Task LoadViewAsync(bool fillInput)
    {
        var s = await _settings.GetAsync();
        if (fillInput) Input = SettingsInput.From(s);
        HasSmtpPassword = !string.IsNullOrEmpty(s.SmtpPassword);
        QrUrl = (await _settings.BankViewAsync()).QrUrl;
        Accounts = await _settings.StaffAccountsAsync();
        Me = AccessService.UserId(User) ?? Guid.Empty;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!_access.CanManageSettings(User)) return NotFound();
        await LoadViewAsync(true);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!_access.CanManageSettings(User)) return NotFound();
        if (!ModelState.IsValid)
        {
            await LoadViewAsync(false);
            return Page();
        }
        await _settings.SaveAsync(Input);
        Notice = _l["Common.Saved"].Value;
        return RedirectToPage();
    }

    /// <summary>R-018 deactivate (stamp rotated, sessions end) and Q-02 reactivate. A Lawyer cannot deactivate themselves.</summary>
    public async Task<IActionResult> OnPostActiveAsync(Guid userId, bool active)
    {
        if (!_access.CanManageSettings(User)) return NotFound();
        var error = await _settings.SetStaffActiveAsync(userId, active, AccessService.UserId(User)!.Value);
        switch (error)
        {
            case StaffAccountError.NotFound: return NotFound();
            case StaffAccountError.Self: Warning = _l["Staff.Settings.CannotDeactivateSelf"].Value; break;
            case StaffAccountError.None: Notice = _l[active ? "Staff.Settings.Reactivated" : "Staff.Settings.Deactivated"].Value; break;
            default: Warning = _l["Common.Error"].Value; break;
        }
        return RedirectToPage(null, null, "staff");
    }
}
