using ClientHub.Web.Data.Entities;

namespace ClientHub.Web.Infrastructure;

public enum Tone { Neutral, Info, Success, Warning, Danger }

/// <summary>One mapping from status value to badge tone, used by the portal and the staff area.</summary>
public static class StatusTone
{
    public const string Active = "Active";
    /// <summary>Deactivated login (Clients list, Client page).</summary>
    public const string Inactive = "Inactive";

    public static Tone For(string status) => status switch
    {
        CaseStatus.New or BillStatus.Draft => Tone.Neutral,
        CaseStatus.Filed or CaseStatus.Hearing or BillStatus.Sent => Tone.Info,
        CaseStatus.Completed or RequestStatus.Received or BillStatus.Paid or Active => Tone.Success,
        CaseStatus.DocumentsCollected or RequestStatus.Pending => Tone.Warning,
        CaseStatus.Cancelled or RequestStatus.Rejected => Tone.Danger,
        _ => Tone.Neutral
    };

    /// <summary>Tailwind classes (pill background + text) for a tone.</summary>
    public static string Classes(Tone tone) => tone switch
    {
        Tone.Info => "bg-info-bg text-info",
        Tone.Success => "bg-success-bg text-success",
        Tone.Warning => "bg-warning-bg text-warning",
        Tone.Danger => "bg-danger-bg text-danger",
        _ => "bg-neutral-bg text-neutral"
    };

    public static string DotClasses(Tone tone) => tone switch
    {
        Tone.Info => "bg-info",
        Tone.Success => "bg-success",
        Tone.Warning => "bg-warning",
        Tone.Danger => "bg-danger",
        _ => "bg-neutral"
    };

    /// <summary>Resx key for the status word, identical in both areas (R-157).</summary>
    public static string Key(string status) => "Status." + status;
}
