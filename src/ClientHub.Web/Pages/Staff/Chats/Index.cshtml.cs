using ClientHub.Web.Services.Chat;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ClientHub.Web.Pages.Staff.Chats;

/// <summary>Staff Chats (R-146): clients the user can open (ClientsFor), unread pills; the thread opens at /Staff/Chats/{clientId}.</summary>
public class IndexModel : PageModel
{
    private readonly IChatService _chat;
    public IndexModel(IChatService chat) => _chat = chat;

    public List<ChatListItem> Items { get; private set; } = new();

    public async Task OnGetAsync() => Items = await _chat.ListAsync(User, HttpContext.RequestAborted);
}
