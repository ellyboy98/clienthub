using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using ClientHub.Tests.Support;
using ClientHub.Web.Data.Entities;
using ClientHub.Web.Infrastructure;
using ClientHub.Web.Services.Audit;
using ClientHub.Web.Services.Bills;
using Microsoft.EntityFrameworkCore;

namespace ClientHub.Tests.Bills;

/// <summary>Bill rules (R-075..R-083): totals only in Recalculate, frozen lines once sent, numbering, audit.</summary>
public class BillTests : IClassFixture<FirmAppFactory>
{
    private readonly FirmAppFactory _app;
    public BillTests(FirmAppFactory app) => _app = app;

    private static readonly DateTime Now = new(2026, 10, 10, 2, 0, 0, DateTimeKind.Utc);

    private static Bill DraftWith(params decimal[] amounts)
    {
        var b = new Bill { BillNo = "BILL-2026-9999" };
        foreach (var a in amounts) b.AddLine(LineType.Fee, "Line", a);
        return b;
    }

    // ---- entity rules (no database) ----

    [Fact]
    public void Recalculate_SumsLines()
    {
        var b = new Bill();
        b.AddLine(LineType.Fee, "Legal fee", 1500m);
        b.AddLine(LineType.Expense, "Court filing", 250.25m);
        Assert.Equal(1750.25m, b.Total);
        // Recalculate is the only place the total comes from: a stale value is replaced.
        b.Total = 1m;
        b.Recalculate();
        Assert.Equal(1750.25m, b.Total);
    }

    [Fact]
    public void Recalculate_NoLines_Zero()
    {
        var b = new Bill { Total = 42m };
        b.Recalculate();
        Assert.Equal(0m, b.Total);
        b.AddLine(LineType.Fee, "Fee", 10m);
        b.RemoveLine(b.Lines[0].Id);
        Assert.Equal(0m, b.Total);
    }

