using System.Globalization;
using System.Security.Claims;
using ClientHub.Web.Data;
using ClientHub.Web.Data.Entities;
using ClientHub.Web.Infrastructure;
using ClientHub.Web.Services.Access;
using ClientHub.Web.Services.Audit;
using ClientHub.Web.Services.Notifications;
using Microsoft.EntityFrameworkCore;

namespace ClientHub.Web.Services.Bills;

public record BillRow(Guid Id, string BillNo, string Status, decimal Total, DateTime CreatedAt, DateTime? IssuedAt, DateTime? PaidAt,
    Guid CaseId, string CaseTitle, Guid ClientId, string ClientName);

public enum BillError { None, NotFound, NotDraft, Invalid, NoLines, TooLarge }

/// <summary>Line form values as typed; the amount is parsed with the invariant culture ("1,234.50").</summary>
public record LineInput(string? LineType, string? Description, string? Amount)
{
    public static bool TryParseAmount(string? raw, out decimal amount) =>
        decimal.TryParse((raw ?? "").Replace("RM", "", StringComparison.OrdinalIgnoreCase).Trim(),
            NumberStyles.Number, CultureInfo.InvariantCulture, out amount);
}

/// <summary>
/// Bills (R-075..R-084). <see cref="FindAsync"/> and <see cref="ListFor"/> are the access gates: the bill's case must be in
/// CasesFor(user), and a Client never sees a Draft (R-079). Every change runs in a transaction that locks the bill row, so a
/// line edit cannot race a send and the stored total always matches the lines.
/// </summary>
public interface IBillService
{
    /// <summary>Bill with lines, or null when missing or not visible to the user.</summary>
    Task<Bill?> FindAsync(ClaimsPrincipal user, Guid billId, CancellationToken ct = default);

    /// <summary>Bills the user may see, newest first (Clients: Sent and Paid only).</summary>
    IQueryable<BillRow> ListFor(ClaimsPrincipal user, Guid? caseId = null);

    /// <summary>New Draft with its number (creation year, Asia/Kuala_Lumpur). Caller checks case access first.</summary>
    Task<Bill> CreateDraftAsync(Guid caseId, CancellationToken ct = default);

    Task<BillError> AddLineAsync(Guid billId, LineInput input, CancellationToken ct = default);
    Task<BillError> UpdateLineAsync(Guid billId, Guid lineId, LineInput input, CancellationToken ct = default);
    Task<BillError> RemoveLineAsync(Guid billId, Guid lineId, CancellationToken ct = default);
    Task<BillError> SaveNoteAsync(Guid billId, string? note, CancellationToken ct = default);
    Task<BillError> MarkSentAsync(Guid billId, Guid actorId, CancellationToken ct = default);
    Task<BillError> MarkPaidAsync(Guid billId, Guid actorId, CancellationToken ct = default);
}

public class BillService : IBillService
{
    private readonly AppDbContext _db;
    private readonly IAccessService _access;
    private readonly IAuditService _audit;
    private readonly INotificationService _notify;

    public BillService(AppDbContext db, IAccessService access, IAuditService audit, INotificationService notify)
    {
        _db = db; _access = access; _audit = audit; _notify = notify;
    }

    private IQueryable<Bill> Visible(ClaimsPrincipal user)
    {
        var caseIds = _access.CasesFor(user).Select(c => c.Id);
        var bills = _db.Bills.Where(b => caseIds.Contains(b.CaseId));
        return _access.IsFirm(user) ? bills : bills.Where(b => b.Status != BillStatus.Draft);
    }

    public Task<Bill?> FindAsync(ClaimsPrincipal user, Guid billId, CancellationToken ct = default) =>
        Visible(user).AsNoTracking().Include(b => b.Lines.OrderBy(l => l.SortOrder)).FirstOrDefaultAsync(b => b.Id == billId, ct);

    /// <summary>caseId filters before the projection (EF cannot filter on a positional-record projection).</summary>
    public IQueryable<BillRow> ListFor(ClaimsPrincipal user, Guid? caseId = null) =>
        from b in Visible(user).Where(b => caseId == null || b.CaseId == caseId)
        join c in _db.Cases on b.CaseId equals c.Id
        join cl in _db.Clients on c.ClientId equals cl.Id
        orderby b.CreatedAt descending
        select new BillRow(b.Id, b.BillNo, b.Status, b.Total, b.CreatedAt, b.IssuedAt, b.PaidAt, c.Id, c.Title, cl.Id, cl.Name);

    public async Task<Bill> CreateDraftAsync(Guid caseId, CancellationToken ct = default)
    {
        var year = Format.ToLocal(DateTime.UtcNow).Year;
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var bill = new Bill { CaseId = caseId, BillNo = await BillNumberGenerator.NextAsync(_db, year, ct) };
        bill.Recalculate();
        _db.Bills.Add(bill);
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return bill;
    }

    public Task<BillError> AddLineAsync(Guid billId, LineInput input, CancellationToken ct = default) =>
        MutateAsync(billId, bill =>
        {
            if (!bill.IsDraft) return BillError.NotDraft;
            if (!TryLine(input, out var type, out var desc, out var amount)) return BillError.Invalid;
            if (bill.Lines.Sum(l => l.Amount) + Bill.RoundAmount(amount) > Bill.MaxAmount) return BillError.TooLarge;
            // Explicit Add: entity ids are set client side, so EF would treat a line found only through the navigation as existing.
            _db.BillLines.Add(bill.AddLine(type, desc, amount));
            return BillError.None;
        }, ct);

