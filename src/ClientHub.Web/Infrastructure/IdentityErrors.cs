using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Localization;

namespace ClientHub.Web.Infrastructure;

/// <summary>Identity error codes to resx text (Identity's own messages are English only).</summary>
public static class IdentityErrors
{
    private static readonly HashSet<string> Known = new()
    {
        "PasswordTooShort", "PasswordRequiresNonAlphanumeric", "PasswordRequiresDigit", "PasswordRequiresUpper",
        "PasswordRequiresLower", "PasswordMismatch", "InvalidToken", "PasswordRequiresUniqueChars"
    };

    public static string Describe(IStringLocalizer l, IdentityError e) =>
        Known.Contains(e.Code) ? l["Identity." + e.Code] : l["Identity.Generic"];
}
