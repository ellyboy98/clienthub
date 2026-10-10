using System.ComponentModel.DataAnnotations;
using ClientHub.Web.Data.Entities;
using ClientHub.Web.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace ClientHub.Web.Pages.Account.Manage;

/// <summary>My account: change password, 2FA state, language (R-135).</summary>
public class IndexModel : PageModel
{
    private readonly UserManager<AppUser> _users;
    private readonly SignInManager<AppUser> _signIn;
    private readonly IStringLocalizer<SharedResource> _l;

    public IndexModel(UserManager<AppUser> users, SignInManager<AppUser> signIn, IStringLocalizer<SharedResource> l)
    {
        _users = users; _signIn = signIn; _l = l;
    }

    public AppUser Me { get; private set; } = null!;
    [BindProperty] public PasswordInput Pw { get; set; } = new();
    [BindProperty] public string Language { get; set; } = Cultures.English;
    [TempData] public string? Notice { get; set; }

    public class PasswordInput
    {
        [Required(ErrorMessage = "Validation.Required")] public string Current { get; set; } = "";
        [Required(ErrorMessage = "Validation.Required")] public string New { get; set; } = "";
        [Required(ErrorMessage = "Validation.Required"), Compare(nameof(New), ErrorMessage = "Validation.PasswordMismatch")]
        public string Confirm { get; set; } = "";
    }

    private async Task<bool> LoadAsync()
    {
        var me = await _users.GetUserAsync(User);
        if (me is null) return false;
        Me = me;
        return true;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await LoadAsync()) return NotFound();
        Language = Me.Language;
        return Page();
    }

    public async Task<IActionResult> OnPostPasswordAsync()
    {
        if (!await LoadAsync()) return NotFound();
        Language = Me.Language;
        foreach (var k in ModelState.Keys.Where(k => !k.StartsWith("Pw.")).ToList()) ModelState.Remove(k);
        if (!ModelState.IsValid) return Page();

        var result = await _users.ChangePasswordAsync(Me, Pw.Current, Pw.New);
        if (!result.Succeeded)
        {
            foreach (var e in result.Errors)
                ModelState.AddModelError(e.Code == "PasswordMismatch" ? "Pw.Current" : "Pw.New", IdentityErrors.Describe(_l, e));
            return Page();
        }
        await _signIn.RefreshSignInAsync(Me);
        Notice = _l["Account.Manage.PasswordChanged"];
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostLanguageAsync()
    {
        if (!await LoadAsync()) return NotFound();
        if (!Cultures.IsSupported(Language)) return NotFound();
        Me.Language = Language;
        await _users.UpdateAsync(Me);
        CultureModel.SetCookie(Response, Language);
        await _signIn.RefreshSignInAsync(Me);
        return RedirectToPage();
    }
}
