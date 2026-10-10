using System.Net;
using ClientHub.Tests.Support;
using ClientHub.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace ClientHub.Tests.Cases;

/// <summary>Access rules on every M3 page and handler (R-012, R-015, R-043, R-145, R-008: failures are 404).</summary>
public class CasePageAccessTests : IClassFixture<FirmAppFactory>
{
    private readonly FirmAppFactory _app;
    public CasePageAccessTests(FirmAppFactory app) => _app = app;
    private static readonly KeyValuePair<string, string>[] NoFields = Array.Empty<KeyValuePair<string, string>>();

    private async Task<(HttpClient Client, AppUser User)> NewStaffAsync()
    {
        var u = await _app.CreateUserAsync(Roles.Staff);
        return (await _app.SignedInAsync(u.Email!), u);
    }

    private static Dictionary<string, string> CaseForm(Guid clientId, Guid lawyerId, params Guid[] staff)
    {
        var d = new Dictionary<string, string>
        {
            ["ClientId"] = clientId.ToString(), ["Form.Title"] = "Land dispute", ["Form.CaseType"] = CaseType.Civil,
            ["Form.Description"] = "About the land", ["Form.LawyerId"] = lawyerId.ToString()
        };
        return d;
    }

    private static IEnumerable<KeyValuePair<string, string>> WithStaff(Dictionary<string, string> form, params Guid[] staff) =>
        form.Concat(staff.Select(s => new KeyValuePair<string, string>("Form.StaffIds", s.ToString())));

