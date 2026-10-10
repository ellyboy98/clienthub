using ClientHub.Web.Data;
using ClientHub.Web.Data.Entities;
using ClientHub.Web.Services.Access;
using ClientHub.Web.Services.Clients;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace ClientHub.Web.Pages.Staff.Clients;

/// <summary>Edit client details: any firm user who can open the client (R-031, Q-14).</summary>
public class EditModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly IAccessService _access;
    private readonly IClientService _clients;
    private readonly IStringLocalizer<SharedResource> _l;

    public EditModel(AppDbContext db, IAccessService access, IClientService clients, IStringLocalizer<SharedResource> l)
    {
        _db = db; _access = access; _clients = clients; _l = l;
    }

    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }
    [BindProperty] public ClientInput Input { get; set; } = new();
    public Client Client { get; private set; } = null!;
    public bool CanChangeEmail => _access.IsLawyerOrOfficeManager(User);

    private async Task<bool> LoadAsync()
    {
        var c = await _access.ClientsFor(User).FirstOrDefaultAsync(x => x.Id == Id);
        if (c is null) return false;
        Client = c;
        return true;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await LoadAsync()) return NotFound();
        Input = ClientInput.From(Client);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!await LoadAsync()) return NotFound();
        // A Staff POST that changes the login email gets 404, like other Lawyer/OM-only fields (R-015 pattern).
        if (!CanChangeEmail && !ClientService.SameEmail(Input.Email, Client.Email)) return NotFound();
        if (!ModelState.IsValid) return Page();
        var (_, error, emailSent) = await _clients.UpdateAsync(Client, Input, CanChangeEmail);
        if (error == ClientError.EmailChangeNotAllowed) return NotFound();
        if (error == ClientError.EmailTaken) { ModelState.AddModelError("Input.Email", _l["Validation.EmailTaken"]); return Page(); }
        if (error != ClientError.None) { ModelState.AddModelError("", _l["Common.Error"]); return Page(); }
        if (emailSent == false) TempData["Warning"] = _l["Staff.Clients.EmailNotSent"].Value;
        else TempData["Notice"] = _l[emailSent == true ? "Staff.Clients.EmailChangedLinkSent" : "Common.Saved"].Value;
        return RedirectToPage("./Details", new { id = Id });
    }
}
