using ClientHub.Web.Data;
using ClientHub.Web.Data.Entities;
using ClientHub.Web.Services.Bills;
using ClientHub.Web.Services.Settings;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace ClientHub.Web.Pages.Portal.Bills;

/// <summary>
/// Client bill page (R-084, R-085, R-154): amount due, lines, total, status, note, bank details, reference, DuitNow QR, print.
/// IBillService.FindAsync is the gate: another client's bill or a Draft is 404 (R-079, R-008).
/// </summary>
public class DetailsModel : PageModel
{
    private readonly IBillService _bills;
    private readonly IFirmSettingsService _settings;
    private readonly AppDbContext _db;

    public DetailsModel(IBillService bills, IFirmSettingsService settings, AppDbContext db) { _bills = bills; _settings = settings; _db = db; }

    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }
    public Bill Bill { get; private set; } = null!;
    public string CaseTitle { get; private set; } = "";
    public BankView Bank { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync()
    {
        var b = await _bills.FindAsync(User, Id);
        if (b is null || b.Status == BillStatus.Draft) return NotFound();
        Bill = b;
        CaseTitle = await _db.Cases.Where(c => c.Id == b.CaseId).Select(c => c.Title).FirstAsync();
        Bank = await _settings.BankViewAsync();
        return Page();
    }
}
