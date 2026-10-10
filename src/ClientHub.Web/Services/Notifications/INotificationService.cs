using ClientHub.Web.Data.Entities;

namespace ClientHub.Web.Services.Notifications;

/// <summary>
/// The single Notify service. Callers invoke it after their own SaveChanges/commit, never from entities.
/// Firm recipients come from IAccessService; this service only writes rows and emails Client users.
/// </summary>
public interface INotificationService
{
    /// <summary>
    /// Writes one row per distinct user (title in the user's language), saves, then emails active Client recipients when the kind
    /// is emailed. An email failure is logged and never thrown (R-100).
    /// </summary>
    Task NotifyAsync(IEnumerable<Guid> userIds, string kind, string linkUrl, string context, CancellationToken ct = default);

    /// <summary>
    /// Runs post-commit notification work with CancellationToken.None and never throws: the action is already saved, so any failure
    /// (DB, cancellation, recipients lookup) is logged and swallowed, and unsaved notification rows are discarded. Every trigger uses it.
    /// </summary>
    Task AfterCommitAsync(string kind, Func<CancellationToken, Task> work);

    /// <summary>Notifies the login of the case's client (if any) with the case title as context.</summary>
    Task NotifyCaseClientAsync(Guid caseId, string kind, string linkUrl, CancellationToken ct = default);

    Task<int> UnreadCountAsync(Guid userId, CancellationToken ct = default);

    /// <summary>The user's own notifications, newest first.</summary>
    Task<List<Notification>> ListAsync(Guid userId, int skip, int take, CancellationToken ct = default);

    /// <summary>Marks the user's own notification read and returns its link; null when it is not theirs (404).</summary>
    Task<string?> OpenAsync(Guid userId, Guid notificationId, CancellationToken ct = default);

    Task MarkAllReadAsync(Guid userId, CancellationToken ct = default);
}