    public Task<BillError> UpdateLineAsync(Guid billId, Guid lineId, LineInput input, CancellationToken ct = default) =>
        MutateAsync(billId, bill =>
        {
            if (!bill.IsDraft) return BillError.NotDraft;
            if (bill.Lines.All(l => l.Id != lineId)) return BillError.NotFound;
            if (!TryLine(input, out var type, out var desc, out var amount)) return BillError.Invalid;
            if (bill.Lines.Where(l => l.Id != lineId).Sum(l => l.Amount) + Bill.RoundAmount(amount) > Bill.MaxAmount) return BillError.TooLarge;
            bill.UpdateLine(lineId, type, desc, amount);
            return BillError.None;
        }, ct);

    public Task<BillError> RemoveLineAsync(Guid billId, Guid lineId, CancellationToken ct = default) =>
        MutateAsync(billId, bill =>
        {
            if (!bill.IsDraft) return BillError.NotDraft;
            var line = bill.Lines.FirstOrDefault(l => l.Id == lineId);
            if (line is null) return BillError.NotFound;
            _db.BillLines.Remove(line);
            bill.RemoveLine(lineId);
            return BillError.None;
        }, ct);

    public Task<BillError> SaveNoteAsync(Guid billId, string? note, CancellationToken ct = default) =>
        MutateAsync(billId, bill =>
        {
            if (!bill.IsDraft) return BillError.NotDraft;
            if ((note?.Trim().Length ?? 0) > Bill.NoteMax) return BillError.Invalid;
            bill.SetNote(note);
            return BillError.None;
        }, ct);

    public Task<BillError> MarkSentAsync(Guid billId, Guid actorId, CancellationToken ct = default) =>
        ChangeStatusAsync(billId, actorId, BillStatus.Sent, ct);

    public Task<BillError> MarkPaidAsync(Guid billId, Guid actorId, CancellationToken ct = default) =>
        ChangeStatusAsync(billId, actorId, BillStatus.Paid, ct);

    private async Task<BillError> ChangeStatusAsync(Guid billId, Guid actorId, string to, CancellationToken ct)
    {
        string from = "";
        var result = await MutateAsync(billId, bill =>
        {
            from = bill.Status;
            if (to == BillStatus.Sent)
            {
                if (!bill.IsDraft) return BillError.NotDraft;
                if (bill.Lines.Count == 0) return BillError.NoLines;
                bill.MarkSent(DateTime.UtcNow);
            }
            else
            {
                if (bill.Status != BillStatus.Sent) return BillError.Invalid;
                bill.MarkPaid(DateTime.UtcNow);
            }
            return BillError.None;
        }, ct, async bill => await _audit.LogAsync(AuditActions.BillStatusChanged, "Bill", bill.Id, new { bill.BillNo, from, to }, actorId, ct));
        if (result == BillError.None)
        {
            // R-094, after commit: the case's client, with the bill number as context.
            await _notify.AfterCommitAsync(to, async t =>
            {
                var bill = await _db.Bills.AsNoTracking().Where(b => b.Id == billId)
                    .Join(_db.Cases, b => b.CaseId, c => c.Id, (b, c) => new { b.BillNo, c.Client!.UserId }).FirstAsync(t);
                if (bill.UserId is { } uid)
                    await _notify.NotifyAsync(new[] { uid }, to == BillStatus.Sent ? NotificationKinds.BillSent : NotificationKinds.BillPaid,
                        $"/Portal/Bills/Details/{billId}", bill.BillNo, t);
            });
        }
        return result;
    }

    /// <summary>
    /// Locks the bill row (SELECT ... FOR UPDATE), loads it with its lines, applies the change, saves, writes the audit row
    /// (status changes) in the same transaction and commits. Changes that return an error are not saved.
    /// </summary>
    private async Task<BillError> MutateAsync(Guid billId, Func<Bill, BillError> change, CancellationToken ct, Func<Bill, Task>? afterSave = null)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        await _db.Database.ExecuteSqlAsync($"SELECT 1 FROM bills WHERE id = {billId} FOR UPDATE", ct);
        var bill = await _db.Bills.Include(b => b.Lines).FirstOrDefaultAsync(b => b.Id == billId, ct);
        if (bill is null) return BillError.NotFound;
        var error = change(bill);
        if (error != BillError.None) { _db.ChangeTracker.Clear(); return error; }
        await _db.SaveChangesAsync(ct);
        if (afterSave is not null) await afterSave(bill);
        await tx.CommitAsync(ct);
        return BillError.None;
    }

    private static bool TryLine(LineInput input, out string type, out string description, out decimal amount)
    {
        type = input.LineType ?? "";
        description = input.Description?.Trim() ?? "";
        amount = 0;
        return Bill.IsValidLineType(type)
            && description.Length is > 0 and <= Bill.DescriptionMax
            && LineInput.TryParseAmount(input.Amount, out amount)
            && Bill.IsValidAmount(amount);
    }
}
