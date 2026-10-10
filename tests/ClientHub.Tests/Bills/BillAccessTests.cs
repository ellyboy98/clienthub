using System.Net;
using ClientHub.Tests.Support;
using ClientHub.Web.Data.Entities;
using ClientHub.Web.Services.Bills;

namespace ClientHub.Tests.Bills;

/// <summary>Bill access (R-007, R-008, R-012, R-079, R-084): through the case, Clients never see drafts, failures are 404.</summary>
public class BillAccessTests : IClassFixture<FirmAppFactory>
{
    private readonly FirmAppFactory _app;
    public BillAccessTests(FirmAppFactory app) => _app = app;
    private static readonly KeyValuePair<string, string>[] NoFields = Array.Empty<KeyValuePair<string, string>>();

    private async Task<Bill> BillAsync(Guid caseId, bool send)
    {
        var bill = await _app.WithServiceAsync<IBillService, Bill>(s => s.CreateDraftAsync(caseId));
        await _app.WithServiceAsync<IBillService, BillError>(s => s.AddLineAsync(bill.Id, new LineInput(LineType.Fee, "Fee", "100")));
        if (send)
            await _app.WithServiceAsync<IBillService, BillError>(s => s.MarkSentAsync(bill.Id, _app.UserId("lawyer1@clienthub.test")));
        return bill;
    }

    [Fact]
    public async Task Client_DraftBill_404()
    {
        var seeded = await _app.NewClientWithCaseAsync();
        var draft = await BillAsync(seeded.CaseId, send: false);
        var client = await _app.SignedInAsync(seeded.ClientUser.Email!);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/Portal/Bills/Details/{draft.Id}")).StatusCode);

        await _app.WithServiceAsync<IBillService, BillError>(s => s.MarkSentAsync(draft.Id, _app.UserId("lawyer1@clienthub.test")));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/Portal/Bills/Details/{draft.Id}")).StatusCode);
    }

    [Fact]
    public async Task Client_ListHidesDrafts()
    {
        var seeded = await _app.NewClientWithCaseAsync();
        var draft = await BillAsync(seeded.CaseId, send: false);
        var sent = await BillAsync(seeded.CaseId, send: true);
        var client = await _app.SignedInAsync(seeded.ClientUser.Email!);

        var list = await client.GetStringAsync("/Portal/Bills");
        Assert.Contains(sent.BillNo, list);
        Assert.DoesNotContain(draft.BillNo, list);
        var tab = await client.GetStringAsync($"/Portal/Cases/Details/{seeded.CaseId}?tab=bills");
        Assert.Contains(sent.BillNo, tab);
        Assert.DoesNotContain(draft.BillNo, tab);
        // Home "Bills" tile counts only what the client can see (Sent = unpaid).
        Assert.Contains("RM 100.00", await client.GetStringAsync("/Portal"));
    }

    [Fact]
    public async Task Client_OtherClientsBill_404()
    {
        var mine = await _app.NewClientWithCaseAsync();
        var other = await _app.NewClientWithCaseAsync();
        var theirs = await BillAsync(other.CaseId, send: true);
        var client = await _app.SignedInAsync(mine.ClientUser.Email!);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/Portal/Bills/Details/{theirs.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/Portal/Bills/Details/{Guid.NewGuid()}")).StatusCode);
        Assert.DoesNotContain(theirs.BillNo, await client.GetStringAsync("/Portal/Bills"));
        // The staff editor is firm only.
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/Staff/Bills/Edit/{theirs.Id}")).StatusCode);
    }

    [Fact]
    public async Task Staff_UnassignedCaseBill_404()
    {
        var staffUser = await _app.CreateUserAsync(Roles.Staff);
        var staff = await _app.SignedInAsync(staffUser.Email!);
        var assigned = await _app.NewClientWithCaseAsync(staffUser.Id);
        var unassigned = await _app.NewClientWithCaseAsync();
        var mine = await BillAsync(assigned.CaseId, send: false);
        var notMine = await BillAsync(unassigned.CaseId, send: false);

        Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync($"/Staff/Bills/Edit/{mine.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await staff.GetAsync($"/Staff/Bills/Edit/{notMine.Id}")).StatusCode);
        var url = $"/Staff/Bills/Edit/{notMine.Id}";
        Assert.Equal(HttpStatusCode.NotFound, (await staff.PostFormAsync($"{url}?handler=AddLine", new Dictionary<string, string>
            { ["lineType"] = LineType.Fee, ["description"] = "X", ["amount"] = "1" }, "/Staff")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await staff.PostFormAsync($"{url}?handler=Send", NoFields, "/Staff")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await staff.PostFormAsync($"/Staff/Cases/Details/{unassigned.CaseId}?handler=NewBill", NoFields, "/Staff")).StatusCode);

        var list = await staff.GetStringAsync("/Staff/Bills");
        Assert.Contains(mine.BillNo, list);
        Assert.DoesNotContain(notMine.BillNo, list);
        Assert.Equal(1, _app.WithDb(db => db.Bills.Count(b => b.CaseId == unassigned.CaseId)));
        Assert.Single(_app.WithDb(db => db.BillLines.Where(l => l.BillId == notMine.Id).ToList()));
    }

    [Fact]
    public async Task OfficeManager_SeesEveryBill()
    {
        var seeded = await _app.NewClientWithCaseAsync();
        var bill = await BillAsync(seeded.CaseId, send: false);
        var om = await _app.SignedInAsync("om@clienthub.test");
        Assert.Equal(HttpStatusCode.OK, (await om.GetAsync($"/Staff/Bills/Edit/{bill.Id}")).StatusCode);
        Assert.Contains(bill.BillNo, await om.GetStringAsync("/Staff/Bills"));
    }
}
