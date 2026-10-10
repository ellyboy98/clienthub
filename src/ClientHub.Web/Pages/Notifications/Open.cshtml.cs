using ClientHub.Web.Services.Access;
using ClientHub.Web.Services.Notifications;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ClientHub.Web.Pages.Notifications;

/// <summary>R-102: marks one of the user's own notifications read and opens its case, chat or bill. Others' ids are 404.</summary>
public class OpenModel : PageModel
{
    private readonly INotificationService _notify;
    public OpenModel(INotificationService notify) => _notify = notify;

    public IActionResult OnGet() => NotFound();

    public async Task<IActionResult> OnPostAsync(Guid id, CancellationToken ct)
    {
        if (AccessService.UserId(User) is not { } me) return NotFound();
        var link = await _notify.OpenAsync(me, id, ct);
        if (link is null || !Url.IsLocalUrl(link)) return NotFound();
        return LocalRedirect(link);
    }
}
