using ClientHub.Web.Infrastructure;
using Microsoft.AspNetCore.Html;

namespace ClientHub.Web.Pages.Shared.Components;

public enum ButtonVariant { Primary, Accent, Secondary, Ghost, Danger }

/// <summary>Href set: renders a link. Otherwise a button (Type submit by default).</summary>
public record ButtonModel(string Text, ButtonVariant Variant = ButtonVariant.Primary, string? Href = null,
    string Type = "submit", bool FullWidth = false, string? Name = null, string? Value = null, string? OnClick = null,
    string? FormAction = null);

public record BadgeModel(string Text, Tone Tone);

/// <summary>Name is the form field name ("Input.Email"); errors are read from ModelState under that name.</summary>
public record InputModel(string Name, string Label, string? Value = null, string Type = "text",
    string? Autocomplete = null, bool Required = false, string? Hint = null, string? InputMode = null, bool Autofocus = false,
    bool ReadOnly = false);

public record CardModel(IHtmlContent Body, string? Title = null);

public record EmptyStateModel(string Text, string? ActionText = null, string? ActionHref = null);

public record TabItem(string Label, string Href, bool Active);

public record TabsModel(IReadOnlyList<TabItem> Items);

/// <summary>Native &lt;dialog&gt; with a POST form. Open it with a button whose onclick is ConfirmDialogModel.OpenScript(Id).</summary>
public record ConfirmDialogModel(string Id, string Title, string Message, string ConfirmText, string Action,
    IReadOnlyDictionary<string, string>? Fields = null, ButtonVariant ConfirmVariant = ButtonVariant.Danger)
{
    public static string OpenScript(string id) => $"document.getElementById('{id}').showModal()";
}

/// <summary>
/// File picker wired by wwwroot/js/upload.js: start → PUT with progress → complete, then the page reloads.
/// Target fields are posted to /Uploads/Start as they are (kind, caseId, requestId); the server builds the key.
/// </summary>
/// <summary>ImagesOnly: JPG/PNG only (Settings DuitNow QR); Hint replaces the default pdf/docx/jpg/png hint.</summary>
public record FileUploadModel(string Id, string Label, IReadOnlyDictionary<string, string> Target, bool Accent = false, bool ShowHint = true,
    bool ImagesOnly = false, string? Hint = null)
{
    public static FileUploadModel ForSettingsQr(string id, string label, string hint) =>
        new(id, label, new Dictionary<string, string> { ["kind"] = "SettingsQr" }, ImagesOnly: true, Hint: hint);

    public static FileUploadModel ForCase(string id, string label, Guid caseId, Guid? requestId = null, bool accent = false, bool showHint = true)
    {
        var target = new Dictionary<string, string> { ["kind"] = "Case", ["caseId"] = caseId.ToString() };
        if (requestId is { } r) target["requestId"] = r.ToString();
        return new(id, label, target, accent, showHint);
    }
}

/// <summary>Case card in the portal (R-151, R-155): title, status badge, type, opened date, last update.</summary>
public record CaseCardModel(string Title, string Status, string CaseType, DateTime OpenedAt, DateTime? LastUpdateAt, string Href);

/// <summary>5-step progress bar plus Cancelled (R-142). Status is the case status value.</summary>
public record CaseProgressModel(string Status);

/// <summary>
/// One chat as seen by the current user (R-146, R-153). FirmSide: the viewer is a firm user, so firm messages sit on the
/// right; for a Client their own messages do. PollUrl/SendUrl already carry the handler; the cursor is appended.
/// </summary>
public record ChatViewModel(Guid ClientId, Guid Me, bool FirmSide, string PollUrl, string SendUrl);

/// <summary>Messages plus the polling sentinel that carries the cursor (seq of the last message shown).</summary>
public record ChatMessagesModel(ChatViewModel Chat, IReadOnlyList<ClientHub.Web.Services.Chat.ChatMessageView> Messages, long AfterSeq)
{
    public static ChatMessagesModel From(ChatViewModel chat, IReadOnlyList<ClientHub.Web.Services.Chat.ChatMessageView> messages, long previousSeq = 0) =>
        new(chat, messages, messages.Count > 0 ? messages[^1].Seq : previousSeq);
}

/// <summary>Mine: on the viewer's side (navy, right). The sender name shows on every message not written by the viewer.</summary>
public record MessageBubbleModel(ClientHub.Web.Services.Chat.ChatMessageView Message, bool Mine, bool ShowName);

/// <summary>Bank details and DuitNow QR from Settings with the bill number as payment reference (R-084, R-147, R-154).</summary>
public record BankDetailsModel(ClientHub.Web.Services.Settings.BankView Bank, string Reference);
