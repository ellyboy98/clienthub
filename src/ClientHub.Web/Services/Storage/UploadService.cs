using System.Security.Claims;
using ClientHub.Web.Data;
using ClientHub.Web.Data.Entities;
using ClientHub.Web.Services.Access;
using ClientHub.Web.Services.Documents;
using Microsoft.EntityFrameworkCore;

namespace ClientHub.Web.Services.Storage;

public enum UploadError { None, NotFound, BadType, TooLarge, Empty, Missing, Expired }

public record StartUploadResult(UploadError Error, string? Url = null, string? Ticket = null, string? ContentType = null);

public record CompleteUploadResult(UploadError Error, Guid? EntityId = null);

/// <summary>
/// Presigned upload flow for every target (R-057..R-059): start = access check, server-built key, 10-minute presigned PUT,
/// protected ticket; complete = ticket check, access re-check, HeadObject validation, then the target's rows.
/// </summary>
public interface IUploadService
{
    Task<StartUploadResult> StartAsync(ClaimsPrincipal user, UploadTarget target, string? fileName, long size, CancellationToken ct = default);
    Task<CompleteUploadResult> CompleteAsync(ClaimsPrincipal user, string? ticket, CancellationToken ct = default);
}

public class UploadService : IUploadService
{
    public static readonly TimeSpan PutExpiry = TimeSpan.FromMinutes(10);
    private readonly AppDbContext _db;
    private readonly IAccessService _access;
    private readonly IStorageService _storage;
    private readonly IUploadTicketProtector _tickets;
    private readonly IDocumentService _documents;
    private readonly ILogger<UploadService> _log;

    public UploadService(AppDbContext db, IAccessService access, IStorageService storage, IUploadTicketProtector tickets,
        IDocumentService documents, ILogger<UploadService> log)
    {
        _db = db; _access = access; _storage = storage; _tickets = tickets; _documents = documents; _log = log;
    }

    public async Task<StartUploadResult> StartAsync(ClaimsPrincipal user, UploadTarget target, string? fileName, long size, CancellationToken ct = default)
    {
        var uid = AccessService.UserId(user);
        if (uid is null || !await CanUseTargetAsync(user, target, ct)) return new(UploadError.NotFound);
        var name = UploadRules.DisplayName(fileName);
        switch (UploadRules.CheckStart(name, size))
        {
            case UploadProblem.BadType: return new(UploadError.BadType);
            case UploadProblem.TooLarge: return new(UploadError.TooLarge);
        }
        var contentType = UploadRules.ContentTypeFor(name)!;
        var key = target.Kind switch
        {
            UploadTargetKind.Case => StorageKeys.ForCase(target.CaseId!.Value, name),
            _ => throw new InvalidOperationException("Target not supported yet.")
        };
        var staging = StorageKeys.ForStaging();
        var ticket = _tickets.Protect(new UploadTicket(uid.Value, target, staging, key, name, contentType));
        return new(UploadError.None, _storage.PresignPut(staging, contentType, PutExpiry), ticket, contentType);
    }

