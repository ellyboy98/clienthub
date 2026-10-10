using ClientHub.Web.Data;
using ClientHub.Web.Data.Entities;
using ClientHub.Web.Services.Access;
using ClientHub.Web.Services.Cases;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace ClientHub.Web.Pages.Portal;

/// <summary>Client Home (R-151): greeting, documents still needed, active case cards, Messages and Bills tiles.</summary>
public class IndexModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly IAccessService _access;
    public IndexModel(AppDbContext db, IAccessService access) { _db = db; _access = access; }

    public string Name { get; private set; } = "";
    public record NeededRow(Guid CaseId, string CaseTitle, string Name, string Status, string? Reason);
    public List<NeededRow> Needed { get; private set; } = new();
    public List<CaseRow> Cases { get; private set; } = new();
    public int UnreadMessages { get; private set; }
    public decimal AmountUnpaid { get; private set; }

    public async Task OnGetAsync()
    {
        var me = AccessService.UserId(User);
        var client = await _access.ClientsFor(User).FirstOrDefaultAsync();
        Name = client?.Name ?? User.Identity?.Name ?? "";
        var cases = _access.CasesFor(User);
        var caseIds = cases.Select(c => c.Id);

        Needed = await _db.DocumentRequests
            .Where(r => caseIds.Contains(r.CaseId) && (r.Status == RequestStatus.Pending || r.Status == RequestStatus.Rejected))
            .Join(_db.Cases, r => r.CaseId, c => c.Id, (r, c) => new { r, c })
            .OrderBy(x => x.r.CreatedAt)
            .Select(x => new NeededRow(x.c.Id, x.c.Title, x.r.Name, x.r.Status, x.r.RejectionReason))
            .ToListAsync();
        Cases = await cases.Active().ToRows(_db).OrderByDescending(r => r.OpenedAt).ToListAsync();
        if (client is not null)
            UnreadMessages = await _db.Messages.CountAsync(m => m.ClientId == client.Id && m.ReadAt == null && m.SenderId != me);
        AmountUnpaid = await _db.Bills.Where(b => caseIds.Contains(b.CaseId) && b.Status == BillStatus.Sent).SumAsync(b => (decimal?)b.Total) ?? 0m;
    }
}
