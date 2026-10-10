using System.Security.Claims;
using ClientHub.Web.Data;
using ClientHub.Web.Data.Entities;
using ClientHub.Web.Services.Access;
using ClientHub.Web.Services.Audit;
using ClientHub.Web.Services.Storage;
using Microsoft.EntityFrameworkCore;

namespace ClientHub.Web.Services.Chat;

/// <summary>One bubble. FromClient = sent by the client's own login (the "client side" of the chat).</summary>
public record ChatMessageView(Guid Id, long Seq, string SenderName, Guid SenderId, bool FromClient, string Body, DateTime SentAt,
    string? AttachmentName, long? AttachmentSizeBytes)
{
    public bool HasFile => AttachmentName is not null;
}

/// <summary>Staff chat list row: accessible client, unread client messages, last message time.</summary>
public class ChatListItem
{
    public Guid ClientId { get; init; }
    public string ClientName { get; init; } = "";
    public int Unread { get; init; }
    public DateTime? LastAt { get; init; }
}

public record ChatThread(Guid ClientId, string ClientName, IReadOnlyList<ChatMessageView> Messages);

public enum SendError { None, NotFound, Empty, TooLong }

/// <summary>
/// One chat per client (R-063..R-069, R-073, R-074). Every method that takes a client or message id checks
/// <see cref="IAccessService.CanAccessClientAsync"/> first and returns null / NotFound on failure.
/// Messages are never edited or deleted (Q-21). Read state per Q-05: one shared read_at; opening a chat, or polling it while
/// the page is visible, marks the other side's messages read, but only up to the highest seq shown to that viewer.
/// </summary>
public interface IChatService
{
    /// <summary>The Client user's own client id, or null.</summary>
    Task<Guid?> OwnClientIdAsync(ClaimsPrincipal user, CancellationToken ct = default);
    Task<List<ChatListItem>> ListAsync(ClaimsPrincipal user, CancellationToken ct = default);
    /// <summary>Firm: chats with unread client messages. Client: unread firm messages in their chat.</summary>
    Task<int> UnreadAsync(ClaimsPrincipal user, CancellationToken ct = default);
    /// <summary>All messages, oldest first, and marks the other side read. Null when the user cannot open the client.</summary>
    Task<ChatThread?> OpenAsync(ClaimsPrincipal user, Guid clientId, CancellationToken ct = default);
    /// <summary>
    /// Messages with seq &gt; afterSeq, in seq order. When markRead (page visible), the other side's messages up to the highest seq
    /// the viewer now has (returned or the cursor) are marked read. Null on no access.
    /// </summary>
    Task<IReadOnlyList<ChatMessageView>?> NewSinceAsync(ClaimsPrincipal user, Guid clientId, long? afterSeq, bool markRead, CancellationToken ct = default);
    Task<SendError> SendAsync(ClaimsPrincipal user, Guid clientId, string? body, CancellationToken ct = default);
    /// <summary>Upload completion for a Chat target: writes the file message (with the optional caption as its text) and the audit row.</summary>
    Task<Guid> RecordUploadAsync(UploadTicket ticket, StoredObject stored, string? caption, CancellationToken ct = default);
    /// <summary>5-minute presigned GET for a chat file plus an audit row (R-074); null when missing or not accessible.</summary>
    Task<string?> DownloadUrlAsync(ClaimsPrincipal user, Guid messageId, CancellationToken ct = default);
}

public class ChatService : IChatService
{
    public const int MaxBodyLength = 4000;

    /// <summary>Caption check for a file message (R-067 "text and/or a file"): blank is fine, over the limit is TooLong.</summary>
    public static SendError CheckCaption(string? caption) => (caption ?? "").Trim().Length > MaxBodyLength ? SendError.TooLong : SendError.None;
    private readonly AppDbContext _db;
    private readonly IAccessService _access;
    private readonly IStorageService _storage;
    private readonly IAuditService _audit;

    public ChatService(AppDbContext db, IAccessService access, IStorageService storage, IAuditService audit)
    {
        _db = db; _access = access; _storage = storage; _audit = audit;
    }

