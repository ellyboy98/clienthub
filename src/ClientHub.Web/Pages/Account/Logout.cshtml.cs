using ClientHub.Web.Data.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ClientHub.Web.Pages.Account;

[AllowAnonymous]
public class LogoutModel : PageModel
{
    private readonly SignInManager<AppUser> _signIn;
    public LogoutModel(SignInManager<AppUser> signIn) => _signIn = signIn;

    public IActionResult OnGet() => LocalRedirect("/");

    public async Task<IActionResult> OnPostAsync()
    {
        await _signIn.SignOutAsync();
        return RedirectToPage("./Login");
    }
}
