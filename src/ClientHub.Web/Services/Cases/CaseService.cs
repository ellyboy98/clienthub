using ClientHub.Web.Data;
using ClientHub.Web.Data.Entities;
using ClientHub.Web.Services.Audit;
using ClientHub.Web.Services.Notifications;
using Microsoft.EntityFrameworkCore;

namespace ClientHub.Web.Services.Cases;

public record CaseInput(Guid ClientId, string Title, string CaseType, Guid LawyerId, string? Description, IReadOnlyCollection<Guid> StaffIds);

public enum CaseError { None, InvalidLawyer, InvalidStaff, InvalidClient, InvalidType }

/// <summary>
/// Case writes. Callers check access (IAccessService) and role capability first; this service only enforces data rules
/// and writes the audit rows (R-044). Cases are never deleted (R-040).
/// </summary>
public interface ICaseService
{
    Task<(Case? Case, CaseError Error)> CreateAsync(CaseInput input, CancellationToken ct = default);
    Task<CaseError> UpdateDetailsAsync(Case c, string title, string caseType, string? description, Guid? lawyerId, CancellationToken ct = default);
    Task<CaseError> SetStaffAsync(Case c, IReadOnlyCollection<Guid> staffIds, CancellationToken ct = default);
    Task ChangeStatusAsync(Case c, string status, Guid actorId, CancellationToken ct = default);
    Task<CaseUpdate> PostUpdateAsync(Guid caseId, Guid authorId, string body, CancellationToken ct = default);

    /// <summary>Active Lawyers for the lawyer picker (Q-18: deactivated Lawyers are left out).</summary>
    Task<List<AppUser>> LawyerChoicesAsync(CancellationToken ct = default);
    Task<List<AppUser>> StaffChoicesAsync(CancellationToken ct = default);
}

public class CaseService : ICaseService
{
    private readonly AppDbContext _db;
    private readonly IAuditService _audit;
    private readonly INotificationService _notify;

    public CaseService(AppDbContext db, IAuditService audit, INotificationService notify) { _db = db; _audit = audit; _notify = notify; }

    public async Task<(Case? Case, CaseError Error)> CreateAsync(CaseInput input, CancellationToken ct = default)
    {
        if (input.CaseType is not (CaseType.Criminal or CaseType.Civil)) return (null, CaseError.InvalidType);
        if (!await _db.Clients.AnyAsync(c => c.Id == input.ClientId, ct)) return (null, CaseError.InvalidClient);
        if (!await IsActiveLawyer(input.LawyerId, ct)) return (null, CaseError.InvalidLawyer);
        var staff = input.StaffIds.Distinct().ToList();
        if (!await AllActiveStaff(staff, ct)) return (null, CaseError.InvalidStaff);

        var c = new Case
        {
            ClientId = input.ClientId,
            Title = input.Title.Trim(),
            CaseType = input.CaseType,
            LawyerId = input.LawyerId,
            Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim(),
            Status = CaseStatus.New,          // R-035
            OpenedAt = DateTime.UtcNow
        };
        c.StaffLinks.AddRange(staff.Select(id => new CaseStaff { CaseId = c.Id, UserId = id }));
        _db.Cases.Add(c);
        await _db.SaveChangesAsync(ct);
        return (c, CaseError.None);
    }

    public async Task<CaseError> UpdateDetailsAsync(Case c, string title, string caseType, string? description, Guid? lawyerId, CancellationToken ct = default)
    {
        if (caseType is not (CaseType.Criminal or CaseType.Civil)) return CaseError.InvalidType;
        if (lawyerId is { } lid && lid != c.LawyerId)
        {
            if (!await IsActiveLawyer(lid, ct)) return CaseError.InvalidLawyer;
            c.LawyerId = lid;
        }
        c.Title = title.Trim();
        c.CaseType = caseType;
        c.Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        await _db.SaveChangesAsync(ct);
        return CaseError.None;
    }

    public async Task<CaseError> SetStaffAsync(Case c, IReadOnlyCollection<Guid> staffIds, CancellationToken ct = default)
    {
        var wanted = staffIds.Distinct().ToList();
        var current = await _db.CaseStaff.Where(s => s.CaseId == c.Id).ToListAsync(ct);
        var added = wanted.Except(current.Select(s => s.UserId)).ToList();
        // Existing assignments of since-deactivated staff stay valid (Q-18); only new ones must be active Staff.
        if (!await AllActiveStaff(added, ct)) return CaseError.InvalidStaff;
        _db.CaseStaff.RemoveRange(current.Where(s => !wanted.Contains(s.UserId)));
        _db.CaseStaff.AddRange(added.Select(id => new CaseStaff { CaseId = c.Id, UserId = id }));
        await _db.SaveChangesAsync(ct);
        return CaseError.None;
    }

