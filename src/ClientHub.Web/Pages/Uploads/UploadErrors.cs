using ClientHub.Web.Services.Storage;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace ClientHub.Web.Pages.Uploads;

internal static class UploadErrors
{
    /// <summary>404 for access failures (R-008); 400 with a localised message for everything else.</summary>
    public static IActionResult ToResult(UploadError e, IStringLocalizer l) => e switch
    {
        UploadError.NotFound => new NotFoundResult(),
        _ => new BadRequestObjectResult(new { error = l[e switch
        {
            UploadError.BadType => "Upload.Error.BadType",
            UploadError.TooLarge => "Upload.Error.TooLarge",
            UploadError.Empty => "Upload.Error.Empty",
            UploadError.Missing => "Upload.Error.Missing",
            UploadError.CaptionTooLong => "Chat.Error.TooLong",
            _ => "Upload.Error.Expired"
        }].Value })
    };
}