    [Fact]
    public void Total_TwoDecimals()
    {
        // Rule: each line is rounded to 2 decimals half away from zero; the total is the exact sum of the rounded lines.
        var b = DraftWith(10.005m, 2.665m, 0.1m, 0.2m);
        Assert.Equal(new[] { 10.01m, 2.67m, 0.10m, 0.20m }, b.Lines.Select(l => l.Amount));
        Assert.Equal(12.98m, b.Total);
        Assert.Equal(b.Lines.Sum(l => l.Amount), b.Total);
        Assert.Equal("RM 1,234.50", Format.Rm(DraftWith(1234.5m).Total));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Bill().AddLine(LineType.Fee, "Zero", 0.004m));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Bill().AddLine(LineType.Fee, "Negative", -5m));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Bill().AddLine(LineType.Fee, "Huge", 10_000_000_000m));
        var big = DraftWith(Bill.MaxAmount);
        Assert.Throws<BillRuleException>(() => big.AddLine(LineType.Fee, "Over", 0.01m));
        Assert.Equal(Bill.MaxAmount, big.Total);
    }

    [Fact]
    public void AddLine_WhenSent_Throws()
    {
        var b = DraftWith(100m);
        b.MarkSent(Now);
        var line = b.Lines[0].Id;
        Assert.Throws<BillRuleException>(() => b.AddLine(LineType.Fee, "Late", 5m));
        Assert.Throws<BillRuleException>(() => b.UpdateLine(line, LineType.Fee, "Changed", 1m));
        Assert.Throws<BillRuleException>(() => b.RemoveLine(line));
        Assert.Throws<BillRuleException>(() => b.SetNote("changed"));
        b.MarkPaid(Now);
        Assert.Throws<BillRuleException>(() => b.AddLine(LineType.Fee, "Late", 5m));
        Assert.Equal(100m, b.Total);
        Assert.Single(b.Lines);
    }

    [Fact]
    public void MarkSent_SetsIssuedAt()
    {
        var b = DraftWith(100m);
        Assert.Null(b.IssuedAt);
        b.MarkSent(Now);
        Assert.Equal(BillStatus.Sent, b.Status);
        Assert.Equal(Now, b.IssuedAt);
        Assert.Throws<BillRuleException>(() => b.MarkSent(Now));
    }

    [Fact]
    public void MarkSent_ZeroLines_Rejected()
    {
        var b = new Bill();
        Assert.Throws<BillRuleException>(() => b.MarkSent(Now));
        Assert.Equal(BillStatus.Draft, b.Status);
        Assert.Null(b.IssuedAt);
    }

    [Fact]
    public void MarkPaid_FromSent_SetsPaidAt()
    {
        var b = DraftWith(100m);
        Assert.Throws<BillRuleException>(() => b.MarkPaid(Now)); // Draft cannot jump to Paid
        b.MarkSent(Now);
        b.MarkPaid(Now.AddDays(1));
        Assert.Equal(BillStatus.Paid, b.Status);
        Assert.Equal(Now.AddDays(1), b.PaidAt);
    }

    [Fact]
    public void Paid_ToSent_Rejected()
    {
        var b = DraftWith(100m);
        b.MarkSent(Now);
        b.MarkPaid(Now);
        Assert.Throws<BillRuleException>(() => b.MarkSent(Now));
        Assert.Throws<BillRuleException>(() => b.MarkPaid(Now));
        Assert.Equal(BillStatus.Paid, b.Status);
    }

    // ---- service and database ----

    [Fact]
    public async Task BillNo_SequentialPerYearUnique()
    {
        var seeded = await _app.NewClientWithCaseAsync();
        var year = Format.ToLocal(DateTime.UtcNow).Year;
        var before = await _app.WithDbAsync(db => db.Bills.CountAsync(b => b.BillNo.StartsWith($"BILL-{year}-")));

        // Concurrent creators, each in its own scope (own connection and transaction).
        var bills = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ =>
            _app.WithServiceAsync<IBillService, Bill>(s => s.CreateDraftAsync(seeded.CaseId))));

        var numbers = bills.Select(b => b.BillNo).ToList();
        Assert.All(numbers, n => Assert.Matches($"^BILL-{year}-\\d{{4}}$", n));
        Assert.Equal(10, numbers.Distinct().Count());
        var seqs = numbers.Select(n => BillNumberGenerator.SeqOf(n, year)).OrderBy(x => x).ToList();
        Assert.Equal(Enumerable.Range(before + 1, 10), seqs);
        Assert.All(bills, b => Assert.Equal(BillStatus.Draft, b.Status));
        Assert.Equal("BILL-2026-0001", BillNumberGenerator.Format(2026, 1));
        Assert.Equal(10000, BillNumberGenerator.SeqOf("BILL-2026-10000", 2026));
    }

    [Fact]
    public async Task StatusChange_WritesAudit()
    {
        var seeded = await _app.NewClientWithCaseAsync();
        var actor = _app.UserId("lawyer1@clienthub.test");
        var bill = await _app.WithServiceAsync<IBillService, Bill>(s => s.CreateDraftAsync(seeded.CaseId));

        Assert.Equal(BillError.NoLines, await _app.WithServiceAsync<IBillService, BillError>(s => s.MarkSentAsync(bill.Id, actor)));
        Assert.Equal(BillError.None, await _app.WithServiceAsync<IBillService, BillError>(s => s.AddLineAsync(bill.Id, new LineInput(LineType.Fee, "Fee", "1,000.50"))));
        Assert.Equal(BillError.Invalid, await _app.WithServiceAsync<IBillService, BillError>(s => s.MarkPaidAsync(bill.Id, actor)));
        Assert.Equal(BillError.None, await _app.WithServiceAsync<IBillService, BillError>(s => s.MarkSentAsync(bill.Id, actor)));
        Assert.Equal(BillError.None, await _app.WithServiceAsync<IBillService, BillError>(s => s.MarkPaidAsync(bill.Id, actor)));
        Assert.Equal(BillError.NotDraft, await _app.WithServiceAsync<IBillService, BillError>(s => s.AddLineAsync(bill.Id, new LineInput(LineType.Fee, "Late", "5"))));

        var rows = await _app.WithDbAsync(db => db.AuditLog.Where(a => a.EntityId == bill.Id).OrderBy(a => a.At).ToListAsync());
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => { Assert.Equal(AuditActions.BillStatusChanged, r.Action); Assert.Equal("Bill", r.Entity); Assert.Equal(actor, r.UserId); });
        var details = rows.Select(r => JsonDocument.Parse(r.Details!).RootElement).ToList();
        Assert.Equal((BillStatus.Draft, BillStatus.Sent), (details[0].GetProperty("from").GetString(), details[0].GetProperty("to").GetString()));
        Assert.Equal((BillStatus.Sent, BillStatus.Paid), (details[1].GetProperty("from").GetString(), details[1].GetProperty("to").GetString()));

        var stored = await _app.WithDbAsync(db => db.Bills.Include(b => b.Lines).SingleAsync(b => b.Id == bill.Id));
        Assert.Equal(1000.50m, stored.Total);
        Assert.Single(stored.Lines);
        Assert.NotNull(stored.IssuedAt);
        Assert.NotNull(stored.PaidAt);
    }

    [Fact]
    public async Task ConcurrentLineAdds_TotalMatchesLines()
    {
        var seeded = await _app.NewClientWithCaseAsync();
        var bill = await _app.WithServiceAsync<IBillService, Bill>(s => s.CreateDraftAsync(seeded.CaseId));
        var results = await Task.WhenAll(Enumerable.Range(1, 8).Select(i =>
            _app.WithServiceAsync<IBillService, BillError>(s => s.AddLineAsync(bill.Id, new LineInput(LineType.Expense, $"Item {i}", "12.34")))));
        Assert.All(results, r => Assert.Equal(BillError.None, r));
        var stored = await _app.WithDbAsync(db => db.Bills.Include(b => b.Lines).SingleAsync(b => b.Id == bill.Id));
        Assert.Equal(8, stored.Lines.Count);
        Assert.Equal(98.72m, stored.Total);
        Assert.Equal(8, stored.Lines.Select(l => l.SortOrder).Distinct().Count());
    }

    /// <summary>Done-criteria flow as Staff: new bill from the case page, lines through HTMX, total read only, send freezes the lines.</summary>
    [Fact]
    public async Task Staff_CreateEditSend_LinesFrozen()
    {
        var staffUser = await _app.CreateUserAsync(Roles.Staff);
        var staff = await _app.SignedInAsync(staffUser.Email!);
        var seeded = await _app.NewClientWithCaseAsync(staffUser.Id);
        var casePage = $"/Staff/Cases/Details/{seeded.CaseId}";
        Assert.Contains("?handler=NewBill", await staff.GetStringAsync(casePage));

        var created = await staff.PostFormAsync($"{casePage}?handler=NewBill", Array.Empty<KeyValuePair<string, string>>(), casePage);
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        var editUrl = created.Headers.Location!.OriginalString;
        Assert.StartsWith("/Staff/Bills/Edit/", editUrl);
        var billId = Guid.Parse(editUrl.Split('/').Last());
        var billNo = await _app.WithDbAsync(db => db.Bills.Where(b => b.Id == billId).Select(b => b.BillNo).SingleAsync());
        Assert.Matches(@"^BILL-\d{4}-\d{4}$", billNo);

        async Task<HttpResponseMessage> Htmx(string handler, Dictionary<string, string> fields)
        {
            var page = await staff.GetStringAsync(editUrl);
            fields["__RequestVerificationToken"] = AppFactory.Token(page);
            var req = new HttpRequestMessage(HttpMethod.Post, $"{editUrl}?handler={handler}") { Content = new FormUrlEncodedContent(fields) };
            req.Headers.Add("HX-Request", "true");
            return await staff.SendAsync(req);
        }

        var add1 = await Htmx("AddLine", new() { ["lineType"] = LineType.Fee, ["description"] = "Legal fee", ["amount"] = "1,500.00" });
        Assert.Equal(HttpStatusCode.OK, add1.StatusCode);
        var add2 = await Htmx("AddLine", new() { ["lineType"] = LineType.Expense, ["description"] = "Stamp duty", ["amount"] = "34.5" });
        var html = await add2.Content.ReadAsStringAsync();
        Assert.Contains("id=\"bill-lines\"", html);
        Assert.Matches("data-bill-total>RM 1,534.50<", html);
        Assert.DoesNotContain("name=\"total\"", html, StringComparison.OrdinalIgnoreCase);

        var bad = await Htmx("AddLine", new() { ["lineType"] = LineType.Fee, ["description"] = "", ["amount"] = "abc" });
        Assert.Equal(HttpStatusCode.OK, bad.StatusCode);
        Assert.Contains("role=\"alert\"", await bad.Content.ReadAsStringAsync());

        var lineId = await _app.WithDbAsync(db => db.BillLines.Where(l => l.BillId == billId && l.LineType == LineType.Expense).Select(l => l.Id).SingleAsync());
        var upd = await Htmx("UpdateLine", new() { ["lineId"] = lineId.ToString(), ["lineType"] = LineType.Expense, ["description"] = "Stamp duty", ["amount"] = "40" });
        Assert.Matches("data-bill-total>RM 1,540.00<", await upd.Content.ReadAsStringAsync());

        var send = await staff.PostFormAsync($"{editUrl}?handler=Send", Array.Empty<KeyValuePair<string, string>>(), editUrl);
        Assert.Equal(HttpStatusCode.Redirect, send.StatusCode);
        var sentPage = await staff.GetStringAsync(editUrl);
        Assert.DoesNotContain("data-add-line", sentPage);
        Assert.DoesNotContain("handler=RemoveLine", sentPage);
        Assert.Contains("handler=Paid", sentPage);

        Assert.Equal(HttpStatusCode.BadRequest, (await Htmx("AddLine", new() { ["lineType"] = LineType.Fee, ["description"] = "Late", ["amount"] = "1" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Htmx("RemoveLine", new() { ["lineId"] = lineId.ToString() })).StatusCode);
        var stored = await _app.WithDbAsync(db => db.Bills.Include(b => b.Lines).SingleAsync(b => b.Id == billId));
        Assert.Equal(BillStatus.Sent, stored.Status);
        Assert.Equal(2, stored.Lines.Count);
        Assert.Equal(1540m, stored.Total);

        // The case page lists the bill.
        Assert.Contains(billNo, await staff.GetStringAsync(casePage));
    }

    /// <summary>Client bill page: amount due, lines, bank details, reference = bill number, QR, print button (R-084, R-154).</summary>
    [Fact]
    public async Task Client_SentBill_ShowsBankQrAndPrint()
    {
        var seeded = await _app.NewClientWithCaseAsync();
        var actor = _app.UserId("lawyer1@clienthub.test");
        var bill = await _app.WithServiceAsync<IBillService, Bill>(s => s.CreateDraftAsync(seeded.CaseId));
        await _app.WithServiceAsync<IBillService, BillError>(s => s.AddLineAsync(bill.Id, new LineInput(LineType.Fee, "Consultation", "300")));
        await _app.WithServiceAsync<IBillService, BillError>(s => s.SaveNoteAsync(bill.Id, "Please pay within 14 days"));
        await _app.WithServiceAsync<IBillService, BillError>(s => s.MarkSentAsync(bill.Id, actor));
        await _app.WithDbAsync(async db =>
        {
            var s = await db.FirmSettings.FirstAsync();
            s.BankName = "Maybank"; s.BankAccountName = "Sample Law Firm"; s.BankAccountNo = "5140 1234 5678"; s.QrStorageKey = "settings/qr/x/qr.png";
            return await db.SaveChangesAsync();
        });

        var client = await _app.SignedInAsync(seeded.ClientUser.Email!);
        var html = await client.GetStringAsync($"/Portal/Bills/Details/{bill.Id}");
        Assert.Contains("data-amount-due", html);
        Assert.Matches("data-bill-total>RM 300.00<", html);
        Assert.Contains("Maybank", html);
        Assert.Contains("5140 1234 5678", html);
        Assert.Matches($"data-bill-reference>{Regex.Escape(bill.BillNo)}<", html);
        Assert.Contains("data-bill-qr", html);
        Assert.Contains("Please pay within 14 days", html);
        Assert.Contains("window.print()", html);
        Assert.Contains("/css/print.css", html);
    }
}
