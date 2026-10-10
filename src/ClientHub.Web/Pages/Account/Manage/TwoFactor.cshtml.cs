using System.Globalization;
using System.Text;
using ClientHub.Web.Data.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using QRCoder;

namespace ClientHub.Web.Pages.Account.Manage;

/// <summary>Authenticator setup; recovery codes shown once (R-023, R-024). Lawyer and OM cannot turn 2FA off.</summary>
public class TwoFactorModel : PageModel
{
    private readonly UserManager<AppUser> _users;
    private readonly SignInManager<AppUser> _signIn;
    private readonly IStringLocalizer<SharedResource> _l;
    private readonly IConfiguration _config;

    public TwoFactorModel(UserManager<AppUser> users, SignInManager<AppUser> signIn, IStringLocalizer<SharedResource> l, IConfiguration config)
    {
        _users = users; _signIn = signIn; _l = l; _config = config;
    }

    public AppUser Me { get; private set; } = null!;
    public string SharedKey { get; private set; } = "";
    public string QrSvg { get; private set; } = "";
    public bool Required { get; private set; }
    public string[]? RecoveryCodes { get; private set; }
    [BindProperty] public string Code { get; set; } = "";
    public string? Error { get; set; }

    private async Task<bool> LoadAsync()
    {
        var me = await _users.GetUserAsync(User);
        if (me is null) return false;
        Me = me;
        Required = me.Role is Roles.Lawyer or Roles.OfficeManager;
        if (!me.TwoFactorEnabled)
        {
            var key = await _users.GetAuthenticatorKeyAsync(me);
            if (string.IsNullOrEmpty(key))
            {
                await _users.ResetAuthenticatorKeyAsync(me);
                key = await _users.GetAuthenticatorKeyAsync(me);
            }
            SharedKey = FormatKey(key!);
            var issuer = Uri.EscapeDataString(_l["Common.AppName"]);
            var uri = $"otpauth://totp/{issuer}:{Uri.EscapeDataString(me.Email!)}?secret={key}&issuer={issuer}&digits=6";
            using var data = new QRCodeGenerator().CreateQrCode(uri, QRCodeGenerator.ECCLevel.Q);
            QrSvg = new SvgQRCode(data).GetGraphic(5);
        }
        return true;
    }

    public async Task<IActionResult> OnGetAsync() => await LoadAsync() ? Page() : NotFound();

    public async Task<IActionResult> OnPostEnableAsync()
    {
        if (!await LoadAsync()) return NotFound();
        if (Me.TwoFactorEnabled) return RedirectToPage();
        var code = Code.Replace(" ", "").Replace("-", "");
        var ok = await _users.VerifyTwoFactorTokenAsync(Me, _users.Options.Tokens.AuthenticatorTokenProvider, code);
        if (!ok)
        {
            Error = _l["Account.LoginWith2fa.Invalid"];
            return Page();
        }
        await _users.SetTwoFactorEnabledAsync(Me, true);
        RecoveryCodes = (await _users.GenerateNewTwoFactorRecoveryCodesAsync(Me, 10))?.ToArray();
        await _signIn.RefreshSignInAsync(Me);
        return Page();
    }

    public async Task<IActionResult> OnPostDisableAsync()
    {
        if (!await LoadAsync()) return NotFound();
        if (Required) return NotFound();
        await _users.SetTwoFactorEnabledAsync(Me, false);
        await _users.ResetAuthenticatorKeyAsync(Me);
        await _signIn.RefreshSignInAsync(Me);
        return RedirectToPage("./Index");
    }

    private static string FormatKey(string key)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < key.Length; i += 4)
            sb.Append(key.AsSpan(i, Math.Min(4, key.Length - i))).Append(' ');
        return sb.ToString().TrimEnd().ToLower(CultureInfo.InvariantCulture);
    }
}
