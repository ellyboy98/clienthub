using ClientHub.Web.Data;
using ClientHub.Web.Data.Entities;
using ClientHub.Web.Services.Access;
using ClientHub.Web.Services.Bills;
using ClientHub.Web.Services.Documents;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace ClientHub.Web.Pages.Portal.Cases;

/// <summary>
/// Client case page (R-042, R-043, R-152): read only. Open requests at the top with Upload/Re-upload; Updates, Documents and Bills tabs
/// (Bills: Sent and Paid only, through IBillService.ListFor).
/// </summary>
public class DetailsModel : PageModel
{
    public const string TabUpdates = "updates", TabDocuments = "documents", TabBills = "bills";
    private readonly AppDbContext _db;
    private readonly IAccessService _access;
    private readonly IDocumentService _documents;
    private readonly IBillService _bills;
    public DetailsModel(AppDbContext db, IAccessService access, IDocumentService documents, IBillService bills)
    {
        _db = db; _access = access; _documents = documents; _bills = bills;
    }

    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }
    [BindProperty(SupportsGet = true)] public string Tab { get; set; } = TabUpdates;
    public Case Case { get; private set; } = null!;
    public record UpdateRow(string Body, DateTime PostedAt);
    public List<UpdateRow> Updates { get; private set; } = new();
    public List<RequestRow> OpenRequests { get; private set; } = new();
    /// <summary>Received requests, shown read only (status pill, no upload).</summary>
    public List<RequestRow> ReceivedRequests { get; private set; } = new();
    public List<DocumentRow> Documents { get; private set; } = new();
    public List<BillRow> Bills { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync()
    {
        var c = await _access.CasesFor(User).Include(x => x.Lawyer).FirstOrDefaultAsync(x => x.Id == Id);
        if (c is null) return NotFound();
        Case = c;
        if (Tab is not (TabDocuments or TabBills)) Tab = TabUpdates;
        Updates = await _db.CaseUpdates.Where(u => u.CaseId == Id).OrderByDescending(u => u.PostedAt)
            .Select(u => new UpdateRow(u.Body, u.PostedAt)).ToListAsync();
        var requests = await _documents.RequestsAsync(Id);
        OpenRequests = requests.Where(r => r.Status != RequestStatus.Received).ToList();
        ReceivedRequests = requests.Where(r => r.Status == RequestStatus.Received).ToList();
        if (Tab == TabDocuments) Documents = await _documents.DocumentsAsync(Id);
        if (Tab == TabBills) Bills = await _bills.ListFor(User, Id).ToListAsync();
        return Page();
    }
}
