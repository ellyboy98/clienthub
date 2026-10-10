using ClientHub.Web.Services.Access;
using ClientHub.Web.Services.Clients;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace ClientHub.Web.Pages.Staff.Clients;

/// <summary>New client with its login (R-014, R-029, R-030). Lawyer and Office Manager only; others get 404 (R-015).</summary>
public class CreateModel : PageModel
{
    private readonly IAccessService _access;
    private readonly IClientService _clients;
    private readonly IStringLocalizer<SharedResource> _l;

    public CreateModel(IAccessService access, IClientService clients, IStringLocalizer<SharedResource> l)
    {
        _access = access; _clients = clients; _l = l;
    }

    [BindProperty] public ClientInput Input { get; set; } = new();

    public IActionResult OnGet() => _access.IsLawyerOrOfficeManager(User) ? Page() : NotFound();

    public async Task<IActionResult> OnPostAsync()
    {
        if (!_access.IsLawyerOrOfficeManager(User)) return NotFound();
        if (!ModelState.IsValid) return Page();
        var (client, error, emailSent) = await _clients.CreateAsync(Input);
        if (error == ClientError.EmailTaken) { ModelState.AddModelError("Input.Email", _l["Validation.EmailTaken"]); return Page(); }
        if (client is null) { ModelState.AddModelError("", _l["Common.Error"]); return Page(); }
        if (emailSent == false) TempData["Warning"] = _l["Staff.Clients.EmailNotSent"].Value;
        else TempData["Notice"] = _l["Staff.Clients.Created"].Value;
        return RedirectToPage("./Details", new { id = client.Id });
    }
}
