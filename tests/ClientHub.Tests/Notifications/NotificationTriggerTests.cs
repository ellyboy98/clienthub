using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using ClientHub.Tests.Support;
using ClientHub.Web.Data.Entities;
using ClientHub.Web.Services.Bills;
using ClientHub.Web.Services.Notifications;
using Microsoft.EntityFrameworkCore;

namespace ClientHub.Tests.Notifications;

/// <summary>
/// Every notification trigger (R-086..R-094): who gets it and who does not, email rules (R-098..R-100, Q-18), and the
/// notification pages (R-102, R-103, R-134): scoped to the current user, others' ids are 404.
/// </summary>
public class NotificationTriggerTests : IClassFixture<FirmAppFactory>
{
    private readonly FirmAppFactory _app;
    public NotificationTriggerTests(FirmAppFactory app) => _app = app;

    private const string ClientTokenPage = "/Account/Manage";
    private static string CaseUrl(Guid id) => $"/Staff/Cases/Details/{id}";
    private static string PortalCase(Guid id) => $"/Portal/Cases/Details/{id}";

    private Guid Lawyer1 => _app.UserId("lawyer1@clienthub.test");
    private Guid Lawyer2 => _app.UserId("lawyer2@clienthub.test");
    private Guid Om => _app.UserId("om@clienthub.test");

    private Task<List<Notification>> NotesAsync(Guid userId, string link) =>
        _app.WithDbAsync(db => db.Notifications.Where(n => n.UserId == userId && n.LinkUrl == link).ToListAsync());

    private Task<int> AllNotesForLinkAsync(string link) =>
        _app.WithDbAsync(db => db.Notifications.CountAsync(n => n.LinkUrl == link));

    private List<SentEmail> EmailsTo(string email) => _app.Emails.Sent.Where(e => e.To == email).ToList();

    private async Task<(HttpClient Http, AppUser User)> NewStaffAsync()
    {
        var u = await _app.CreateUserAsync(Roles.Staff);
        return (await _app.SignedInAsync(u.Email!), u);
    }

    private async Task<Guid> AddRequestAsync(Guid caseId)
    {
        var r = new DocumentRequest { CaseId = caseId, Name = "IC copy", Status = RequestStatus.Pending };
        await _app.WithDbAsync(async db => { db.DocumentRequests.Add(r); return await db.SaveChangesAsync(); });
        return r.Id;
    }

    private static async Task<HttpResponseMessage> UploadAsync(HttpClient http, string tokenPage, Guid caseId, Guid? requestId, string name = "secret-ic-900101.pdf")
    {
        var fields = new Dictionary<string, string> { ["kind"] = "Case", ["caseId"] = caseId.ToString(), ["fileName"] = name, ["size"] = "2048" };
        if (requestId is { } r) fields["requestId"] = r.ToString();
        var start = await http.PostFormAsync("/Uploads/Start", fields, tokenPage);
        Assert.Equal(HttpStatusCode.OK, start.StatusCode);
        var json = JsonDocument.Parse(await start.Content.ReadAsStringAsync()).RootElement;
        Assert.True((await TestStorage.PutPresignedAsync(json.GetProperty("url").GetString()!, TestStorage.Pdf(),
            json.GetProperty("contentType").GetString()!)).IsSuccessStatusCode);
        return await http.PostFormAsync("/Uploads/Complete",
            new Dictionary<string, string> { ["ticket"] = json.GetProperty("ticket").GetString()! }, tokenPage);
    }

    private Task SetLanguageAsync(Guid userId, string lang) =>
        _app.WithDbAsync(async db => { (await db.Users.SingleAsync(u => u.Id == userId)).Language = lang; return await db.SaveChangesAsync(); });

    private static int BellCount(string html)
    {
        var m = Regex.Match(html, "data-bell-count[^>]*>(\\d+)<");
        return m.Success ? int.Parse(m.Groups[1].Value) : 0;
    }

    // ---- Client-facing triggers (in-system + email) ----

