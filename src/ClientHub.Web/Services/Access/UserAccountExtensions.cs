using ClientHub.Web.Data.Entities;
using Microsoft.AspNetCore.Identity;

namespace ClientHub.Web.Services.Access;

public static class UserAccountExtensions
{
    /// <summary>The only way to deactivate a user: clears is_active and rotates the security stamp so open sessions end.</summary>
    public static async Task<IdentityResult> DeactivateAsync(this UserManager<AppUser> users, AppUser user)
    {
        user.IsActive = false;
        var res = await users.UpdateAsync(user);
        return res.Succeeded ? await users.UpdateSecurityStampAsync(user) : res;
    }
}
