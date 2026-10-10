using ClientHub.Web.Data.Entities;
using ClientHub.Web.Services.Access;
using ClientHub.Web.Services.Cases;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace ClientHub.Web.Pages.Staff.Cases;

/// <summary>
/// Edit case fields. Q-10: anyone who can open the case edits title, type, description; only Lawyer and Office Manager
/// change the lawyer in charge and assigned staff. A Staff POST that carries those fields gets 404 (R-015).
/// </summary>
public class EditModel : PageModel
{
    private readonly IAccessService _access;
    private readonly ICaseService _cases;
    private readonly IStringLocalizer<SharedResource> _l;

    public EditModel(IAccessService access, ICaseService cases, IStringLocalizer<SharedResource> l)
    {
        _access = access; _cases = cases; _l = l;
    }

    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }
    [BindProperty] public CaseForm Form { get; set; } = new();
    public Case Case { get; private set; } = null!;
    public CaseFieldsModel Fields { get; private set; } = null!;
    private bool CanAssign => _access.IsLawyerOrOfficeManager(User);

    private async Task<bool> LoadAsync()
    {
        var c = await _access.CasesFor(User).Include(x => x.Client).Include(x => x.Lawyer)
            .Include(x => x.StaffLinks).ThenInclude(s => s.User).FirstOrDefaultAsync(x => x.Id == Id);
        if (c is null) return false;
        Case = c;
        return true;
    }

    private async Task LoadChoicesAsync()
    {
        if (!CanAssign) { Fields = new CaseFieldsModel(Form, false, [], []); return; }
        var lawyers = (await _cases.LawyerChoicesAsync()).Select(u => new Choice(u.Id, u.DisplayName)).ToList();
        if (lawyers.All(l => l.Id != Case.LawyerId) && Case.Lawyer is { } current) lawyers.Insert(0, new Choice(current.Id, current.DisplayName));
        var staff = (await _cases.StaffChoicesAsync()).Select(u => new Choice(u.Id, u.DisplayName)).ToList();
        // Keep already-assigned (possibly deactivated) staff visible so saving does not drop them (Q-18).
        foreach (var link in Case.StaffLinks.Where(l => l.User is not null && staff.All(s => s.Id != l.UserId)))
            staff.Add(new Choice(link.UserId, link.User!.DisplayName));
        Fields = new CaseFieldsModel(Form, true, lawyers, staff);
    }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await LoadAsync()) return NotFound();
        Form = new CaseForm
        {
            Title = Case.Title, CaseType = Case.CaseType, Description = Case.Description, LawyerId = Case.LawyerId,
            StaffIds = Case.StaffLinks.Select(s => s.UserId).ToList()
        };
        await LoadChoicesAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!await LoadAsync()) return NotFound();
        if (!CanAssign && (Form.LawyerId is not null || Request.Form.ContainsKey("Form.StaffIds"))) return NotFound();
        if (CanAssign && Form.LawyerId is null) ModelState.AddModelError("Form.LawyerId", _l["Validation.Required"]);
        if (!ModelState.IsValid) { await LoadChoicesAsync(); return Page(); }

        var error = await _cases.UpdateDetailsAsync(Case, Form.Title!, Form.CaseType, Form.Description, CanAssign ? Form.LawyerId : null);
        if (error == CaseError.None && CanAssign) error = await _cases.SetStaffAsync(Case, Form.StaffIds);
        if (error != CaseError.None)
        {
            var key = error switch { CaseError.InvalidLawyer => "Form.LawyerId", CaseError.InvalidStaff => "Form.StaffIds", _ => "Form.CaseType" };
            ModelState.AddModelError(key, _l["Validation.InvalidChoice"]);
            await LoadChoicesAsync();
            return Page();
        }
        TempData["Notice"] = _l["Common.Saved"].Value;
        return RedirectToPage("./Details", new { id = Id });
    }
}