    [Fact]
    public async Task StatusChange_NotifiesAndEmailsClient()
    {
        var (staff, staffUser) = await NewStaffAsync();
        var s = await _app.NewClientWithCaseAsync(staffUser.Id, title: "Tan v Lim");
        var res = await staff.PostFormAsync(CaseUrl(s.CaseId) + "?handler=Status",
            new Dictionary<string, string> { ["status"] = CaseStatus.Filed }, CaseUrl(s.CaseId));
        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);

        var notes = await NotesAsync(s.ClientUser.Id, PortalCase(s.CaseId));
        var n = Assert.Single(notes);
        Assert.Equal(NotificationKinds.CaseStatusChanged, n.Kind);
        Assert.Equal(1, await AllNotesForLinkAsync(PortalCase(s.CaseId))); // nobody else
        Assert.Empty(await NotesAsync(staffUser.Id, CaseUrl(s.CaseId)));
        var mail = Assert.Single(EmailsTo(s.ClientUser.Email!));
        Assert.Contains($"{AppFactory.BaseUrl}/Account/Login?ReturnUrl=", mail.Body);
    }

    [Fact]
    public async Task UpdatePosted_NotifiesAndEmailsClient()
    {
        var (staff, staffUser) = await NewStaffAsync();
        var s = await _app.NewClientWithCaseAsync(staffUser.Id);
        var res = await staff.PostFormAsync(CaseUrl(s.CaseId) + "?handler=Update",
            new Dictionary<string, string> { ["body"] = "Hearing moved to Monday" }, CaseUrl(s.CaseId));
        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);

        Assert.Equal(NotificationKinds.CaseUpdatePosted, Assert.Single(await NotesAsync(s.ClientUser.Id, PortalCase(s.CaseId))).Kind);
        Assert.Equal(1, await AllNotesForLinkAsync(PortalCase(s.CaseId)));
        var mail = Assert.Single(EmailsTo(s.ClientUser.Email!));
        Assert.DoesNotContain("Hearing moved", mail.Body);
    }

    [Fact]
    public async Task RequestAdded_NotifiesAndEmailsClient()
    {
        var (staff, staffUser) = await NewStaffAsync();
        var s = await _app.NewClientWithCaseAsync(staffUser.Id);
        var res = await staff.PostFormAsync(CaseUrl(s.CaseId) + "?handler=Request",
            new Dictionary<string, string> { ["name"] = "Bank statement", ["note"] = "" }, CaseUrl(s.CaseId));
        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);

        Assert.Equal(NotificationKinds.DocumentRequested, Assert.Single(await NotesAsync(s.ClientUser.Id, PortalCase(s.CaseId))).Kind);
        var mail = Assert.Single(EmailsTo(s.ClientUser.Email!));
        Assert.DoesNotContain("Bank statement", mail.Body);
    }

    [Fact]
    public async Task RequestRejected_NotifiesAndEmailsClient()
    {
        var (staff, staffUser) = await NewStaffAsync();
        var s = await _app.NewClientWithCaseAsync(staffUser.Id);
        var requestId = await AddRequestAsync(s.CaseId);
        var client = await _app.SignedInAsync(s.ClientUser.Email!);
        Assert.Equal(HttpStatusCode.OK, (await UploadAsync(client, ClientTokenPage, s.CaseId, requestId)).StatusCode);

        var res = await staff.PostFormAsync(CaseUrl(s.CaseId) + "?handler=Reject",
            new Dictionary<string, string> { ["requestId"] = requestId.ToString(), ["reason"] = "Blurry" }, CaseUrl(s.CaseId));
        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);

        Assert.Equal(NotificationKinds.DocumentRejected, Assert.Single(await NotesAsync(s.ClientUser.Id, PortalCase(s.CaseId))).Kind);
        var mail = Assert.Single(EmailsTo(s.ClientUser.Email!));
        Assert.DoesNotContain("Blurry", mail.Body);
    }

    [Fact]
    public async Task FirmChat_NotifiesAndEmailsClient()
    {
        var (staff, staffUser) = await NewStaffAsync();
        var s = await _app.NewClientWithCaseAsync(staffUser.Id);
        var res = await staff.PostFormAsync($"/Staff/Chats/{s.ClientId}?handler=Send",
            new Dictionary<string, string> { ["body"] = "Your hearing is on 3 March" }, "/Staff");
        Assert.True(res.IsSuccessStatusCode || res.StatusCode == HttpStatusCode.Redirect);

        var n = Assert.Single(await NotesAsync(s.ClientUser.Id, "/Portal/Chat"));
        Assert.Equal(NotificationKinds.FirmMessage, n.Kind);
        Assert.DoesNotContain("hearing", n.Title, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await NotesAsync(Lawyer1, $"/Staff/Chats/{s.ClientId}"));
        var mail = Assert.Single(EmailsTo(s.ClientUser.Email!));
        Assert.DoesNotContain("3 March", mail.Body);
    }

    [Fact]
    public async Task BillSent_And_BillPaid_NotifyAndEmailClient()
    {
        var s = await _app.NewClientWithCaseAsync();
        var bill = await _app.WithServiceAsync<IBillService, Bill>(b => b.CreateDraftAsync(s.CaseId));
        var link = $"/Portal/Bills/Details/{bill.Id}";
        await _app.WithServiceAsync<IBillService, BillError>(b => b.AddLineAsync(bill.Id, new LineInput(LineType.Fee, "Advice", "500.00")));
        Assert.Empty(await NotesAsync(s.ClientUser.Id, link)); // a Draft is not notified

        Assert.Equal(BillError.None, await _app.WithServiceAsync<IBillService, BillError>(b => b.MarkSentAsync(bill.Id, Lawyer1)));
        var sent = Assert.Single(await NotesAsync(s.ClientUser.Id, link));
        Assert.Equal(NotificationKinds.BillSent, sent.Kind);
        Assert.Contains(bill.BillNo, sent.Title);

        Assert.Equal(BillError.None, await _app.WithServiceAsync<IBillService, BillError>(b => b.MarkPaidAsync(bill.Id, Lawyer1)));
        var notes = await NotesAsync(s.ClientUser.Id, link);
        Assert.Equal(2, notes.Count);
        Assert.Contains(notes, n => n.Kind == NotificationKinds.BillPaid);
        Assert.Equal(2, await AllNotesForLinkAsync(link));
        Assert.Equal(2, EmailsTo(s.ClientUser.Email!).Count);
    }

    // ---- Client events (firm recipients, in-system only) ----

    [Fact]
    public async Task ClientUpload_NotifiesLawyerAndAssignedStaffOnly()
    {
        var (_, assigned) = await NewStaffAsync();
        var (_, unassigned) = await NewStaffAsync();
        var s = await _app.NewClientWithCaseAsync(assigned.Id);
        var requestId = await AddRequestAsync(s.CaseId);
        var client = await _app.SignedInAsync(s.ClientUser.Email!);
        var before = _app.Emails.Sent.Count;
        Assert.Equal(HttpStatusCode.OK, (await UploadAsync(client, ClientTokenPage, s.CaseId, requestId)).StatusCode);

        var link = CaseUrl(s.CaseId);
        var n = Assert.Single(await NotesAsync(Lawyer1, link));
        Assert.Equal(NotificationKinds.DocumentUploaded, n.Kind);
        Assert.DoesNotContain("secret-ic", n.Title);
        Assert.Single(await NotesAsync(assigned.Id, link));
        Assert.Empty(await NotesAsync(unassigned.Id, link));
        Assert.Empty(await NotesAsync(Lawyer2, link));
        Assert.Equal(2, await AllNotesForLinkAsync(link));
        Assert.Equal(before, _app.Emails.Sent.Count); // R-098: firm users are never emailed
    }

    [Fact]
    public async Task ClientUpload_NotOfficeManager()
    {
        var s = await _app.NewClientWithCaseAsync();
        var requestId = await AddRequestAsync(s.CaseId);
        var client = await _app.SignedInAsync(s.ClientUser.Email!);
        Assert.Equal(HttpStatusCode.OK, (await UploadAsync(client, ClientTokenPage, s.CaseId, requestId)).StatusCode);
        Assert.Empty(await NotesAsync(Om, CaseUrl(s.CaseId)));
        Assert.Single(await NotesAsync(Lawyer1, CaseUrl(s.CaseId)));
    }

    [Fact]
    public async Task FirmUpload_SendsNoNotification()
    {
        var (staff, staffUser) = await NewStaffAsync();
        var s = await _app.NewClientWithCaseAsync(staffUser.Id);
        Assert.Equal(HttpStatusCode.OK, (await UploadAsync(staff, CaseUrl(s.CaseId), s.CaseId, null)).StatusCode);
        Assert.Equal(0, await AllNotesForLinkAsync(CaseUrl(s.CaseId)));
        Assert.Equal(0, await AllNotesForLinkAsync(PortalCase(s.CaseId)));
    }

    [Fact]
    public async Task ClientMessage_DedupsAcrossCases()
    {
        var (_, staffA) = await NewStaffAsync();
        var (_, staffB) = await NewStaffAsync();
        var s = await _app.NewClientWithCaseAsync(staffA.Id);
        // Second case of the same client: same lawyer and staff A again, plus staff B, lawyer2 in charge.
        var second = new Case { ClientId = s.ClientId, Title = "Second", LawyerId = Lawyer2, Status = CaseStatus.New, OpenedAt = DateTime.UtcNow };
        await _app.WithDbAsync(async db =>
        {
            db.Cases.Add(second);
            db.CaseStaff.Add(new CaseStaff { CaseId = second.Id, UserId = staffA.Id });
            db.CaseStaff.Add(new CaseStaff { CaseId = second.Id, UserId = staffB.Id });
            return await db.SaveChangesAsync();
        });
        var client = await _app.SignedInAsync(s.ClientUser.Email!);
        var before = _app.Emails.Sent.Count;
        var res = await client.PostFormAsync("/Portal/Chat?handler=Send", new Dictionary<string, string> { ["body"] = "Private details" }, ClientTokenPage);
        Assert.True(res.IsSuccessStatusCode || res.StatusCode == HttpStatusCode.Redirect);

        var link = $"/Staff/Chats/{s.ClientId}";
        foreach (var id in new[] { Lawyer1, Lawyer2, staffA.Id, staffB.Id })
        {
            var n = Assert.Single(await NotesAsync(id, link));
            Assert.Equal(NotificationKinds.ClientMessage, n.Kind);
            Assert.DoesNotContain("Private", n.Title);
        }
        Assert.Empty(await NotesAsync(Om, link));
        Assert.Equal(4, await AllNotesForLinkAsync(link));
        Assert.Empty(await NotesAsync(s.ClientUser.Id, "/Portal/Chat"));
        Assert.Equal(before, _app.Emails.Sent.Count);
    }

    [Fact]
    public async Task ClientMessage_NoCases_AllActiveLawyers()
    {
        var inactiveLawyer = await _app.CreateUserAsync(Roles.Lawyer, active: false);
        var (_, staff) = await NewStaffAsync();
        var user = await _app.CreateUserAsync(Roles.Client);
        var c = new Client { ClientType = ClientType.Person, Name = "No Case", IcOrRegNo = "1", Phone = "1", Email = user.Email, Address = "x", UserId = user.Id };
        await _app.WithDbAsync(async db => { db.Clients.Add(c); return await db.SaveChangesAsync(); });

        var recipients = await _app.WithServiceAsync<ClientHub.Web.Services.Access.IAccessService, List<Guid>>(a => a.FirmRecipientsForClientMessageAsync(c.Id));
        var activeLawyers = await _app.WithDbAsync(db => db.Users.Where(u => u.Role == Roles.Lawyer && u.IsActive).Select(u => u.Id).ToListAsync());
        Assert.Equal(activeLawyers.OrderBy(x => x), recipients.OrderBy(x => x));

        var client = await _app.SignedInAsync(user.Email!);
        await client.PostFormAsync("/Portal/Chat?handler=Send", new Dictionary<string, string> { ["body"] = "hello" }, ClientTokenPage);
        var link = $"/Staff/Chats/{c.Id}";
        Assert.Single(await NotesAsync(Lawyer1, link));
        Assert.Single(await NotesAsync(Lawyer2, link));
        Assert.Empty(await NotesAsync(inactiveLawyer.Id, link));
        Assert.Empty(await NotesAsync(Om, link));
        Assert.Empty(await NotesAsync(staff.Id, link));
    }

    [Fact]
    public async Task ClientEvents_NoActiveRecipient_FallBackToAllActiveLawyers()
    {
        var goneLawyer = await _app.CreateUserAsync(Roles.Lawyer);
        var (_, unassigned) = await NewStaffAsync();
        var s = await _app.NewClientWithCaseAsync();
        await _app.WithDbAsync(async db =>
        {
            (await db.Cases.SingleAsync(c => c.Id == s.CaseId)).LawyerId = goneLawyer.Id;
            (await db.Users.SingleAsync(u => u.Id == goneLawyer.Id)).IsActive = false;
            return await db.SaveChangesAsync();
        });
        var activeLawyers = await _app.WithDbAsync(db => db.Users.Where(u => u.Role == Roles.Lawyer && u.IsActive).Select(u => u.Id).ToListAsync());

        var client = await _app.SignedInAsync(s.ClientUser.Email!);
        await client.PostFormAsync("/Portal/Chat?handler=Send", new Dictionary<string, string> { ["body"] = "anyone?" }, ClientTokenPage);
        var requestId = await AddRequestAsync(s.CaseId);
        Assert.Equal(HttpStatusCode.OK, (await UploadAsync(client, ClientTokenPage, s.CaseId, requestId)).StatusCode);

        foreach (var link in new[] { $"/Staff/Chats/{s.ClientId}", CaseUrl(s.CaseId) })
        {
            foreach (var id in activeLawyers) Assert.Single(await NotesAsync(id, link));
            Assert.Empty(await NotesAsync(goneLawyer.Id, link));
            Assert.Empty(await NotesAsync(Om, link));
            Assert.Empty(await NotesAsync(unassigned.Id, link));
            Assert.Equal(activeLawyers.Count, await AllNotesForLinkAsync(link));
        }
    }

    [Fact]
    public async Task List_Pages_OlderNotificationsReachable()
    {
        var s = await _app.NewClientWithCaseAsync();
        var start = DateTime.UtcNow.AddDays(-1);
        var rows = Enumerable.Range(0, 120).Select(i => new Notification
        {
            UserId = s.ClientUser.Id, Kind = NotificationKinds.CaseUpdatePosted, Title = $"Item {i:000}", LinkUrl = PortalCase(s.CaseId)
        }).ToList();
        await _app.WithDbAsync(async db =>
        {
            db.Notifications.AddRange(rows);
            await db.SaveChangesAsync();
            for (var i = 0; i < rows.Count; i++) // CreatedAt is stamped on save; spread them so order is defined
                await db.Notifications.Where(n => n.Id == rows[i].Id).ExecuteUpdateAsync(u => u.SetProperty(n => n.CreatedAt, start.AddMinutes(i)));
            return 0;
        });
        var client = await _app.SignedInAsync(s.ClientUser.Email!);
        var p1 = await client.GetStringAsync("/Notifications");
        Assert.Contains("Item 119", p1);
        Assert.DoesNotContain("Item 069", p1);
        Assert.Contains("/Notifications?p=2", p1);
        var p3 = await client.GetStringAsync("/Notifications?p=3");
        Assert.Contains("Item 000", p3);
        Assert.Contains("Item 019", p3);
        Assert.DoesNotContain("Item 020", p3);
        Assert.DoesNotContain("/Notifications?p=4", p3);

        var open = await client.PostFormAsync($"/Notifications/Open/{rows[0].Id}", Array.Empty<KeyValuePair<string, string>>(), ClientTokenPage);
        Assert.Equal(HttpStatusCode.Redirect, open.StatusCode);
        Assert.Equal(119, BellCount(await client.GetStringAsync("/Portal")));
    }

    // ---- Email rules ----

    [Fact]
    public async Task FirmUsers_NeverEmailed()
    {
        var staff = await _app.CreateUserAsync(Roles.Staff);
        var link = $"/Staff/Cases/Details/{Guid.NewGuid()}";
        await _app.WithServiceAsync<INotificationService, int>(async n =>
        {
            await n.NotifyAsync(new[] { staff.Id, Lawyer1, Om }, NotificationKinds.CaseStatusChanged, link, "x");
            return 0;
        });
        Assert.Equal(3, await AllNotesForLinkAsync(link));
        Assert.DoesNotContain(_app.Emails.Sent, e => e.To == staff.Email || e.To == "lawyer1@clienthub.test" || e.To == "om@clienthub.test");
    }

    [Fact]
    public async Task SmtpFails_NotificationSavedActionSucceeds()
    {
        var (staff, staffUser) = await NewStaffAsync();
        var s = await _app.NewClientWithCaseAsync(staffUser.Id);
        _app.Emails.Fail = true;
        try
        {
            var res = await staff.PostFormAsync(CaseUrl(s.CaseId) + "?handler=Update",
                new Dictionary<string, string> { ["body"] = "Posted while SMTP is down" }, CaseUrl(s.CaseId));
            Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
        }
        finally { _app.Emails.Fail = false; }
        Assert.True(await _app.WithDbAsync(db => db.CaseUpdates.AnyAsync(u => u.CaseId == s.CaseId && u.Body == "Posted while SMTP is down")));
        Assert.Single(await NotesAsync(s.ClientUser.Id, PortalCase(s.CaseId)));
        Assert.Empty(EmailsTo(s.ClientUser.Email!));
    }

    [Fact]
    public async Task InactiveClient_NoEmail()
    {
        var (staff, staffUser) = await NewStaffAsync();
        var s = await _app.NewClientWithCaseAsync(staffUser.Id);
        await _app.WithDbAsync(async db => { (await db.Users.SingleAsync(u => u.Id == s.ClientUser.Id)).IsActive = false; return await db.SaveChangesAsync(); });
        var res = await staff.PostFormAsync(CaseUrl(s.CaseId) + "?handler=Status",
            new Dictionary<string, string> { ["status"] = CaseStatus.Hearing }, CaseUrl(s.CaseId));
        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
        Assert.Single(await NotesAsync(s.ClientUser.Id, PortalCase(s.CaseId)));
        Assert.Empty(EmailsTo(s.ClientUser.Email!));
    }

    [Fact]
    public async Task ClientEmail_HasNoCaseDetails_InUserLanguage()
    {
        var (staff, staffUser) = await NewStaffAsync();
        var s = await _app.NewClientWithCaseAsync(staffUser.Id, title: "Kes Rahsia Ahmad");
        await SetLanguageAsync(s.ClientUser.Id, "ms");
        var res = await staff.PostFormAsync(CaseUrl(s.CaseId) + "?handler=Status",
            new Dictionary<string, string> { ["status"] = CaseStatus.Hearing }, CaseUrl(s.CaseId));
        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);

        var mail = Assert.Single(EmailsTo(s.ClientUser.Email!));
        Assert.Equal("Status kes anda telah berubah", mail.Subject);
        Assert.StartsWith("Salam ", mail.Body);
        Assert.DoesNotContain("Rahsia", mail.Body);
        Assert.DoesNotContain("Rahsia", mail.Subject);
        Assert.DoesNotContain(s.CaseId.ToString(), mail.Subject);
        var n = Assert.Single(await NotesAsync(s.ClientUser.Id, PortalCase(s.CaseId)));
        Assert.StartsWith("Status kes dikemas kini", n.Title);
    }

    // ---- Notification pages ----

    [Fact]
    public async Task Open_MarksRead()
    {
        var (staff, staffUser) = await NewStaffAsync();
        var s = await _app.NewClientWithCaseAsync(staffUser.Id);
        await staff.PostFormAsync(CaseUrl(s.CaseId) + "?handler=Status", new Dictionary<string, string> { ["status"] = CaseStatus.Filed }, CaseUrl(s.CaseId));
        var client = await _app.SignedInAsync(s.ClientUser.Email!);
        Assert.Equal(1, BellCount(await client.GetStringAsync("/Portal")));
        var list = await client.GetStringAsync("/Notifications");
        var n = Assert.Single(await NotesAsync(s.ClientUser.Id, PortalCase(s.CaseId)));
        Assert.Contains($"/Notifications/Open/{n.Id}", list);

        var open = await client.PostFormAsync($"/Notifications/Open/{n.Id}", Array.Empty<KeyValuePair<string, string>>(), ClientTokenPage);
        Assert.Equal(HttpStatusCode.Redirect, open.StatusCode);
        Assert.Equal(PortalCase(s.CaseId), open.Headers.Location!.OriginalString);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(PortalCase(s.CaseId))).StatusCode);
        Assert.Equal(0, BellCount(await client.GetStringAsync("/Portal")));
        Assert.NotNull(Assert.Single(await NotesAsync(s.ClientUser.Id, PortalCase(s.CaseId))).ReadAt);
    }

    [Fact]
    public async Task Open_OthersNotification_404()
    {
        var s = await _app.NewClientWithCaseAsync();
        await _app.WithServiceAsync<INotificationService, int>(async n =>
        { await n.NotifyCaseClientAsync(s.CaseId, NotificationKinds.CaseUpdatePosted, PortalCase(s.CaseId)); return 0; });
        var note = Assert.Single(await NotesAsync(s.ClientUser.Id, PortalCase(s.CaseId)));

        var other = await _app.NewClientWithCaseAsync();
        var otherClient = await _app.SignedInAsync(other.ClientUser.Email!);
        var res = await otherClient.PostFormAsync($"/Notifications/Open/{note.Id}", Array.Empty<KeyValuePair<string, string>>(), ClientTokenPage);
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.DoesNotContain(note.Id.ToString(), await otherClient.GetStringAsync("/Notifications"));
        var (staff, _) = await NewStaffAsync();
        Assert.Equal(HttpStatusCode.NotFound,
            (await staff.PostFormAsync($"/Notifications/Open/{note.Id}", Array.Empty<KeyValuePair<string, string>>(), "/Staff")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await otherClient.GetAsync($"/Notifications/Open/{note.Id}")).StatusCode);
        Assert.Null(Assert.Single(await NotesAsync(s.ClientUser.Id, PortalCase(s.CaseId))).ReadAt);
    }

    [Fact]
    public async Task MarkAllRead()
    {
        var s = await _app.NewClientWithCaseAsync();
        var other = await _app.NewClientWithCaseAsync();
        await _app.WithServiceAsync<INotificationService, int>(async n =>
        {
            await n.NotifyCaseClientAsync(s.CaseId, NotificationKinds.CaseUpdatePosted, PortalCase(s.CaseId));
            await n.NotifyCaseClientAsync(s.CaseId, NotificationKinds.DocumentRequested, PortalCase(s.CaseId));
            await n.NotifyCaseClientAsync(other.CaseId, NotificationKinds.CaseUpdatePosted, PortalCase(other.CaseId));
            return 0;
        });
        var client = await _app.SignedInAsync(s.ClientUser.Email!);
        Assert.Equal(2, BellCount(await client.GetStringAsync("/Portal")));
        var res = await client.PostFormAsync("/Notifications?handler=MarkAll", Array.Empty<KeyValuePair<string, string>>(), ClientTokenPage);
        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
        Assert.Equal(0, BellCount(await client.GetStringAsync("/Portal")));
        Assert.All(await NotesAsync(s.ClientUser.Id, PortalCase(s.CaseId)), n => Assert.NotNull(n.ReadAt));
        Assert.Null(Assert.Single(await NotesAsync(other.ClientUser.Id, PortalCase(other.CaseId))).ReadAt); // others untouched
    }

    [Fact]
    public async Task Notifications_AnonymousRedirectedToLogin_StaffSeesOwnList()
    {
        var anon = _app.NewClient();
        var res = await anon.GetAsync("/Notifications");
        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
        Assert.Contains("/Account/Login", res.Headers.Location!.OriginalString);

        var (staff, staffUser) = await NewStaffAsync();
        var s = await _app.NewClientWithCaseAsync(staffUser.Id);
        await _app.WithServiceAsync<INotificationService, int>(async n =>
        { await n.NotifyAsync(new[] { staffUser.Id }, NotificationKinds.DocumentUploaded, CaseUrl(s.CaseId), "Case X"); return 0; });
        var page = await staff.GetStringAsync("/Notifications");
        Assert.Contains("Case X", page);
        Assert.Contains("Mark all read", page);
    }
}
