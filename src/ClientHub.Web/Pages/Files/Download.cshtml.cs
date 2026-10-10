using ClientHub.Web.Services.Documents;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ClientHub.Web.Pages.Files;

/// <summary>
/// GET /Files/Download/{id}: access check, audit row, redirect to a 5-minute presigned GET (R-060..R-062).
/// Missing, removed or inaccessible documents are 404.
/// </summary>
public class DownloadModel : PageModel
{
    private readonly IDocumentService _documents;
    public DownloadModel(IDocumentService documents) => _documents = documents;

    public async Task<IActionResult> OnGetAsync(Guid id)
    {
        var url = await _documents.DownloadUrlAsync(User, id, HttpContext.RequestAborted);
        if (url is null) return NotFound();
        Response.Headers.CacheControl = "no-store";
        return Redirect(url);
    }
}
