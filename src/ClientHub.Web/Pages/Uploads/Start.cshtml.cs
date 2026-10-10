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

    public async Task<IActionResult> OnPostAsync(string? kind, Guid? caseId, Guid? requestId, Guid? clientId, string? fileName, long size)
    {
        UploadTarget? target = kind switch
        {
            null or UploadTargetKind.Case when caseId is { } c => UploadTarget.Case(c, requestId),
            UploadTargetKind.Chat when clientId is { } cl => UploadTarget.Chat(cl),
            UploadTargetKind.SettingsQr => UploadTarget.SettingsQr(),
            _ => null
        };
        if (target is null) return NotFound();
        var r = await _uploads.StartAsync(User, target, fileName, size, HttpContext.RequestAborted);
        if (r.Error != UploadError.None) return UploadErrors.ToResult(r.Error, _l);
        return new JsonResult(new { url = r.Url, ticket = r.Ticket, contentType = r.ContentType });
    }
}
