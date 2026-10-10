using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ClientHub.Web.Pages;

/// <summary>The one page shown for every access failure or missing record (R-008, R-156). Never says "forbidden".</summary>
[AllowAnonymous]
[IgnoreAntiforgeryToken]
public class NotFoundModel : PageModel
{
    public void OnGet() => Response.StatusCode = StatusCodes.Status404NotFound;
    public void OnPost() => Response.StatusCode = StatusCodes.Status404NotFound;
}
