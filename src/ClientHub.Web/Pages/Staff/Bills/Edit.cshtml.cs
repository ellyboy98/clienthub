using ClientHub.Web.Data;
using ClientHub.Web.Data.Entities;
using ClientHub.Web.Services.Access;
using ClientHub.Web.Services.Bills;
using ClientHub.Web.Services.Settings;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace ClientHub.Web.Pages.Staff.Bills;

/// <summary>
/// Bill editor (R-077..R-083, R-147). Every handler loads the bill through IBillService.FindAsync (case access) and returns 404
/// on failure. Line add/edit/remove answer HTMX with the lines block; without HTMX they redirect back.
/// </summary>
public class EditModel : PageModel
{
    private readonly IBillService _bills;
    private readonly IFirmSettingsService _settings;
    private readonly AppDbContext _db;
    private readonly IStringLocalizer<SharedResource> _l;

    public EditModel(IBillService bills, IFirmSettingsService settings, AppDbContext db, IStringLocalizer<SharedResource> l)
    {
        _bills = bills; _settings = settings; _db = db; _l = l;
    }

    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }
    public Bill Bill { get; private set; } = null!;
    public string CaseTitle { get; private set; } = "";
    public Guid ClientId { get; private set; }
    public string ClientName { get; private set; } = "";
    public BankView Bank { get; private set; } = null!;
    /// <summary>Line form error shown in the lines block; LineErrorFor = the line it belongs to (null = the add form).</summary>
    public string? LineError { get; private set; }
    public Guid? LineErrorFor { get; private set; }
    [TempData] public string? Notice { get; set; }
    [TempData] public string? Warning { get; set; }

    private bool IsHtmx => Request.Headers.ContainsKey("HX-Request");

    private async Task<bool> LoadAsync()
    {
        var b = await _bills.FindAsync(User, Id);
        if (b is null) return false;
        Bill = b;
        var info = await _db.Cases.Where(c => c.Id == b.CaseId)
            .Select(c => new { c.Title, c.ClientId, ClientName = c.Client!.Name }).FirstAsync();
        CaseTitle = info.Title;
        ClientId = info.ClientId;
        ClientName = info.ClientName;
        return true;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await LoadAsync()) return NotFound();
        Bank = await _settings.BankViewAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAddLineAsync(string? lineType, string? description, string? amount)
    {
        if (!await LoadAsync()) return NotFound();
        return await LinesResultAsync(await _bills.AddLineAsync(Id, new LineInput(lineType, description, amount)), null);
    }

    public async Task<IActionResult> OnPostUpdateLineAsync(Guid lineId, string? lineType, string? description, string? amount)
    {
        if (!await LoadAsync()) return NotFound();
        return await LinesResultAsync(await _bills.UpdateLineAsync(Id, lineId, new LineInput(lineType, description, amount)), lineId);
    }

    public async Task<IActionResult> OnPostRemoveLineAsync(Guid lineId)
    {
        if (!await LoadAsync()) return NotFound();
        return await LinesResultAsync(await _bills.RemoveLineAsync(Id, lineId), lineId);
    }

    public async Task<IActionResult> OnPostNoteAsync(string? note)
    {
        if (!await LoadAsync()) return NotFound();
        var error = await _bills.SaveNoteAsync(Id, note);
        if (error == BillError.NotDraft) return BadRequest();
        if (error == BillError.Invalid) Warning = _l["Validation.TooLong"].Value;
        else Notice = _l["Common.Saved"].Value;
        return RedirectToPage(new { id = Id });
    }

    /// <summary>R-080, Q-08: Draft to Sent, at least one line.</summary>
    public async Task<IActionResult> OnPostSendAsync()
    {
        if (!await LoadAsync()) return NotFound();
        var error = await _bills.MarkSentAsync(Id, AccessService.UserId(User)!.Value);
        switch (error)
        {
            case BillError.None: Notice = _l["Staff.Bill.SentNotice"].Value; break;
            case BillError.NoLines: Warning = _l["Staff.Bill.NeedsLine"].Value; break;
            default: return BadRequest();
        }
        return RedirectToPage(new { id = Id });
    }

    /// <summary>R-082: Sent to Paid only.</summary>
    public async Task<IActionResult> OnPostPaidAsync()
    {
        if (!await LoadAsync()) return NotFound();
        if (await _bills.MarkPaidAsync(Id, AccessService.UserId(User)!.Value) != BillError.None) return BadRequest();
        Notice = _l["Staff.Bill.PaidNotice"].Value;
        return RedirectToPage(new { id = Id });
    }

    private async Task<IActionResult> LinesResultAsync(BillError error, Guid? lineId)
    {
        switch (error)
        {
            case BillError.NotFound: return NotFound();
            case BillError.NotDraft: return BadRequest(); // R-081: Sent and Paid bills have frozen lines
            case BillError.Invalid or BillError.TooLarge:
                LineError = _l[error == BillError.TooLarge ? "Staff.Bill.TotalTooLarge" : "Staff.Bill.LineInvalid"].Value;
                LineErrorFor = lineId;
                break;
        }
        if (!IsHtmx)
        {
            if (LineError is not null) Warning = LineError;
            return RedirectToPage(new { id = Id });
        }
        Bill = (await _bills.FindAsync(User, Id))!;
        return Partial("_BillLines", this);
    }
}