    public async Task<Guid?> OwnClientIdAsync(ClaimsPrincipal user, CancellationToken ct = default)
    {
        if (AccessService.Role(user) != Roles.Client) return null;
        var ids = await _access.ClientsFor(user).Select(c => c.Id).Take(1).ToListAsync(ct);
        return ids.Count == 0 ? null : ids[0];
    }

    public Task<List<ChatListItem>> ListAsync(ClaimsPrincipal user, CancellationToken ct = default) =>
        _access.ClientsFor(user)
            .Select(cl => new ChatListItem
            {
                ClientId = cl.Id,
                ClientName = cl.Name,
                Unread = _db.Messages.Count(m => m.ClientId == cl.Id && m.ReadAt == null && m.SenderId == cl.UserId),
                LastAt = _db.Messages.Where(m => m.ClientId == cl.Id).Max(m => (DateTime?)m.SentAt)
            })
            .OrderByDescending(x => x.Unread > 0).ThenByDescending(x => x.LastAt).ThenBy(x => x.ClientName)
            .ToListAsync(ct);

    public async Task<int> UnreadAsync(ClaimsPrincipal user, CancellationToken ct = default)
    {
        var me = AccessService.UserId(user);
        if (me is null) return 0;
        if (_access.IsFirm(user))
            return await _access.ClientsFor(user).CountAsync(cl =>
                _db.Messages.Any(m => m.ClientId == cl.Id && m.ReadAt == null && m.SenderId == cl.UserId), ct);
        var clientIds = _access.ClientsFor(user).Select(c => c.Id);
        return await _db.Messages.CountAsync(m => clientIds.Contains(m.ClientId) && m.ReadAt == null && m.SenderId != me.Value, ct);
    }

    public async Task<ChatThread?> OpenAsync(ClaimsPrincipal user, Guid clientId, CancellationToken ct = default)
    {
        var client = await _access.ClientsFor(user).Where(c => c.Id == clientId)
            .Select(c => new { c.Id, c.Name, c.UserId }).FirstOrDefaultAsync(ct);
        if (client is null) return null;
        var messages = await Views(client.Id, client.UserId).ToListAsync(ct);
        if (messages.Count > 0) await MarkReadAsync(user, client.Id, client.UserId, messages[^1].Seq, ct);
        return new ChatThread(client.Id, client.Name, messages);
    }

    public async Task<IReadOnlyList<ChatMessageView>?> NewSinceAsync(ClaimsPrincipal user, Guid clientId, long? afterSeq, bool markRead, CancellationToken ct = default)
    {
        var client = await _access.ClientsFor(user).Where(c => c.Id == clientId)
            .Select(c => new { c.Id, c.UserId }).FirstOrDefaultAsync(ct);
        if (client is null) return null;
        var after = afterSeq is > 0 ? afterSeq.Value : 0;
        var messages = await Views(client.Id, client.UserId, after).ToListAsync(ct);
        // Never past what this viewer has on screen: a message committed after this query has a higher seq and stays unread.
        var shownUpTo = messages.Count > 0 ? messages[^1].Seq : after;
        if (markRead && shownUpTo > 0) await MarkReadAsync(user, client.Id, client.UserId, shownUpTo, ct);
        return messages;
    }

    public async Task<SendError> SendAsync(ClaimsPrincipal user, Guid clientId, string? body, CancellationToken ct = default)
    {
        var me = AccessService.UserId(user);
        if (me is null || !await _access.CanAccessClientAsync(user, clientId, ct)) return SendError.NotFound;
        var text = (body ?? "").Trim();
        if (text.Length == 0) return SendError.Empty;
        if (text.Length > MaxBodyLength) return SendError.TooLong;
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        await LockChatAsync(clientId, ct);
        _db.Messages.Add(new Message { ClientId = clientId, SenderId = me.Value, Body = text, SentAt = DateTime.UtcNow });
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        // NOTIFY: ChatMessage R-091/R-092 (client sender: FirmRecipientsForClientMessageAsync, in-system only; firm sender: the client login, in-system + email) — M7
        return SendError.None;
    }

