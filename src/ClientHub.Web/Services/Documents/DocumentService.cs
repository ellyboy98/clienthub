using System.Security.Claims;
using ClientHub.Web.Data;
using ClientHub.Web.Data.Entities;
using ClientHub.Web.Services.Access;
using ClientHub.Web.Services.Audit;
using ClientHub.Web.Services.Storage;
using Microsoft.EntityFrameworkCore;

namespace ClientHub.Web.Services.Documents;

public record RequestRow(Guid Id, string Name, string? Note, string Status, string? RejectionReason, Guid? DocumentId, bool DocumentActive, DateTime CreatedAt);

public record DocumentRow(Guid Id, string FileName, string ContentType, long SizeBytes, DateTime UploadedAt, string UploadedBy);

/// <summary>Document requests and case documents (R-045..R-056, R-060, R-062). Callers check case access first.</summary>
public interface IDocumentService
{
    Task<List<RequestRow>> RequestsAsync(Guid caseId, bool openOnly = false, CancellationToken ct = default);
    Task<List<DocumentRow>> DocumentsAsync(Guid caseId, CancellationToken ct = default);
    Task<Guid> AddRequestAsync(Guid caseId, string name, string? note, CancellationToken ct = default);
    /// <summary>Received requests only; reason required. False when the request is not on the case or not Received.</summary>
    Task<bool> RejectAsync(Guid caseId, Guid requestId, string reason, CancellationToken ct = default);
    /// <summary>Sets is_active = false and writes an audit row; the object stays. False when not found or already removed.</summary>
    Task<bool> RemoveAsync(Guid caseId, Guid documentId, Guid userId, CancellationToken ct = default);
    /// <summary>Presigned 5-minute GET after the access check, with an audit row; null = 404 (missing, removed or no access).</summary>
    Task<string?> DownloadUrlAsync(ClaimsPrincipal user, Guid documentId, CancellationToken ct = default);
    /// <summary>Called by the upload completion step after validation: documents row, request link, audit row.</summary>
    Task<Guid> RecordUploadAsync(UploadTicket ticket, StoredObject stored, CancellationToken ct = default);
}

public class DocumentService : IDocumentService
{
    public const int NameMax = 200, NoteMax = 1000, ReasonMax = 1000;
    public static readonly TimeSpan DownloadExpiry = TimeSpan.FromMinutes(5);
    private readonly AppDbContext _db;
    private readonly IAccessService _access;
    private readonly IAuditService _audit;
    private readonly IStorageService _storage;

    public DocumentService(AppDbContext db, IAccessService access, IAuditService audit, IStorageService storage)
    {
        _db = db; _access = access; _audit = audit; _storage = storage;
    }

    public Task<List<RequestRow>> RequestsAsync(Guid caseId, bool openOnly = false, CancellationToken ct = default) =>
        _db.DocumentRequests
            .Where(r => r.CaseId == caseId && (!openOnly || r.Status == RequestStatus.Pending || r.Status == RequestStatus.Rejected))
            .OrderBy(r => r.CreatedAt)
            .Select(r => new RequestRow(r.Id, r.Name, r.Note, r.Status, r.RejectionReason, r.DocumentId,
                r.DocumentId != null && _db.Documents.Any(d => d.Id == r.DocumentId), r.CreatedAt))
            .ToListAsync(ct);

    public Task<List<DocumentRow>> DocumentsAsync(Guid caseId, CancellationToken ct = default) =>
        _db.Documents.Where(d => d.CaseId == caseId) // global filter hides removed documents
            .OrderByDescending(d => d.CreatedAt)
            .Join(_db.Users, d => d.UploadedById, u => u.Id,
                (d, u) => new DocumentRow(d.Id, d.FileName, d.ContentType, d.SizeBytes, d.CreatedAt, u.DisplayName))
            .ToListAsync(ct);

    public async Task<Guid> AddRequestAsync(Guid caseId, string name, string? note, CancellationToken ct = default)
    {
        var r = new DocumentRequest { CaseId = caseId, Name = name.Trim(), Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim() };
        _db.DocumentRequests.Add(r);
        await _db.SaveChangesAsync(ct);
        // NOTIFY: DocumentRequested R-088 (case's client, in-system + email) — M7
        return r.Id;
    }

    public async Task<bool> RejectAsync(Guid caseId, Guid requestId, string reason, CancellationToken ct = default)
    {
        var r = await _db.DocumentRequests.FirstOrDefaultAsync(x => x.Id == requestId && x.CaseId == caseId, ct);
        if (r is null || r.Status != RequestStatus.Received || string.IsNullOrWhiteSpace(reason)) return false;
        r.Status = RequestStatus.Rejected;
        r.RejectionReason = reason.Trim();
        await _db.SaveChangesAsync(ct);
        // NOTIFY: DocumentRejected R-089 (case's client, in-system + email) — M7
        return true;
    }

    public async Task<bool> RemoveAsync(Guid caseId, Guid documentId, Guid userId, CancellationToken ct = default)
    {
        var d = await _db.Documents.FirstOrDefaultAsync(x => x.Id == documentId && x.CaseId == caseId, ct);
        if (d is null) return false;
        d.IsActive = false;
        d.RemovedById = userId;
        d.RemovedAt = DateTime.UtcNow;
        // Q-13: a linked Received request is left as it is. The audit save also saves the document.
        await _audit.LogAsync(AuditActions.DocumentRemoved, nameof(Document), d.Id, new { d.CaseId, d.FileName }, userId, ct);
        return true;
    }

    public async Task<string?> DownloadUrlAsync(ClaimsPrincipal user, Guid documentId, CancellationToken ct = default)
    {
        var d = await _db.Documents.FirstOrDefaultAsync(x => x.Id == documentId, ct);
        if (d is null || !await _access.CanAccessCaseAsync(user, d.CaseId, ct)) return null;
        await _audit.LogAsync(AuditActions.DocumentDownloaded, nameof(Document), d.Id, new { d.CaseId, d.FileName }, ct: ct);
        return _storage.PresignGet(d.StorageKey, d.FileName, DownloadExpiry);
    }

    public async Task<Guid> RecordUploadAsync(UploadTicket t, StoredObject stored, CancellationToken ct = default)
    {
        var caseId = t.Target.CaseId!.Value;
        var doc = new Document
        {
            CaseId = caseId, UploadedById = t.UserId, FileName = t.FileName, ContentType = t.ContentType,
            SizeBytes = stored.Size, StorageKey = t.Key
        };
        _db.Documents.Add(doc);
        if (t.Target.RequestId is { } requestId)
        {
            // R-047, Q-12: the request now points at the new file; an earlier file stays active.
            var r = await _db.DocumentRequests.FirstAsync(x => x.Id == requestId && x.CaseId == caseId, ct);
            r.Status = RequestStatus.Received;
            r.DocumentId = doc.Id;
        }
        await _audit.LogAsync(AuditActions.DocumentUploaded, nameof(Document), doc.Id,
            new { CaseId = caseId, t.Target.RequestId, doc.FileName, doc.SizeBytes }, t.UserId, ct);
        // NOTIFY: DocumentUploaded R-090 (client uploads only: case lawyer + assigned Staff, in-system, no email) — M7
        return doc.Id;
    }
}
