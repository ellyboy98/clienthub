namespace ClientHub.Web.Services.Audit;

public static class AuditActions
{
    public const string CaseStatusChanged = "Case.StatusChanged";
    public const string DocumentUploaded = "Document.Uploaded";
    public const string DocumentDownloaded = "Document.Downloaded";
    public const string DocumentRemoved = "Document.Removed";
    public const string BillStatusChanged = "Bill.StatusChanged";
    public const string UserLogin = "User.Login";
    public const string UserLoginFailed = "User.LoginFailed";
    public const string CaseUpdatePosted = "CaseUpdate.Posted";
}

public interface IAuditService
{
    /// <summary>Writes and saves one audit_log row. userId defaults to the signed-in user.</summary>
    Task LogAsync(string action, string entity, Guid? entityId, object? details = null, Guid? userId = null, CancellationToken ct = default);
}
