using ClientHub.Web.Data;
using ClientHub.Web.Data.Entities;
using ClientHub.Web.Services.Access;
using ClientHub.Web.Services.Cases;
using ClientHub.Web.Services.Documents;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace ClientHub.Web.Pages.Staff.Cases;

/// <summary>Case page (R-142, R-145): status, progress, updates, details, assigned staff, client card.</summary>
public class DetailsModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly IAccessService _access;
    private readonly ICaseService _cases;
    private readonly IDocumentService _documents;
    private readonly IStringLocalizer<SharedResource> _l;

    public DetailsModel(AppDbContext db, IAccessService access, ICaseService cases, IDocumentService documents, IStringLocalizer<SharedResource> l)
    {
        _db = db; _access = access; _cases = cases; _documents = documents; _l = l;
    }

    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }
    public Case Case { get; private set; } = null!;
    public bool CanAssign { get; private set; }
    public record UpdateRow(string Body, DateTime PostedAt, string Author);
    public List<UpdateRow> Updates { get; private set; } = new();
    public List<Choice> StaffChoices { get; private set; } = new();
    [TempData] public string? Notice { get; set; }
    public string? UpdateError { get; private set; }
    public List<RequestRow> Requests { get; private set; } = new();
    public List<DocumentRow> Documents { get; private set; } = new();
    public string? RequestError { get; private set; }
    public Guid? RejectFor { get; private set; }
    public string? RejectError { get; private set; }

    private async Task<bool> LoadAsync()
    {
        var c = await _access.CasesFor(User)
            .Include(x => x.Client).Include(x => x.Lawyer).Include(x => x.StaffLinks).ThenInclude(s => s.User)
            .FirstOrDefaultAsync(x => x.Id == Id);
        if (c is null) return false;
        Case = c;
        CanAssign = _access.IsLawyerOrOfficeManager(User);
        return true;
    }

    private async Task LoadViewAsync()
    {
        Updates = await _db.CaseUpdates.Where(u => u.CaseId == Id).OrderByDescending(u => u.PostedAt)
            .Join(_db.Users, u => u.AuthorId, a => a.Id, (u, a) => new UpdateRow(u.Body, u.PostedAt, a.DisplayName))
            .ToListAsync();
        Requests = await _documents.RequestsAsync(Id);
        Documents = await _documents.DocumentsAsync(Id);
        if (CanAssign)
        {
            var assigned = Case.StaffLinks.Select(s => s.UserId).ToHashSet();
            // Active Staff plus anyone already assigned (Q-18 keeps existing assignments of deactivated staff).
            StaffChoices = await _db.Users.Where(u => u.Role == Roles.Staff && (u.IsActive || assigned.Contains(u.Id)))
                .OrderBy(u => u.DisplayName).Select(u => new Choice(u.Id, u.DisplayName)).ToListAsync();
        }
    }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await LoadAsync()) return NotFound();
        await LoadViewAsync();
        return Page();
    }

    /// <summary>R-037, R-038, Q-09: any firm user who can open the case; any status to any status.</summary>
    public async Task<IActionResult> OnPostStatusAsync(string? status)
    {
        if (!await LoadAsync()) return NotFound();
        if (!CaseStatus.IsValid(status)) return BadRequest();
        await _cases.ChangeStatusAsync(Case, status!, AccessService.UserId(User)!.Value);
        Notice = _l["Staff.Case.StatusChanged"].Value;
        return RedirectToPage(new { id = Id });
    }

    /// <summary>R-041: post an update, dated automatically. Updates cannot be edited or deleted (Q-21).</summary>
    public async Task<IActionResult> OnPostUpdateAsync(string? body)
    {
        if (!await LoadAsync()) return NotFound();
        if (string.IsNullOrWhiteSpace(body) || body.Length > 4000)
        {
            UpdateError = _l[string.IsNullOrWhiteSpace(body) ? "Validation.Required" : "Validation.TooLong"];
            await LoadViewAsync();
            return Page();
        }
        await _cases.PostUpdateAsync(Id, AccessService.UserId(User)!.Value, body);
        Notice = _l["Staff.Case.UpdatePosted"].Value;
        return RedirectToPage(new { id = Id });
    }

    /// <summary>R-036, R-145: Lawyer and Office Manager only; Staff get 404 (R-015).</summary>
    public async Task<IActionResult> OnPostStaffAsync(List<Guid> staffIds)
    {
        if (!await LoadAsync() || !CanAssign) return NotFound();
        var error = await _cases.SetStaffAsync(Case, staffIds);
        if (error != CaseError.None) return BadRequest();
        Notice = _l["Common.Saved"].Value;
        return RedirectToPage(new { id = Id });
    }

    /// <summary>R-045: add a document request (name, optional note); it starts Pending.</summary>
    public async Task<IActionResult> OnPostRequestAsync(string? name, string? note)
    {
        if (!await LoadAsync()) return NotFound();
        if (string.IsNullOrWhiteSpace(name) || name.Length > DocumentService.NameMax || (note?.Length ?? 0) > DocumentService.NoteMax)
        {
            RequestError = _l[string.IsNullOrWhiteSpace(name) ? "Validation.Required" : "Validation.TooLong"];
            await LoadViewAsync();
            return Page();
        }
        await _documents.AddRequestAsync(Id, name, note);
        Notice = _l["Staff.Case.RequestAdded"].Value;
        return RedirectToPage(new { id = Id });
    }

    /// <summary>R-048: reject a Received request; a reason is required and shown to the client.</summary>
    public async Task<IActionResult> OnPostRejectAsync(Guid requestId, string? reason)
    {
        if (!await LoadAsync()) return NotFound();
        if (!await _db.DocumentRequests.AnyAsync(r => r.Id == requestId && r.CaseId == Id)) return NotFound();
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > DocumentService.ReasonMax)
        {
            RejectFor = requestId;
            RejectError = _l[string.IsNullOrWhiteSpace(reason) ? "Staff.Case.RejectReasonRequired" : "Validation.TooLong"];
            await LoadViewAsync();
            return Page();
        }
        if (!await _documents.RejectAsync(Id, requestId, reason)) return BadRequest();
        Notice = _l["Staff.Case.RequestRejected"].Value;
        return RedirectToPage(new { id = Id });
    }

    /// <summary>R-051, R-052: remove after confirmation; is_active = false, audit row, the stored object stays.</summary>
    public async Task<IActionResult> OnPostRemoveAsync(Guid documentId)
    {
        if (!await LoadAsync() || !_access.IsFirm(User)) return NotFound();
        if (!await _documents.RemoveAsync(Id, documentId, AccessService.UserId(User)!.Value)) return NotFound();
        Notice = _l["Staff.Case.DocumentRemoved"].Value;
        return RedirectToPage(new { id = Id });
    }
}
