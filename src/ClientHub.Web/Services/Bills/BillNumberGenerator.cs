using ClientHub.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace ClientHub.Web.Services.Bills;

/// <summary>
/// BILL-&lt;year&gt;-&lt;4-digit seq&gt; (R-076, Q-06). Must run inside the transaction that inserts the bill: it takes a per-year
/// advisory transaction lock, so concurrent creators wait and each sees the previous number. The unique index on bill_no
/// is the backstop.
/// </summary>
public static class BillNumberGenerator
{
    /// <summary>Advisory lock namespace ("BILL" in ASCII) plus the year.</summary>
    private const long LockBase = 0x42494C4C_00000000L;

    public static string Prefix(int year) => $"BILL-{year}-";

    public static string Format(int year, int seq) => $"{Prefix(year)}{seq:D4}";

    /// <summary>Sequence part of a bill number of that year, or 0.</summary>
    public static int SeqOf(string billNo, int year) =>
        billNo.StartsWith(Prefix(year), StringComparison.Ordinal) && int.TryParse(billNo[Prefix(year).Length..], out var n) ? n : 0;

    public static async Task<string> NextAsync(AppDbContext db, int year, CancellationToken ct = default)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Bill numbers are generated inside a transaction.");
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({LockBase + year})", ct);
        var prefix = Prefix(year);
        // Longest first, then lexical: stays correct past 9999.
        var last = await db.Bills.Where(b => b.BillNo.StartsWith(prefix))
            .OrderByDescending(b => b.BillNo.Length).ThenByDescending(b => b.BillNo)
            .Select(b => b.BillNo).FirstOrDefaultAsync(ct);
        return Format(year, (last is null ? 0 : SeqOf(last, year)) + 1);
    }
}