    public async Task ChangeStatusAsync(Case c, string status, Guid actorId, CancellationToken ct = default)
    {
        var from = c.Status;
        if (from == status) return;
        c.SetStatus(status, DateTime.UtcNow);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(AuditActions.CaseStatusChanged, "Case", c.Id, new { from, to = status }, actorId, ct);
        await _notify.AfterCommitAsync(NotificationKinds.CaseStatusChanged, t =>
            _notify.NotifyCaseClientAsync(c.Id, NotificationKinds.CaseStatusChanged, $"/Portal/Cases/Details/{c.Id}", t)); // R-086
    }

    public async Task<CaseUpdate> PostUpdateAsync(Guid caseId, Guid authorId, string body, CancellationToken ct = default)
    {
        var u = new CaseUpdate { CaseId = caseId, AuthorId = authorId, Body = body.Trim(), PostedAt = DateTime.UtcNow };
        _db.CaseUpdates.Add(u);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(AuditActions.CaseUpdatePosted, "CaseUpdate", u.Id, new { caseId }, authorId, ct);
        await _notify.AfterCommitAsync(NotificationKinds.CaseUpdatePosted, t =>
            _notify.NotifyCaseClientAsync(caseId, NotificationKinds.CaseUpdatePosted, $"/Portal/Cases/Details/{caseId}", t)); // R-087
        return u;
    }

    public Task<List<AppUser>> LawyerChoicesAsync(CancellationToken ct = default) =>
        _db.Users.Where(u => u.Role == Roles.Lawyer && u.IsActive).OrderBy(u => u.DisplayName).ToListAsync(ct);

    public Task<List<AppUser>> StaffChoicesAsync(CancellationToken ct = default) =>
        _db.Users.Where(u => u.Role == Roles.Staff && u.IsActive).OrderBy(u => u.DisplayName).ToListAsync(ct);

    private Task<bool> IsActiveLawyer(Guid id, CancellationToken ct) =>
        _db.Users.AnyAsync(u => u.Id == id && u.Role == Roles.Lawyer && u.IsActive, ct);

    private async Task<bool> AllActiveStaff(List<Guid> ids, CancellationToken ct) =>
        ids.Count == 0 || await _db.Users.CountAsync(u => ids.Contains(u.Id) && u.Role == Roles.Staff && u.IsActive, ct) == ids.Count;
}

/// <summary>One row of a case list (dashboard table, Cases, Past cases, portal cards). Member-init so EF can order by its fields.</summary>
public record CaseRow
{
    public Guid Id { get; init; }
    public string Title { get; init; } = "";
    public Guid ClientId { get; init; }
    public string ClientName { get; init; } = "";
    public string CaseType { get; init; } = "";
    public string Status { get; init; } = "";
    public string LawyerName { get; init; } = "";
    public DateTime OpenedAt { get; init; }
    public DateTime? ClosedAt { get; init; }
    public DateTime? LastUpdateAt { get; init; }
}

public static class CaseQueries
{
    /// <summary>Projects an already access-scoped query to rows; last update comes from case_updates in the same SQL.</summary>
    public static IQueryable<CaseRow> ToRows(this IQueryable<Case> q, AppDbContext db) =>
        q.Select(c => new CaseRow
        {
            Id = c.Id, Title = c.Title, ClientId = c.ClientId, ClientName = c.Client!.Name, CaseType = c.CaseType,
            Status = c.Status, LawyerName = c.Lawyer!.DisplayName, OpenedAt = c.OpenedAt, ClosedAt = c.ClosedAt,
            LastUpdateAt = db.CaseUpdates.Where(u => u.CaseId == c.Id).Max(u => (DateTime?)u.PostedAt)
        });

    public static IQueryable<Case> Active(this IQueryable<Case> q) =>
        q.Where(c => c.Status != CaseStatus.Completed && c.Status != CaseStatus.Cancelled);

    public static IQueryable<Case> Past(this IQueryable<Case> q) =>
        q.Where(c => c.Status == CaseStatus.Completed || c.Status == CaseStatus.Cancelled);
}
