using ClientHub.Web.Services.Access;
using ClientHub.Web.Services.Settings;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace ClientHub.Web.Pages.Staff.Settings;

/// <summary>Add a firm account (R-016): Lawyer, Office Manager or Staff, no password; a set-password link is emailed. Lawyer only (R-017).</summary>
public class StaffAccountsModel : PageModel
{
    private readonly IFirmSettingsService _settings;
    private readonly IAccessService _access;
    private readonly IStringLocalizer<SharedResource> _l;

    public StaffAccountsModel(IFirmSettingsService settings, IAccessService access, IStringLocalizer<SharedResource> l)
    {
        _settings = settings; _access = access; _l = l;
    }

    [BindProperty] public StaffAccountInput Input { get; set; } = new();
    [TempData] public string? Notice { get; set; }
    [TempData] public string? Warning { get; set; }

    public IActionResult OnGet() => _access.CanManageSettings(User) ? Page() : NotFound();

    public async Task<IActionResult> OnPostAsync()
    {
        if (!_access.CanManageSettings(User)) return NotFound();
        if (!ModelState.IsValid) return Page();
        var r = await _settings.CreateStaffAccountAsync(Input);
        switch (r.Error)
        {
            case StaffAccountError.None:
                if (r.EmailSent) Notice = string.Format(_l["Staff.Settings.AccountCreated"].Value, r.User!.Email);
                else Warning = _l["Staff.Settings.EmailNotSent"].Value;
                return RedirectToPage("Index", null, "staff");
            case StaffAccountError.EmailTaken:
                ModelState.AddModelError("Input.Email", _l["Validation.EmailTaken"]);
                return Page();
            case StaffAccountError.InvalidRole:
                ModelState.AddModelError("Input.Role", _l["Validation.Required"]);
                return Page();
            default:
                ModelState.AddModelError(string.Empty, _l["Common.Error"]);
                return Page();
        }
    }
}
