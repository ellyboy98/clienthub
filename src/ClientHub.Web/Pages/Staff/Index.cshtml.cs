using ClientHub.Web.Data;
using ClientHub.Web.Data.Entities;
using ClientHub.Web.Services.Access;
using ClientHub.Web.Services.Cases;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace ClientHub.Web.Pages.Staff;

/// <summary>
/// Dashboard (R-137, R-138 first half): four tiles counted over cases/clients the user can open, and the active-cases table.
/// The "Needs your attention" feed is cut for v1 (decisions.md).
/// </summary>
public class IndexModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly IAccessService _access;
    public IndexModel(AppDbContext db, IAccessService access) { _db = db; _access = access; }

    public int ActiveCases { get; private set; }
    public int PendingDocuments { get; private set; }
    public int UnreadChats { get; private set; }
    public int BillsAwaiting { get; private set; }
    public List<CaseRow> Rows { get; private set; } = new();

    public async Task OnGetAsync()
    {
        var cases = _access.CasesFor(User);
        var caseIds = cases.Select(c => c.Id);
        var clients = _access.ClientsFor(User);

        ActiveCases = await cases.Active().CountAsync();
        // Q-03: requests waiting on the client (Pending or Rejected).
        PendingDocuments = await _db.DocumentRequests
            .CountAsync(r => caseIds.Contains(r.CaseId) && (r.Status == RequestStatus.Pending || r.Status == RequestStatus.Rejected));
        // Chats with at least one unread message sent by the client's own login.
        UnreadChats = await clients.CountAsync(cl =>
            _db.Messages.Any(m => m.ClientId == cl.Id && m.ReadAt == null && m.SenderId == cl.UserId));
        BillsAwaiting = await _db.Bills.CountAsync(b => caseIds.Contains(b.CaseId) && b.Status == BillStatus.Sent);
        Rows = await cases.Active().ToRows(_db).OrderByDescending(r => r.LastUpdateAt ?? r.OpenedAt).ToListAsync();
    }
}