    public async Task<Guid> RecordUploadAsync(UploadTicket t, StoredObject stored, string? caption, CancellationToken ct = default)
    {
        var clientId = t.Target.ClientId!.Value;
        var text = (caption ?? "").Trim();
        if (text.Length > MaxBodyLength) throw new ArgumentException("Caption too long; check with IChatService.CheckCaption first.", nameof(caption));
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        await LockChatAsync(clientId, ct);
        var m = new Message
        {
            ClientId = clientId, SenderId = t.UserId, Body = text, SentAt = DateTime.UtcNow,
            AttachmentKey = t.Key, AttachmentName = t.FileName, AttachmentContentType = t.ContentType, AttachmentSizeBytes = stored.Size
        };
        _db.Messages.Add(m);
        // The audit service saves, which also saves the message.
        await _audit.LogAsync(AuditActions.MessageFileUploaded, nameof(Message), m.Id,
            new { ClientId = clientId, FileName = t.FileName, SizeBytes = stored.Size }, t.UserId, ct);
        await tx.CommitAsync(ct);
        // NOTIFY: ChatMessage R-091/R-092 (file message; same recipients as a text message) — M7
        return m.Id;
    }

    public async Task<string?> DownloadUrlAsync(ClaimsPrincipal user, Guid messageId, CancellationToken ct = default)
    {
        var m = await _db.Messages.Where(x => x.Id == messageId && x.AttachmentKey != null && x.AttachmentName != null)
            .Select(x => new { x.Id, x.ClientId, x.AttachmentKey, x.AttachmentName }).FirstOrDefaultAsync(ct);
        if (m is null || !await _access.CanAccessClientAsync(user, m.ClientId, ct)) return null;
        await _audit.LogAsync(AuditActions.MessageFileDownloaded, nameof(Message), m.Id, new { m.ClientId, FileName = m.AttachmentName }, ct: ct);
        return _storage.PresignGet(m.AttachmentKey!, m.AttachmentName!, Documents.DocumentService.DownloadExpiry);
    }

    /// <summary>
    /// Serialises message inserts per chat until commit (pg_advisory_xact_lock), so seq order equals commit order within a chat.
    /// Without it, identity values are handed out at insert, not commit, and a poll could move past a still-uncommitted lower seq.
    /// </summary>
    private Task LockChatAsync(Guid clientId, CancellationToken ct)
    {
        var key = BitConverter.ToInt64(clientId.ToByteArray(), 0);
        return _db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({key})", ct);
    }

    /// <summary>
    /// Q-05: a firm user marks the client's messages read; the client marks the firm's messages read. Only messages with
    /// seq &lt;= upToSeq (the highest seq shown to this viewer).
    /// </summary>
    private async Task MarkReadAsync(ClaimsPrincipal user, Guid clientId, Guid? clientUserId, long upToSeq, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var unread = _db.Messages.Where(m => m.ClientId == clientId && m.ReadAt == null && m.Seq <= upToSeq);
        unread = _access.IsFirm(user)
            ? unread.Where(m => m.SenderId == clientUserId)
            : unread.Where(m => m.SenderId != clientUserId);
        await unread.ExecuteUpdateAsync(s => s.SetProperty(m => m.ReadAt, now).SetProperty(m => m.UpdatedAt, now), ct);
    }

    /// <summary>Filters and orders before projecting (EF cannot filter on a positional-record projection).</summary>
    private IQueryable<ChatMessageView> Views(Guid clientId, Guid? clientUserId, long afterSeq = 0) =>
        from m in _db.Messages
        join u in _db.Users on m.SenderId equals u.Id
        where m.ClientId == clientId && m.Seq > afterSeq
        orderby m.Seq
        select new ChatMessageView(m.Id, m.Seq, u.DisplayName, m.SenderId, m.SenderId == clientUserId, m.Body, m.SentAt,
            m.AttachmentName, m.AttachmentSizeBytes);
}
