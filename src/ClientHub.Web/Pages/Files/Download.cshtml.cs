using ClientHub.Web.Services.Chat;
using ClientHub.Web.Services.Documents;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ClientHub.Web.Pages.Files;

/// <summary>
/// GET /Files/Download/{id}: access check, audit row, redirect to a 5-minute presigned GET (R-060..R-062).
/// Missing, removed or inaccessible documents are 404.
/// GET /Files/Download/{messageId}?handler=Message: the same for a chat file, gated by CanAccessClient (R-074).
/// </summary>
public class DownloadModel : PageModel
{
    private readonly IDocumentService _documents;
    private readonly IChatService _chat;
    public DownloadModel(IDocumentService documents, IChatService chat) { _documents = documents; _chat = chat; }

    public async Task<IActionResult> OnGetMessageAsync(Guid id)
    {
        var url = await _chat.DownloadUrlAsync(User, id, HttpContext.RequestAborted);
        if (url is null) return NotFound();
        Response.Headers.CacheControl = "no-store";
        return Redirect(url);
    }

    public async Task<IActionResult> OnGetAsync(Guid id)
    {
        var url = await _documents.DownloadUrlAsync(User, id, HttpContext.RequestAborted);
        if (url is null) return NotFound();
        Response.Headers.CacheControl = "no-store";
        return Redirect(url);
    }
}