    [Fact]
    public async Task Staff_PostCreateClient_404()
    {
        var (staff, _) = await NewStaffAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await staff.GetAsync("/Staff/Clients/Create")).StatusCode);
        var email = $"x-{Guid.NewGuid():N}@clienthub.test";
        var res = await staff.PostFormAsync("/Staff/Clients/Create", new Dictionary<string, string>
        {
            ["Input.ClientType"] = ClientType.Person, ["Input.Name"] = "X", ["Input.IcOrRegNo"] = "1", ["Input.Phone"] = "1",
            ["Input.Email"] = email, ["Input.Address"] = "A"
        }, "/Staff");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.False(_app.WithDb(db => db.Users.Any(u => u.Email == email)));
        Assert.DoesNotContain("/Staff/Clients/Create", await staff.GetStringAsync("/Staff/Clients"));
    }

    [Fact]
    public async Task Staff_PostCreateCase_404()
    {
        var (staff, staffUser) = await NewStaffAsync();
        var seeded = await _app.NewClientWithCaseAsync(staffUser.Id);
        Assert.Equal(HttpStatusCode.NotFound, (await staff.GetAsync($"/Staff/Cases/Create?clientId={seeded.ClientId}")).StatusCode);
        var res = await staff.PostFormAsync("/Staff/Cases/Create", CaseForm(seeded.ClientId, _app.UserId("lawyer1@clienthub.test")), "/Staff");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.Equal(1, _app.WithDb(db => db.Cases.Count(c => c.ClientId == seeded.ClientId)));
        Assert.DoesNotContain("/Staff/Cases/Create", await staff.GetStringAsync($"/Staff/Clients/Details/{seeded.ClientId}"));
    }

    [Fact]
    public async Task Staff_EditAssignedStaff_404()
    {
        var (staff, staffUser) = await NewStaffAsync();
        var other = await _app.CreateUserAsync(Roles.Staff);
        var seeded = await _app.NewClientWithCaseAsync(staffUser.Id);
        var details = $"/Staff/Cases/Details/{seeded.CaseId}";

        var res = await staff.PostFormAsync(details + "?handler=Staff", new[] { new KeyValuePair<string, string>("staffIds", other.Id.ToString()) }, details);
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        // Edit page: a Staff POST carrying lawyer or staff fields is refused too (Q-10).
        res = await staff.PostFormAsync($"/Staff/Cases/Edit/{seeded.CaseId}",
            WithStaff(new Dictionary<string, string> { ["Form.Title"] = "T", ["Form.CaseType"] = CaseType.Civil }, other.Id), details);
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.Equal(new[] { staffUser.Id }, _app.WithDb(db => db.CaseStaff.Where(s => s.CaseId == seeded.CaseId).Select(s => s.UserId).ToArray()));
        Assert.DoesNotContain("name=\"staffIds\"", await staff.GetStringAsync(details));

        // Staff may still edit title, type and description (Q-10).
        res = await staff.PostFormAsync($"/Staff/Cases/Edit/{seeded.CaseId}",
            new Dictionary<string, string> { ["Form.Title"] = "Renamed by staff", ["Form.CaseType"] = CaseType.Criminal }, details);
        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
        Assert.Equal("Renamed by staff", _app.WithDb(db => db.Cases.Single(c => c.Id == seeded.CaseId).Title));
    }

    [Fact]
    public async Task Om_EditAssignedStaff_Saves()
    {
        var om = await _app.SignedInAsync("om@clienthub.test");
        var other = await _app.CreateUserAsync(Roles.Staff);
        var seeded = await _app.NewClientWithCaseAsync(_app.UserId("staff@clienthub.test"));
        var details = $"/Staff/Cases/Details/{seeded.CaseId}";
        var res = await om.PostFormAsync(details + "?handler=Staff", new[] { new KeyValuePair<string, string>("staffIds", other.Id.ToString()) }, details);
        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
        Assert.Equal(new[] { other.Id }, _app.WithDb(db => db.CaseStaff.Where(s => s.CaseId == seeded.CaseId).Select(s => s.UserId).ToArray()));
    }

    [Fact]
    public async Task Staff_UnassignedCase_404()
    {
        var (staff, _) = await NewStaffAsync();
        var seeded = await _app.NewClientWithCaseAsync();
        var details = $"/Staff/Cases/Details/{seeded.CaseId}";
        Assert.Equal(HttpStatusCode.NotFound, (await staff.GetAsync(details)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await staff.GetAsync($"/Staff/Cases/Edit/{seeded.CaseId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await staff.GetAsync($"/Staff/Clients/Details/{seeded.ClientId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await staff.GetAsync($"/Staff/Clients/Edit/{seeded.ClientId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await staff.PostFormAsync(details + "?handler=Status",
            new Dictionary<string, string> { ["status"] = CaseStatus.Filed }, "/Staff")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await staff.PostFormAsync(details + "?handler=Update",
            new Dictionary<string, string> { ["body"] = "sneaky" }, "/Staff")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await staff.PostFormAsync($"/Staff/Clients/Edit/{seeded.ClientId}",
            new Dictionary<string, string> { ["Input.Name"] = "x" }, "/Staff")).StatusCode);
        Assert.Equal(CaseStatus.New, _app.WithDb(db => db.Cases.Single(c => c.Id == seeded.CaseId).Status));
        Assert.False(_app.WithDb(db => db.CaseUpdates.Any(u => u.CaseId == seeded.CaseId)));
        // R-012: a Staff user with no assigned cases sees empty lists.
        foreach (var list in new[] { "/Staff/Clients", "/Staff/Cases", "/Staff/PastCases", "/Staff" })
            Assert.DoesNotContain(seeded.CaseId.ToString(), await staff.GetStringAsync(list));
        Assert.DoesNotContain(seeded.ClientId.ToString(), await staff.GetStringAsync("/Staff/Clients"));
    }

    [Fact]
    public async Task Staff_DeactivateClient_404()
    {
        var (staff, staffUser) = await NewStaffAsync();
        var seeded = await _app.NewClientWithCaseAsync(staffUser.Id);
        var details = $"/Staff/Clients/Details/{seeded.ClientId}";
        Assert.Equal(HttpStatusCode.NotFound, (await staff.PostFormAsync(details + "?handler=Deactivate", NoFields, details)).StatusCode);
        Assert.True(_app.WithDb(db => db.Users.Single(u => u.Id == seeded.ClientUser.Id).IsActive));
    }

    [Fact]
    public async Task Client_OtherClientCase_404()
    {
        var mine = await _app.NewClientWithCaseAsync();
        var theirs = await _app.NewClientWithCaseAsync();
        var client = await _app.SignedInAsync(mine.ClientUser.Email!);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/Portal/Cases/Details/{mine.CaseId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/Portal/Cases/Details/{theirs.CaseId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/Portal/Cases/Details/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/Staff/Cases/Details/{mine.CaseId}")).StatusCode);
        foreach (var list in new[] { "/Portal", "/Portal/Cases", "/Portal/Cases/Past" })
            Assert.DoesNotContain(theirs.CaseId.ToString(), await client.GetStringAsync(list));
    }

    [Fact]
    public async Task Dashboard_StaffCountsOnlyAssigned()
    {
        var (staff, staffUser) = await NewStaffAsync();
        var assigned = await _app.NewClientWithCaseAsync(staffUser.Id);
        var assignedPast = await _app.NewClientWithCaseAsync(staffUser.Id, CaseStatus.Completed);
        var notAssigned = await _app.NewClientWithCaseAsync();
        await _app.WithDbAsync(async db =>
        {
            db.DocumentRequests.Add(new DocumentRequest { CaseId = assigned.CaseId, Name = "IC copy" });
            db.DocumentRequests.Add(new DocumentRequest { CaseId = notAssigned.CaseId, Name = "IC copy" });
            db.Bills.Add(new Bill { CaseId = notAssigned.CaseId, BillNo = "T-" + Guid.NewGuid().ToString("N")[..8], Status = BillStatus.Sent });
            db.Messages.Add(new Message { ClientId = notAssigned.ClientId, SenderId = notAssigned.ClientUser.Id, Body = "hi", SentAt = DateTime.UtcNow });
            db.Messages.Add(new Message { ClientId = assigned.ClientId, SenderId = assigned.ClientUser.Id, Body = "hi", SentAt = DateTime.UtcNow });
            return await db.SaveChangesAsync();
        });

        var html = await staff.GetStringAsync("/Staff");
        Assert.Equal(1, TestData.Tile(html, "active"));
        Assert.Equal(1, TestData.Tile(html, "documents"));
        Assert.Equal(1, TestData.Tile(html, "chats"));
        Assert.Equal(0, TestData.Tile(html, "bills"));
        Assert.Contains(assigned.CaseId.ToString(), html);
        Assert.DoesNotContain(notAssigned.CaseId.ToString(), html);
        Assert.DoesNotContain("Needs your attention", html);

        var past = await staff.GetStringAsync("/Staff/PastCases");
        Assert.Contains(assignedPast.CaseId.ToString(), past);
        var cases = await staff.GetStringAsync("/Staff/Cases");
        Assert.Contains(assigned.CaseId.ToString(), cases);
        Assert.DoesNotContain(assignedPast.CaseId.ToString(), cases);

        // The Lawyer sees every active case.
        var lawyer = await _app.SignedInAsync("lawyer1@clienthub.test");
        var lawyerActive = TestData.Tile(await lawyer.GetStringAsync("/Staff"), "active");
        Assert.Equal(_app.WithDb(db => db.Cases.Count(c => c.Status != CaseStatus.Completed && c.Status != CaseStatus.Cancelled)), lawyerActive);
    }

    [Fact]
    public async Task Om_CreateCase_WithStaff_ShowsNew()
    {
        var om = await _app.SignedInAsync("om@clienthub.test");
        var seeded = await _app.NewClientWithCaseAsync();
        var staffId = _app.UserId("staff@clienthub.test");
        var form = await om.GetAsync($"/Staff/Cases/Create?clientId={seeded.ClientId}");
        Assert.Equal(HttpStatusCode.OK, form.StatusCode);
        var res = await om.PostFormAsync("/Staff/Cases/Create",
            WithStaff(CaseForm(seeded.ClientId, _app.UserId("lawyer2@clienthub.test")), staffId), "/Staff/Clients");
        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
        var location = res.Headers.Location!.OriginalString;
        Assert.StartsWith("/Staff/Cases/Details/", location);

        var created = await _app.WithDbAsync(db => db.Cases.Include(c => c.StaffLinks).SingleAsync(c => c.ClientId == seeded.ClientId && c.Title == "Land dispute"));
        Assert.Equal(CaseStatus.New, created.Status);
        Assert.Equal(staffId, Assert.Single(created.StaffLinks).UserId);
        var page = await om.GetStringAsync(location);
        Assert.Contains("Land dispute", page);
        Assert.Contains(">New<", page.Replace("</span>", "<"));
        // The assigned Staff now sees it.
        var staff = await _app.SignedInAsync("staff@clienthub.test");
        Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync(location)).StatusCode);
    }

    [Fact]
    public async Task Completed_MovesToPast_ReopenReturns()
    {
        var (staff, staffUser) = await NewStaffAsync();
        var seeded = await _app.NewClientWithCaseAsync(staffUser.Id);
        var client = await _app.SignedInAsync(seeded.ClientUser.Email!);
        var details = $"/Staff/Cases/Details/{seeded.CaseId}";
        var id = seeded.CaseId.ToString();

        async Task SetStatus(string s) => Assert.Equal(HttpStatusCode.Redirect,
            (await staff.PostFormAsync(details + "?handler=Status", new Dictionary<string, string> { ["status"] = s }, details)).StatusCode);

        await SetStatus(CaseStatus.Hearing);
        await SetStatus(CaseStatus.DocumentsCollected); // backward move
        await SetStatus(CaseStatus.Completed);
        Assert.Contains(id, await staff.GetStringAsync("/Staff/PastCases"));
        Assert.DoesNotContain(id, await staff.GetStringAsync("/Staff/Cases"));
        Assert.Contains(id, await client.GetStringAsync("/Portal/Cases/Past"));
        Assert.DoesNotContain(id, await client.GetStringAsync("/Portal/Cases"));
        Assert.NotNull(_app.WithDb(db => db.Cases.Single(c => c.Id == seeded.CaseId).ClosedAt));

        await SetStatus(CaseStatus.Hearing); // reopen (Q-09)
        Assert.DoesNotContain(id, await staff.GetStringAsync("/Staff/PastCases"));
        Assert.Contains(id, await client.GetStringAsync("/Portal/Cases"));
        Assert.Null(_app.WithDb(db => db.Cases.Single(c => c.Id == seeded.CaseId).ClosedAt));
        Assert.Equal(4, _app.WithDb(db => db.AuditLog.Count(a => a.EntityId == seeded.CaseId && a.Action == "Case.StatusChanged")));
    }

    [Fact]
    public async Task Client_HomeAndCase_ShowsUpdatesNewestFirst_NoEditControls()
    {
        var (staff, staffUser) = await NewStaffAsync();
        var seeded = await _app.NewClientWithCaseAsync(staffUser.Id, title: "Tenancy matter");
        var details = $"/Staff/Cases/Details/{seeded.CaseId}";
        foreach (var body in new[] { "First update", "Second update" })
        {
            Assert.Equal(HttpStatusCode.Redirect, (await staff.PostFormAsync(details + "?handler=Update",
                new Dictionary<string, string> { ["body"] = body }, details)).StatusCode);
            await Task.Delay(20);
        }

        var client = await _app.SignedInAsync(seeded.ClientUser.Email!);
        var home = await client.GetStringAsync("/Portal");
        var name = _app.WithDb(db => db.Clients.Single(c => c.Id == seeded.ClientId).Name);
        Assert.Contains($"Hello, {name}", home);
        Assert.Contains("Tenancy matter", home);
        Assert.Contains($"/Portal/Cases/Details/{seeded.CaseId}", home);

        var page = await client.GetStringAsync($"/Portal/Cases/Details/{seeded.CaseId}");
        Assert.True(page.IndexOf("Second update", StringComparison.Ordinal) < page.IndexOf("First update", StringComparison.Ordinal));
        Assert.DoesNotContain("handler=Status", page);
        Assert.DoesNotContain("handler=Update", page);
        Assert.DoesNotContain("/Staff/Cases/Edit", page);
        Assert.Contains("Lawyer One", page);

        // R-043: a client cannot post to the staff handlers.
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostFormAsync(details + "?handler=Status",
            new Dictionary<string, string> { ["status"] = CaseStatus.Filed }, "/Account/Manage")).StatusCode);
    }

    [Fact]
    public async Task AllM3Pages_Render_ForOmStaffAndClient()
    {
        var staffId = _app.UserId("staff@clienthub.test");
        var seeded = await _app.NewClientWithCaseAsync(staffId);
        var om = await _app.SignedInAsync("om@clienthub.test");
        var staff = await _app.SignedInAsync("staff@clienthub.test");
        var client = await _app.SignedInAsync(seeded.ClientUser.Email!);
        var firmPages = new[]
        {
            "/Staff", "/Staff/Clients", "/Staff/Clients?filter=company", $"/Staff/Clients/Details/{seeded.ClientId}",
            $"/Staff/Clients/Edit/{seeded.ClientId}", "/Staff/Cases", "/Staff/PastCases",
            $"/Staff/Cases/Details/{seeded.CaseId}", $"/Staff/Cases/Edit/{seeded.CaseId}"
        };
        foreach (var p in firmPages.Concat(new[] { "/Staff/Clients/Create", "/Staff/Cases/Create" }))
            Assert.True((await om.GetAsync(p)).StatusCode == HttpStatusCode.OK, "OM " + p);
        foreach (var p in firmPages)
            Assert.True((await staff.GetAsync(p)).StatusCode == HttpStatusCode.OK, "Staff " + p);
        foreach (var p in new[] { "/Portal", "/Portal/Cases", "/Portal/Cases/Past", $"/Portal/Cases/Details/{seeded.CaseId}",
                     $"/Portal/Cases/Details/{seeded.CaseId}?tab=documents" })
            Assert.True((await client.GetAsync(p)).StatusCode == HttpStatusCode.OK, "Client " + p);
        // Malay labels on the new pages.
        await om.GetAsync("/Account/Culture?culture=ms&returnUrl=%2FStaff");
        Assert.Contains("Klien", await om.GetStringAsync("/Staff/Clients"));
    }
}