    public async Task<CompleteUploadResult> CompleteAsync(ClaimsPrincipal user, string? protectedTicket, CancellationToken ct = default)
    {
        var t = _tickets.Unprotect(protectedTicket);
        if (t is null) return new(UploadError.Expired);
        // The ticket belongs to one user and one target; access is checked again because it may have been revoked.
        if (t.UserId != AccessService.UserId(user) || !KeyMatchesTarget(t)) return new(UploadError.NotFound);

        // Completing the same ticket twice (double click, retry) returns the first row instead of writing another.
        var first = await ExistingAsync(user, t, ct);
        if (first is not null) return first;

        if (!await CanUseTargetAsync(user, t.Target, ct)) return new(UploadError.NotFound);

        var staged = await _storage.HeadAsync(t.StagingKey, ct);
        if (staged is null)
            // A concurrent completion of the same ticket may have finished and cleaned up staging already.
            return await ExistingAsync(user, t, ct) ?? new(UploadError.Missing);
        var refused = await RefuseIfInvalidAsync(t, staged, t.StagingKey, ct);
        if (refused is not null) return refused;

        // Server-side copy to the final key, then check the copy itself: the PUT URL stays valid until it expires, so the
        // staging object may have been replaced between the check above and the copy.
        // Staging can disappear between the head and the copy when a concurrent completion of the same ticket finishes
        // (it deletes staging only after saving the row), so a missing source means "look for that row".
        if (!await _storage.CopyAsync(t.StagingKey, t.Key, ct))
            return await ExistingAsync(user, t, ct) ?? new(UploadError.Missing);
        var stored = await _storage.HeadAsync(t.Key, ct);
        if (stored is null) return await ExistingAsync(user, t, ct) ?? new(UploadError.Missing);
        refused = await RefuseIfInvalidAsync(t, stored, t.Key, ct);
        if (refused is not null) { await DeleteStagingAsync(t, ct); return refused; }

        Guid id;
        try
        {
            id = t.Target.Kind switch
            {
                UploadTargetKind.Case => await _documents.RecordUploadAsync(t, stored, ct),
                _ => throw new InvalidOperationException("Target not supported yet.")
            };
        }
        catch (DbUpdateException e) when (e.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation })
        {
            // Two completions of the same ticket raced past the lookup; the other one wrote the row (same key, same object).
            _db.ChangeTracker.Clear();
            return await ExistingAsync(user, t, ct) ?? throw new InvalidOperationException("Unique violation without an existing document.", e);
        }
        await DeleteStagingAsync(t, ct);
        return new(UploadError.None, id);
    }

    /// <summary>The document already written for this ticket's key: its id, 404 if removed or no longer accessible, null if none.</summary>
    private async Task<CompleteUploadResult?> ExistingAsync(ClaimsPrincipal user, UploadTicket t, CancellationToken ct)
    {
        var doc = await _db.Documents.IgnoreQueryFilters().Where(d => d.StorageKey == t.Key)
            .Select(d => new { d.Id, d.IsActive }).FirstOrDefaultAsync(ct);
        if (doc is null) return null;
        return doc.IsActive && await _access.CanAccessCaseAsync(user, t.Target.CaseId!.Value, ct)
            ? new(UploadError.None, doc.Id) : new(UploadError.NotFound);
    }

    /// <summary>R-055: an object that fails validation is deleted (the only deletion besides staging cleanup). Logs ids only, never the file name.</summary>
    private async Task<CompleteUploadResult?> RefuseIfInvalidAsync(UploadTicket t, StoredObject obj, string key, CancellationToken ct)
    {
        var problem = UploadRules.CheckStored(obj, t.ContentType);
        if (problem == UploadProblem.None) return null;
        _log.LogWarning("Upload refused on completion: {Problem}, case {CaseId}, user {UserId}, {Size} bytes", problem, t.Target.CaseId, t.UserId, obj.Size);
        await _storage.DeleteAsync(key, ct);
        return new(problem switch { UploadProblem.TooLarge => UploadError.TooLarge, UploadProblem.Empty => UploadError.Empty, _ => UploadError.BadType });
    }

    /// <summary>Staging objects are not documents; a failed cleanup is left to the bucket lifecycle rule.</summary>
    private async Task DeleteStagingAsync(UploadTicket t, CancellationToken ct)
    {
        try { await _storage.DeleteAsync(t.StagingKey, ct); }
        catch (Exception e) { _log.LogWarning(e, "Could not delete staging object for case {CaseId}", t.Target.CaseId); }
    }

    private static bool KeyMatchesTarget(UploadTicket t) => t.Target.Kind switch
    {
        UploadTargetKind.Case => t.Target.CaseId is { } c && t.Key.StartsWith(StorageKeys.CasePrefix(c), StringComparison.Ordinal)
            && t.StagingKey.StartsWith(StorageKeys.StagingPrefix, StringComparison.Ordinal),
        _ => false
    };

    /// <summary>
    /// Case: anyone who can open the case. Clients only against a Pending or Rejected request of that case (R-046, R-049, Q-11);
    /// firm uploads are plain case documents (no request). Chat (M5) and SettingsQr (M6) are not accepted yet.
    /// </summary>
    private async Task<bool> CanUseTargetAsync(ClaimsPrincipal user, UploadTarget target, CancellationToken ct)
    {
        if (target.Kind != UploadTargetKind.Case || target.CaseId is not { } caseId) return false;
        if (!await _access.CanAccessCaseAsync(user, caseId, ct)) return false;
        if (_access.IsFirm(user)) return target.RequestId is null;
        if (target.RequestId is not { } requestId) return false;
        return await _db.DocumentRequests.AnyAsync(r => r.Id == requestId && r.CaseId == caseId
            && (r.Status == RequestStatus.Pending || r.Status == RequestStatus.Rejected), ct);
    }
}
