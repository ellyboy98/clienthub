using System.Globalization;
using ClientHub.Web.Data;
using ClientHub.Web.Data.Entities;
using ClientHub.Web.Infrastructure;
using ClientHub.Web.Services.Email;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace ClientHub.Web.Services.Notifications;

public class NotificationService : INotificationService
{
    private readonly AppDbContext _db;
    private readonly IEmailSender _email;
    private readonly IAppLinks _links;
    private readonly IStringLocalizer<SharedResource> _l;
    private readonly ILogger<NotificationService> _log;

    public NotificationService(AppDbContext db, IEmailSender email, IAppLinks links, IStringLocalizer<SharedResource> l, ILogger<NotificationService> log)
    {
        _db = db; _email = email; _links = links; _l = l; _log = log;
    }

    public async Task NotifyAsync(IEnumerable<Guid> userIds, string kind, string linkUrl, string context, CancellationToken ct = default)
    {
        if (!NotificationKinds.All.Contains(kind)) throw new ArgumentException($"Unknown notification kind {kind}.", nameof(kind));
        if (!linkUrl.StartsWith('/') || linkUrl.StartsWith("//")) throw new ArgumentException("Link must be a local path.", nameof(linkUrl));
        var ids = userIds.Distinct().ToList();
        if (ids.Count == 0) return;
        var users = await _db.Users.AsNoTracking().Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.Role, u.Language, u.IsActive, u.Email, u.DisplayName }).ToListAsync(ct);
        if (users.Count == 0) return;

        var now = DateTime.UtcNow;
        foreach (var u in users)
            _db.Notifications.Add(new Notification
            {
                UserId = u.Id, Kind = kind, LinkUrl = linkUrl, CreatedAt = now,
                Title = InLanguage(u.Language, () => string.Format(_l[$"Notify.{kind}.Title"].Value, context))
            });
        await _db.SaveChangesAsync(ct);

        // R-098: firm users never get email. Q-18: no email to inactive users.
        if (!NotificationKinds.Emailed.Contains(kind)) return;
        var login = _links.Absolute("Account/Login?ReturnUrl=" + Uri.EscapeDataString(linkUrl));
        foreach (var u in users.Where(u => u.Role == Roles.Client && u.IsActive && !string.IsNullOrWhiteSpace(u.Email)))
        {
            // R-099: only what happened and a login link; no case details, file names or message text.
            var (subject, body) = InLanguage(u.Language, () =>
                (_l[$"Email.{kind}.Subject"].Value, string.Format(_l[$"Email.{kind}.Body"].Value, u.DisplayName, login)));
            try { await _email.SendAsync(u.Email!, subject, body, ct); }
            catch (Exception ex) { _log.LogError(ex, "Notification email {Kind} failed for user {UserId}", kind, u.Id); }
        }
    }

    public async Task AfterCommitAsync(string kind, Func<CancellationToken, Task> work)
    {
        try { await work(CancellationToken.None); }
        catch (Exception ex)
        {
            foreach (var e in _db.ChangeTracker.Entries<Notification>().Where(e => e.State == EntityState.Added).ToList())
                e.State = EntityState.Detached;
            _log.LogError("Notification {Kind} failed after commit ({Error}); the action itself was saved.", kind, ex.GetType().Name);
        }
    }

    public async Task NotifyCaseClientAsync(Guid caseId, string kind, string linkUrl, CancellationToken ct = default)
    {
        var c = await _db.Cases.AsNoTracking().Where(x => x.Id == caseId)
            .Select(x => new { x.Title, x.Client!.UserId }).FirstOrDefaultAsync(ct);
        if (c?.UserId is not { } uid) return;
        await NotifyAsync(new[] { uid }, kind, linkUrl, c.Title, ct);
    }

    public Task<int> UnreadCountAsync(Guid userId, CancellationToken ct = default) =>
        _db.Notifications.CountAsync(n => n.UserId == userId && n.ReadAt == null, ct);

    public Task<List<Notification>> ListAsync(Guid userId, int skip, int take, CancellationToken ct = default) =>
        _db.Notifications.AsNoTracking().Where(n => n.UserId == userId)
            .OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id).Skip(skip).Take(take).ToListAsync(ct);

    public async Task<string?> OpenAsync(Guid userId, Guid notificationId, CancellationToken ct = default)
    {
        var n = await _db.Notifications.FirstOrDefaultAsync(x => x.Id == notificationId && x.UserId == userId, ct);
        if (n is null) return null;
        if (n.ReadAt is null)
        {
            n.ReadAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }
        return n.LinkUrl;
    }

    public Task MarkAllReadAsync(Guid userId, CancellationToken ct = default) =>
        _db.Notifications.Where(n => n.UserId == userId && n.ReadAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, DateTime.UtcNow), ct);

    private static T InLanguage<T>(string language, Func<T> f)
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo(Cultures.Supported.Contains(language) ? language : Cultures.English);
            return f();
        }
        finally { CultureInfo.CurrentUICulture = previous; }
    }
}
