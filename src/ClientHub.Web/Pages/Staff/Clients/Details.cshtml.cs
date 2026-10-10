using ClientHub.Web.Data;
using ClientHub.Web.Data.Entities;
using ClientHub.Web.Services.Access;
using ClientHub.Web.Services.Cases;
using ClientHub.Web.Services.Clients;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace ClientHub.Web.Pages.Staff.Clients;

/// <summary>Client page (R-141): details, login state, cases the user can open, chat link.</summary>
public class DetailsModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly IAccessService _access;
    private readonly IClientService _clients;
    private readonly IStringLocalizer<SharedResource> _l;

    public DetailsModel(AppDbContext db, IAccessService access, IClientService clients, IStringLocalizer<SharedResource> l)
    {
        _db = db; _access = access; _clients = clients; _l = l;
    }

    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }
    public Client Client { get; private set; } = null!;
    public bool LoginActive { get; private set; }
    public List<CaseRow> Cases { get; private set; } = new();
    public bool CanManage { get; private set; }
    [TempData] public string? Notice { get; set; }
    [TempData] public string? Warning { get; set; }

    private async Task<bool> LoadAsync()
    {
        var c = await _access.ClientsFor(User).Include(x => x.User).FirstOrDefaultAsync(x => x.Id == Id);
        if (c is null) return false;
        Client = c;
        LoginActive = c.User?.IsActive ?? false;
        CanManage = _access.IsLawyerOrOfficeManager(User);
        return true;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await LoadAsync()) return NotFound();
        Cases = await _access.CasesFor(User).Where(c => c.ClientId == Id)
            .ToRows(_db).OrderBy(r => r.ClosedAt != null).ThenByDescending(r => r.OpenedAt).ToListAsync();
        return Page();
    }

    public Task<IActionResult> OnPostDeactivateAsync() => SetActive(false);
    public Task<IActionResult> OnPostReactivateAsync() => SetActive(true);

    /// <summary>R-019, Q-01, Q-02: Lawyer and Office Manager deactivate or reactivate; Staff get 404.</summary>
    private async Task<IActionResult> SetActive(bool active)
    {
        if (!await LoadAsync() || !CanManage) return NotFound();
        await _clients.SetLoginActiveAsync(Client, active);
        Notice = _l[active ? "Staff.Client.Reactivated" : "Staff.Client.Deactivated"].Value;
        return RedirectToPage(new { id = Id });
    }
}
