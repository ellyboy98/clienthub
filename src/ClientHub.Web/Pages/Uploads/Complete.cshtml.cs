using ClientHub.Web.Services.Storage;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace ClientHub.Web.Pages.Uploads;

/// <summary>
/// POST /Uploads/Complete (antiforgery): unprotects the ticket, checks it belongs to this user, re-checks access to its target,
/// validates the stored object with HeadObject and writes the rows (R-054, R-055, R-058).
/// </summary>
public class CompleteModel : PageModel
{
    private readonly IUploadService _uploads;
    private readonly IStringLocalizer<SharedResource> _l;
    public CompleteModel(IUploadService uploads, IStringLocalizer<SharedResource> l) { _uploads = uploads; _l = l; }

    public IActionResult OnGet() => NotFound();

    public async Task<IActionResult> OnPostAsync(string? ticket, string? caption)
    {
        var r = await _uploads.CompleteAsync(User, ticket, caption, HttpContext.RequestAborted);
        if (r.Error != UploadError.None) return UploadErrors.ToResult(r.Error, _l);
        return new JsonResult(new { ok = true, id = r.EntityId });
    }
}
