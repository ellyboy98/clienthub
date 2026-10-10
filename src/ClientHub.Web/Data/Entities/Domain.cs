namespace ClientHub.Web.Data.Entities;

public class Client : Entity
{
    public string ClientType { get; set; } = Entities.ClientType.Person;
    public string Name { get; set; } = "";
    public string? IcOrRegNo { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? ContactPersonName { get; set; }
    public string? ContactPersonPhone { get; set; }
    public Guid? UserId { get; set; }
    public AppUser? User { get; set; }
    public List<Case> Cases { get; set; } = new();
}

public class Case : Entity
{
    public Guid ClientId { get; set; }
    public Client? Client { get; set; }
    public string Title { get; set; } = "";
    public string CaseType { get; set; } = Entities.CaseType.Civil;
    public string Status { get; set; } = CaseStatus.New;
    public Guid LawyerId { get; set; }
    public AppUser? Lawyer { get; set; }
    public string? Description { get; set; }
    public DateTime OpenedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public List<CaseStaff> StaffLinks { get; set; } = new();

    /// <summary>Completed or Cancelled: listed under Past cases.</summary>
    public bool IsClosed => CaseStatus.IsClosed(Status);

    /// <summary>
    /// Any status to any status (R-037, R-038, Q-09). Entering Completed/Cancelled sets closed_at;
    /// moving between the two keeps it; returning to an active step clears it.
    /// </summary>
    public void SetStatus(string status, DateTime now)
    {
        if (!CaseStatus.IsValid(status)) throw new ArgumentException($"Unknown case status '{status}'.", nameof(status));
        var wasClosed = IsClosed;
        Status = status;
        if (!IsClosed) ClosedAt = null;
        else if (!wasClosed || ClosedAt is null) ClosedAt = now;
    }
}

public class CaseStaff : Entity
{
    public Guid CaseId { get; set; }
    public Case? Case { get; set; }
    public Guid UserId { get; set; }
    public AppUser? User { get; set; }
}

public class CaseUpdate : Entity
{
    public Guid CaseId { get; set; }
    public Guid AuthorId { get; set; }
    public string Body { get; set; } = "";
    public DateTime PostedAt { get; set; }
}

public class DocumentRequest : Entity
{
    public Guid CaseId { get; set; }
    public string Name { get; set; } = "";
    public string? Note { get; set; }
    public string Status { get; set; } = RequestStatus.Pending;
    public string? RejectionReason { get; set; }
    public Guid? DocumentId { get; set; }
}

public class Document : Entity
{
    public Guid CaseId { get; set; }
    public Guid UploadedById { get; set; }
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long SizeBytes { get; set; }
    public string StorageKey { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public Guid? RemovedById { get; set; }
    public DateTime? RemovedAt { get; set; }
}

public class Message : Entity
{
    public Guid ClientId { get; set; }
    /// <summary>
    /// Identity, assigned by PostgreSQL at insert. Inserts into one chat hold a per-client advisory lock until commit, so within a
    /// chat seq order is commit order: the poll cursor (seq &gt; after) can never skip a message.
    /// </summary>
    public long Seq { get; set; }
    public Guid SenderId { get; set; }
    public string Body { get; set; } = "";
    public string? AttachmentKey { get; set; }
    public string? AttachmentName { get; set; }
    public string? AttachmentContentType { get; set; }
    public long? AttachmentSizeBytes { get; set; }
    public DateTime SentAt { get; set; }
    public DateTime? ReadAt { get; set; }
}

public class Notification : Entity
{
    public Guid UserId { get; set; }
    public string Kind { get; set; } = "";
    public string Title { get; set; } = "";
    public string LinkUrl { get; set; } = "";
    public DateTime? ReadAt { get; set; }
}

public class FirmSettings : Entity
{
    public string FirmName { get; set; } = "";
    public string? BankName { get; set; }
    public string? BankAccountNo { get; set; }
    public string? BankAccountName { get; set; }
    public string? QrStorageKey { get; set; }
    public string? SmtpHost { get; set; }
    public int? SmtpPort { get; set; }
    public string? SmtpUser { get; set; }
    /// <summary>Stored encrypted with ASP.NET Data Protection (purpose SmtpEmailSender.ProtectorPurpose), never plain text.</summary>
    public string? SmtpPassword { get; set; }
    public string? SmtpFrom { get; set; }
}

public class AuditLog : Entity
{
    public Guid? UserId { get; set; }
    public string Action { get; set; } = "";
    public string Entity { get; set; } = "";
    public Guid? EntityId { get; set; }
    public DateTime At { get; set; }
    public string? Details { get; set; }
}
