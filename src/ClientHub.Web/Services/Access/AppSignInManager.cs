using ClientHub.Web.Data.Entities;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace ClientHub.Web.Services.Access;

/// <summary>Inactive users cannot sign in with any password (R-020).</summary>
public class AppSignInManager : SignInManager<AppUser>
{
    public AppSignInManager(UserManager<AppUser> userManager, IHttpContextAccessor contextAccessor,
        IUserClaimsPrincipalFactory<AppUser> claimsFactory, IOptions<IdentityOptions> optionsAccessor,
        ILogger<SignInManager<AppUser>> logger, IAuthenticationSchemeProvider schemes, IUserConfirmation<AppUser> confirmation)
        : base(userManager, contextAccessor, claimsFactory, optionsAccessor, logger, schemes, confirmation) { }

    public override async Task<bool> CanSignInAsync(AppUser user) =>
        user.IsActive && await base.CanSignInAsync(user);

    /// <summary>Periodic cookie check: a deactivated user loses the session at the next validation.</summary>
    public override async Task<AppUser?> ValidateSecurityStampAsync(System.Security.Claims.ClaimsPrincipal? principal)
    {
        var user = await base.ValidateSecurityStampAsync(principal);
        return user is { IsActive: true } ? user : null;
    }
}
