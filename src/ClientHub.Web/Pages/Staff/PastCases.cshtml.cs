using ClientHub.Web.Data;
using ClientHub.Web.Services.Access;
using ClientHub.Web.Services.Cases;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace ClientHub.Web.Pages.Staff;

/// <summary>Completed and Cancelled cases the user can open (R-148). Lives at /Staff/PastCases to match the sidebar.</summary>
public class PastCasesModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly IAccessService _access;
    public PastCasesModel(AppDbContext db, IAccessService access) { _db = db; _access = access; }

    public List<CaseRow> Rows { get; private set; } = new();

    public async Task OnGetAsync() =>
        Rows = await _access.CasesFor(User).Past().ToRows(_db).OrderByDescending(r => r.ClosedAt).ToListAsync();
}
