using ClientHub.Web.Services.Access;
using ClientHub.Web.Services.Cases;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace ClientHub.Web.Pages.Staff.Cases;

/// <summary>New case (R-034..R-036). Lawyer and Office Manager only; others get 404 (R-015).</summary>
public class CreateModel : PageModel
{
    private readonly IAccessService _access;
    private readonly ICaseService _cases;
    private readonly IStringLocalizer<SharedResource> _l;

    public CreateModel(IAccessService access, ICaseService cases, IStringLocalizer<SharedResource> l)
    {
        _access = access; _cases = cases; _l = l;
    }

    [BindProperty(SupportsGet = true)] public Guid? ClientId { get; set; }
    [BindProperty] public CaseForm Form { get; set; } = new();
    public List<Choice> Clients { get; private set; } = new();
    public CaseFieldsModel Fields { get; private set; } = null!;

    private async Task LoadChoicesAsync()
    {
        Clients = await _access.ClientsFor(User).OrderBy(c => c.Name).Select(c => new Choice(c.Id, c.Name)).ToListAsync();
        var lawyers = (await _cases.LawyerChoicesAsync()).Select(u => new Choice(u.Id, u.DisplayName)).ToList();
        var staff = (await _cases.StaffChoicesAsync()).Select(u => new Choice(u.Id, u.DisplayName)).ToList();
        Fields = new CaseFieldsModel(Form, true, lawyers, staff);
    }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!_access.IsLawyerOrOfficeManager(User)) return NotFound();
        if (ClientId is { } cid && !await _access.CanAccessClientAsync(User, cid)) return NotFound();
        await LoadChoicesAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!_access.IsLawyerOrOfficeManager(User)) return NotFound();
        if (ClientId is null) ModelState.AddModelError(nameof(ClientId), _l["Validation.Required"]);
        else if (!await _access.CanAccessClientAsync(User, ClientId.Value)) return NotFound();
        if (Form.LawyerId is null) ModelState.AddModelError("Form.LawyerId", _l["Validation.Required"]);
        if (!ModelState.IsValid) { await LoadChoicesAsync(); return Page(); }

        var (c, error) = await _cases.CreateAsync(new CaseInput(ClientId!.Value, Form.Title!, Form.CaseType, Form.LawyerId!.Value, Form.Description, Form.StaffIds));
        if (c is null)
        {
            var key = error switch
            {
                CaseError.InvalidLawyer => "Form.LawyerId",
                CaseError.InvalidStaff => "Form.StaffIds",
                CaseError.InvalidType => "Form.CaseType",
                _ => nameof(ClientId)
            };
            ModelState.AddModelError(key, _l["Validation.InvalidChoice"]);
            await LoadChoicesAsync();
            return Page();
        }
        TempData["Notice"] = _l["Staff.Cases.Created"].Value;
        return RedirectToPage("./Details", new { id = c.Id });
    }
}
