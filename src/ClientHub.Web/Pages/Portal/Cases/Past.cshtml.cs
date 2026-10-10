using ClientHub.Web.Data;
using ClientHub.Web.Services.Access;
using ClientHub.Web.Services.Cases;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace ClientHub.Web.Pages.Portal.Cases;

/// <summary>The client's Completed and Cancelled cases (R-155).</summary>
public class PastModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly IAccessService _access;
    public PastModel(AppDbContext db, IAccessService access) { _db = db; _access = access; }

    public List<CaseRow> Cases { get; private set; } = new();

    public async Task OnGetAsync() =>
        Cases = await _access.CasesFor(User).Past().ToRows(_db).OrderByDescending(r => r.ClosedAt).ToListAsync();
}
