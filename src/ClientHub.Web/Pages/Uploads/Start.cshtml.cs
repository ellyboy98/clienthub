using ClientHub.Web.Services.Storage;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace ClientHub.Web.Pages.Uploads;

/// <summary>
/// POST /Uploads/Start (antiforgery): access check, server-built key, 10-minute presigned PUT and a protected ticket (R-057, R-059).
/// The browser sends only the target, the file name and its size; it never chooses a key or a content type.
/// </summary>
public class StartModel : PageModel
{
    private readonly IUploadService _uploads;
    private readonly IStringLocalizer<SharedResource> _l;
    public StartModel(IUploadService uploads, IStringLocalizer<SharedResource> l) { _uploads = uploads; _l = l; }

    public IActionResult OnGet() => NotFound();

    public async Task<IActionResult> OnPostAsync(string? kind, Guid? caseId, Guid? requestId, string? fileName, long size)
    {
        if ((kind is not null && kind != UploadTargetKind.Case) || caseId is null) return NotFound();
        var r = await _uploads.StartAsync(User, UploadTarget.Case(caseId.Value, requestId), fileName, size, HttpContext.RequestAborted);
        if (r.Error != UploadError.None) return UploadErrors.ToResult(r.Error, _l);
        return new JsonResult(new { url = r.Url, ticket = r.Ticket, contentType = r.ContentType });
    }
}
