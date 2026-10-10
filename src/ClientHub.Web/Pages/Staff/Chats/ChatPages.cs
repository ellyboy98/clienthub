using ClientHub.Web.Pages.Shared.Components;
using ClientHub.Web.Services.Chat;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace ClientHub.Web.Pages.Staff.Chats;

public record ChatListModel(IReadOnlyList<ChatListItem> Items, Guid? ActiveClientId);

/// <summary>Shared by the staff thread and the portal chat: send-result handling for HTMX and plain form posts.</summary>
public static class ChatResults
{
    /// <summary>
    /// HTMX: 200 with an empty body plus HX-Trigger "chat:refresh" on success (the poller fetches at once), or 200 with the
    /// localised error text (swapped into #chat-error). Plain posts redirect back to the chat. Access failures are 404.
    /// </summary>
    public static IActionResult Send(PageModel page, SendError error, IStringLocalizer l, string backUrl)
    {
        if (error == SendError.NotFound) return new NotFoundResult();
        if (page.Request.Headers["HX-Request"] != "true") return new RedirectResult(backUrl);
        if (error == SendError.None)
        {
            page.Response.Headers["HX-Trigger"] = "chat:refresh";
            return new ContentResult { Content = "", ContentType = "text/html" };
        }
        var key = error == SendError.TooLong ? "Chat.Error.TooLong" : "Chat.Error.Empty";
        return new ContentResult { Content = System.Net.WebUtility.HtmlEncode(l[key].Value), ContentType = "text/html" };
    }

    public static IActionResult NewMessages(PageModel page, ChatViewModel chat, IReadOnlyList<ChatMessageView> messages, long? after)
    {
        page.Response.Headers.CacheControl = "no-store";
        return new PartialViewResult
        {
            ViewName = "/Pages/Shared/Components/_ChatMessages.cshtml",
            ViewData = new Microsoft.AspNetCore.Mvc.ViewFeatures.ViewDataDictionary<ChatMessagesModel>(page.ViewData,
                ChatMessagesModel.From(chat, messages, after ?? 0))
        };
    }
}
