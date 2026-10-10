using ClientHub.Web.Data;
using ClientHub.Web.Services.Access;
using ClientHub.Web.Services.Cases;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace ClientHub.Web.Pages.Staff.Cases;

/// <summary>Active cases the user can open (Q-04).</summary>
public class IndexModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly IAccessService _access;
    public IndexModel(AppDbContext db, IAccessService access) { _db = db; _access = access; }

    public List<CaseRow> Rows { get; private set; } = new();

    public async Task OnGetAsync() =>
        Rows = await _access.CasesFor(User).Active().ToRows(_db).OrderByDescending(r => r.OpenedAt).ToListAsync();
}
