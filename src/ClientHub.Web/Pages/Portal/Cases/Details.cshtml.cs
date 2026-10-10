using ClientHub.Web.Data;
using ClientHub.Web.Data.Entities;
using ClientHub.Web.Services.Access;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace ClientHub.Web.Pages.Portal.Cases;

/// <summary>
/// Client case page (R-042, R-043, R-152): read only. Updates tab now; Documents (M4) and Bills (M6) tabs are filled later.
/// </summary>
public class DetailsModel : PageModel
{
    public const string TabUpdates = "updates", TabDocuments = "documents", TabBills = "bills";
    private readonly AppDbContext _db;
    private readonly IAccessService _access;
    public DetailsModel(AppDbContext db, IAccessService access) { _db = db; _access = access; }

    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }
    [BindProperty(SupportsGet = true)] public string Tab { get; set; } = TabUpdates;
    public Case Case { get; private set; } = null!;
    public record UpdateRow(string Body, DateTime PostedAt);
    public List<UpdateRow> Updates { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync()
    {
        var c = await _access.CasesFor(User).Include(x => x.Lawyer).FirstOrDefaultAsync(x => x.Id == Id);
        if (c is null) return NotFound();
        Case = c;
        if (Tab is not (TabDocuments or TabBills)) Tab = TabUpdates;
        Updates = await _db.CaseUpdates.Where(u => u.CaseId == Id).OrderByDescending(u => u.PostedAt)
            .Select(u => new UpdateRow(u.Body, u.PostedAt)).ToListAsync();
        return Page();
    }
}
