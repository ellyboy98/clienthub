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
public record FileUploadModel(string Id, string Label, IReadOnlyDictionary<string, string> Target, bool Accent = false, bool ShowHint = true)
{
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
