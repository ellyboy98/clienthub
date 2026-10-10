namespace ClientHub.Web.Services.Storage;

public static class UploadTargetKind
{
    public const string Case = "Case", Chat = "Chat", SettingsQr = "SettingsQr";
}

/// <summary>Where an upload goes: Case(caseId, requestId?), Chat(clientId) (M5) or SettingsQr (M6).</summary>
public record UploadTarget(string Kind, Guid? CaseId = null, Guid? RequestId = null, Guid? ClientId = null)
{
    public static UploadTarget Case(Guid caseId, Guid? requestId = null) => new(UploadTargetKind.Case, caseId, requestId);
    public static UploadTarget Chat(Guid clientId) => new(UploadTargetKind.Chat, ClientId: clientId);
    public static UploadTarget SettingsQr() => new(UploadTargetKind.SettingsQr);
}

/// <summary>
/// Issued by the start step and carried by the browser to the completion step, protected with Data Protection.
/// Binds the upload to one user, one target and two server-built keys: the browser PUTs to StagingKey (an unguessable
/// <c>staging/</c> key without the file name); on completion the server copies the checked object to Key, which the browser
/// never sees (the ticket is encrypted). Re-using the PUT URL afterwards only touches staging.
/// </summary>
public record UploadTicket(Guid UserId, UploadTarget Target, string StagingKey, string Key, string FileName, string ContentType);
