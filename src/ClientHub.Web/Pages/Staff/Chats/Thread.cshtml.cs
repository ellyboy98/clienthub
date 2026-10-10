using ClientHub.Web.Pages.Shared.Components;
using ClientHub.Web.Services.Access;
using ClientHub.Web.Services.Chat;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace ClientHub.Web.Pages.Staff.Chats;

/// <summary>
/// /Staff/Chats/{clientId} (R-064, R-065, R-146): thread, 5-second polling (?handler=New) and send (?handler=Send).
/// Every handler goes through CanAccessClient (inside IChatService) and returns 404 on failure.
/// </summary>
public class ThreadModel : PageModel
{
    private readonly IChatService _chat;
    private readonly IStringLocalizer<SharedResource> _l;
    public ThreadModel(IChatService chat, IStringLocalizer<SharedResource> l) { _chat = chat; _l = l; }

    public ChatThread Thread { get; private set; } = null!;
    public List<ChatListItem> Items { get; private set; } = new();
    public ChatViewModel Chat { get; private set; } = null!;

    private ChatViewModel ViewFor(Guid clientId) => new(clientId, AccessService.UserId(User) ?? Guid.Empty, true,
        $"/Staff/Chats/{clientId}?handler=New", $"/Staff/Chats/{clientId}?handler=Send");

    public async Task<IActionResult> OnGetAsync(Guid clientId)
    {
        var thread = await _chat.OpenAsync(User, clientId, HttpContext.RequestAborted);
        if (thread is null) return NotFound();
        Thread = thread;
        // Listed after opening, so this chat's pill is already cleared.
        Items = await _chat.ListAsync(User, HttpContext.RequestAborted);
        Chat = ViewFor(clientId);
        return Page();
    }

    /// <summary>visible=1 only while the page is visible (chat.js); background-tab polls fetch but do not mark read.</summary>
    public async Task<IActionResult> OnGetNewAsync(Guid clientId, long? after, int? visible)
    {
        var messages = await _chat.NewSinceAsync(User, clientId, after, visible == 1, HttpContext.RequestAborted);
        return messages is null ? NotFound() : ChatResults.NewMessages(this, ViewFor(clientId), messages, after);
    }

    public async Task<IActionResult> OnPostSendAsync(Guid clientId, string? body)
    {
        var error = await _chat.SendAsync(User, clientId, body, HttpContext.RequestAborted);
        return ChatResults.Send(this, error, _l, $"/Staff/Chats/{clientId}");
    }
}
