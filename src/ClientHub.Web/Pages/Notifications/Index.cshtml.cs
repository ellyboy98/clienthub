using ClientHub.Web.Data.Entities;
using ClientHub.Web.Services.Access;
using ClientHub.Web.Services.Notifications;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ClientHub.Web.Pages.Notifications;

/// <summary>R-102, R-103, R-134: the signed-in user's own notifications; every handler is scoped to the current user.</summary>
public class IndexModel : PageModel
{
    private readonly INotificationService _notify;
    public IndexModel(INotificationService notify) => _notify = notify;

    public const int PageSize = 50;

    public List<Notification> Items { get; private set; } = new();
    /// <summary>1-based page, newest first. Older pages keep every notification reachable (the bell counts all unread).</summary>
    [BindProperty(SupportsGet = true, Name = "p")] public int PageNo { get; set; } = 1;
    public bool HasOlder { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (AccessService.UserId(User) is not { } me) return NotFound();
        if (PageNo < 1) PageNo = 1;
        Items = await _notify.ListAsync(me, (PageNo - 1) * PageSize, PageSize + 1, ct);
        HasOlder = Items.Count > PageSize;
        if (HasOlder) Items.RemoveAt(PageSize);
        return Page();
    }

    public async Task<bool> HasUnreadAsync() =>
        AccessService.UserId(User) is { } me && await _notify.UnreadCountAsync(me) > 0;

    public async Task<IActionResult> OnPostMarkAllAsync(CancellationToken ct)
    {
        if (AccessService.UserId(User) is not { } me) return NotFound();
        await _notify.MarkAllReadAsync(me, ct);
        return RedirectToPage(new { p = (int?)null });
    }
}
