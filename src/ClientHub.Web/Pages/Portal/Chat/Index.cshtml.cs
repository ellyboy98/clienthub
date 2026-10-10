using ClientHub.Web.Pages.Shared.Components;
using ClientHub.Web.Pages.Staff.Chats;
using ClientHub.Web.Services.Access;
using ClientHub.Web.Services.Chat;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace ClientHub.Web.Pages.Portal.Chat;

/// <summary>
/// /Portal/Chat (R-066, R-153): the client's own chat with the firm, 5-second polling (?handler=New), send (?handler=Send).
/// The client id never comes from the request; it is the signed-in Client's own record (404 when there is none).
/// </summary>
public class IndexModel : PageModel
{
    public const string Url = "/Portal/Chat";
    private readonly IChatService _chat;
    private readonly IStringLocalizer<SharedResource> _l;
    public IndexModel(IChatService chat, IStringLocalizer<SharedResource> l) { _chat = chat; _l = l; }

    public ChatThread Thread { get; private set; } = null!;
    public ChatViewModel Chat { get; private set; } = null!;

    private ChatViewModel ViewFor(Guid clientId) =>
        new(clientId, AccessService.UserId(User) ?? Guid.Empty, false, Url + "?handler=New", Url + "?handler=Send");

    public async Task<IActionResult> OnGetAsync()
    {
        var clientId = await _chat.OwnClientIdAsync(User, HttpContext.RequestAborted);
        var thread = clientId is { } id ? await _chat.OpenAsync(User, id, HttpContext.RequestAborted) : null;
        if (thread is null) return NotFound();
        Thread = thread;
        Chat = ViewFor(thread.ClientId);
        return Page();
    }

    /// <summary>visible=1 only while the page is visible (chat.js); background-tab polls fetch but do not mark read.</summary>
    public async Task<IActionResult> OnGetNewAsync(long? after, int? visible)
    {
        var clientId = await _chat.OwnClientIdAsync(User, HttpContext.RequestAborted);
        if (clientId is null) return NotFound();
        var messages = await _chat.NewSinceAsync(User, clientId.Value, after, visible == 1, HttpContext.RequestAborted);
        return messages is null ? NotFound() : ChatResults.NewMessages(this, ViewFor(clientId.Value), messages, after);
    }

    public async Task<IActionResult> OnPostSendAsync(string? body)
    {
        var clientId = await _chat.OwnClientIdAsync(User, HttpContext.RequestAborted);
        if (clientId is null) return NotFound();
        var error = await _chat.SendAsync(User, clientId.Value, body, HttpContext.RequestAborted);
        return ChatResults.Send(this, error, _l, Url);
    }
}
