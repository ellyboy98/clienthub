using ClientHub.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ClientHub.Web.Pages;

public class IndexModel : PageModel
{
    public IActionResult OnGet() => LocalRedirect(PageModelExtensions.HomeFor(User));
}
