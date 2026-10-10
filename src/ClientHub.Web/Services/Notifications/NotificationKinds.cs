namespace ClientHub.Web.Services.Notifications;

/// <summary>
/// Notification kinds (R-086..R-094). Each kind has a resx title <c>Notify.&lt;Kind&gt;.Title</c> ({0} = context: case title,
/// client name or bill number). Kinds in <see cref="Emailed"/> also have <c>Email.&lt;Kind&gt;.{Subject,Body}</c>
/// ({0} = recipient name, {1} = login link); those go only to active Client users (R-098, Q-18).
/// </summary>
public static class NotificationKinds
{
    public const string CaseStatusChanged = "CaseStatusChanged";   // R-086
    public const string CaseUpdatePosted = "CaseUpdatePosted";     // R-087
    public const string DocumentRequested = "DocumentRequested";   // R-088
    public const string DocumentRejected = "DocumentRejected";     // R-089
    public const string DocumentUploaded = "DocumentUploaded";     // R-090 (firm, no email)
    public const string FirmMessage = "FirmMessage";               // R-091 (firm user wrote to the client)
    public const string ClientMessage = "ClientMessage";           // R-092, R-093 (firm, no email)
    public const string BillSent = "BillSent";                     // R-094
    public const string BillPaid = "BillPaid";                     // R-094

    public static readonly string[] All =
        { CaseStatusChanged, CaseUpdatePosted, DocumentRequested, DocumentRejected, DocumentUploaded, FirmMessage, ClientMessage, BillSent, BillPaid };

    public static readonly string[] Emailed =
        { CaseStatusChanged, CaseUpdatePosted, DocumentRequested, DocumentRejected, FirmMessage, BillSent, BillPaid };
}
