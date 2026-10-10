using System.Security.Claims;
using ClientHub.Web.Data.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace ClientHub.Web.Services.Access;

/// <summary>Issues users.role (the single role source) as the role claim, plus display name, language and 2FA state.</summary>
public class AppClaimsPrincipalFactory : UserClaimsPrincipalFactory<AppUser>
{
    public const string DisplayNameClaim = "clienthub:name";
    public const string LanguageClaim = "clienthub:lang";
    public const string TwoFactorClaim = "clienthub:2fa";

    public AppClaimsPrincipalFactory(UserManager<AppUser> users, IOptions<IdentityOptions> options) : base(users, options) { }

    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(AppUser user)
    {
        var id = await base.GenerateClaimsAsync(user);
        id.AddClaim(new Claim(ClaimTypes.Role, user.Role));
        id.AddClaim(new Claim(DisplayNameClaim, user.DisplayName));
        id.AddClaim(new Claim(LanguageClaim, user.Language));
        id.AddClaim(new Claim(TwoFactorClaim, user.TwoFactorEnabled ? "1" : "0"));
        return id;
    }
}
