using System.Net;
using System.Text.Json;
using ClientHub.Tests.Support;
using ClientHub.Web.Data.Entities;
using ClientHub.Web.Services.Audit;
using ClientHub.Web.Services.Storage;
using Microsoft.EntityFrameworkCore;

namespace ClientHub.Tests.Documents;

/// <summary>
/// Presigned upload, document requests, removal and download, end to end against PostgreSQL and SeaweedFS.
/// Access rules on start, completion, download and removal fail with 404 (R-008, R-046..R-062).
/// </summary>
public class DocumentFlowTests : IClassFixture<FirmAppFactory>
{
    private readonly FirmAppFactory _app;
    public DocumentFlowTests(FirmAppFactory app) => _app = app;

    private record Started(HttpStatusCode Status, string? Url, string? Ticket, string? ContentType, string Body);

    private static async Task<Started> StartAsync(HttpClient http, string tokenPage, Guid caseId, Guid? requestId, string fileName, long size,
        params KeyValuePair<string, string>[] extra)
    {
        var fields = new List<KeyValuePair<string, string>> { new("kind", "Case"), new("caseId", caseId.ToString()), new("fileName", fileName), new("size", size.ToString()) };
        if (requestId is { } r) fields.Add(new("requestId", r.ToString()));
        fields.AddRange(extra);
        var res = await http.PostFormAsync("/Uploads/Start", fields, tokenPage);
        var body = await res.Content.ReadAsStringAsync();
        if (res.StatusCode != HttpStatusCode.OK) return new(res.StatusCode, null, null, null, body);
        var json = JsonDocument.Parse(body).RootElement;
        return new(res.StatusCode, json.GetProperty("url").GetString(), json.GetProperty("ticket").GetString(), json.GetProperty("contentType").GetString(), body);
    }

    private static Task<HttpResponseMessage> CompleteAsync(HttpClient http, string tokenPage, string ticket) =>
        http.PostFormAsync("/Uploads/Complete", new[] { new KeyValuePair<string, string>("ticket", ticket) }, tokenPage);

    /// <summary>Full browser flow; returns the new document id.</summary>
    private async Task<Guid> UploadAsync(HttpClient http, string tokenPage, Guid caseId, Guid? requestId = null, string name = "scan.pdf")
    {
        var s = await StartAsync(http, tokenPage, caseId, requestId, name, 2048);
        Assert.Equal(HttpStatusCode.OK, s.Status);
        Assert.True((await TestStorage.PutPresignedAsync(s.Url!, TestStorage.Pdf(), s.ContentType!)).IsSuccessStatusCode);
        var c = await CompleteAsync(http, tokenPage, s.Ticket!);
        Assert.Equal(HttpStatusCode.OK, c.StatusCode);
        return JsonDocument.Parse(await c.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();
    }

    private async Task<(HttpClient Http, AppUser User)> NewStaffAsync()
    {
        var u = await _app.CreateUserAsync(Roles.Staff);
        return (await _app.SignedInAsync(u.Email!), u);
    }

    private async Task<Guid> AddRequestAsync(Guid caseId, string status = RequestStatus.Pending)
    {
        var r = new DocumentRequest { CaseId = caseId, Name = "IC copy", Status = status };
        await _app.WithDbAsync(async db => { db.DocumentRequests.Add(r); return await db.SaveChangesAsync(); });
        return r.Id;
    }

    private static string Details(Guid caseId) => $"/Staff/Cases/Details/{caseId}";
    private const string ClientTokenPage = "/Account/Manage";

    private async Task<StoredObject?> HeadAsync(string key)
    {
        using var s = new S3StorageService(TestStorage.Options());
        return await s.HeadAsync(key);
    }

    [Fact]
    public async Task Start_NoAccess_404()
    {
        var (staff, _) = await NewStaffAsync();
        var seeded = await _app.NewClientWithCaseAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await StartAsync(staff, "/Staff", seeded.CaseId, null, "a.pdf", 10)).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await StartAsync(staff, "/Staff", Guid.NewGuid(), null, "a.pdf", 10)).Status);

        // Another client's case, even against a real open request there.
        var other = await _app.NewClientWithCaseAsync();
        var otherRequest = await AddRequestAsync(other.CaseId);
        var client = await _app.SignedInAsync(seeded.ClientUser.Email!);
        Assert.Equal(HttpStatusCode.NotFound, (await StartAsync(client, ClientTokenPage, other.CaseId, otherRequest, "a.pdf", 10)).Status);
        // Own case, but a request id that belongs to another case.
        Assert.Equal(HttpStatusCode.NotFound, (await StartAsync(client, ClientTokenPage, seeded.CaseId, otherRequest, "a.pdf", 10)).Status);

        // Anonymous visitors are sent to login.
        var anon = _app.NewClient();
        var res = await anon.PostAsync("/Uploads/Start", new FormUrlEncodedContent(new Dictionary<string, string> { ["caseId"] = seeded.CaseId.ToString() }));
        Assert.NotEqual(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task Start_BadTypeOrTooLarge_400()
    {
        var lawyer = await _app.SignedInAsync("lawyer1@clienthub.test");
        var seeded = await _app.NewClientWithCaseAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await StartAsync(lawyer, "/Staff", seeded.CaseId, null, "virus.exe", 10)).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await StartAsync(lawyer, "/Staff", seeded.CaseId, null, "big.pdf", UploadRules.MaxBytes + 1)).Status);
    }

    [Fact]
    public async Task Key_GeneratedByServer()
    {
        var lawyer = await _app.SignedInAsync("lawyer1@clienthub.test");
        var seeded = await _app.NewClientWithCaseAsync();
        var s = await StartAsync(lawyer, "/Staff", seeded.CaseId, null, "../../other/My Evil Name.pdf", 2048,
            new KeyValuePair<string, string>("key", "cases/somewhere-else/x.pdf"), new KeyValuePair<string, string>("contentType", "text/html"));
        Assert.Equal(HttpStatusCode.OK, s.Status);
        Assert.Equal(UploadRules.Pdf, s.ContentType);
        // The browser only ever sees an unguessable staging key without the file name.
        var path = Uri.UnescapeDataString(new Uri(s.Url!).AbsolutePath);
        Assert.Matches("^/clienthub-test/staging/[0-9a-f-]{36}/[0-9a-f-]{36}$", path);

        Assert.True((await TestStorage.PutPresignedAsync(s.Url!, TestStorage.Pdf(), s.ContentType!)).IsSuccessStatusCode);
        Assert.Equal(HttpStatusCode.OK, (await CompleteAsync(lawyer, "/Staff", s.Ticket!)).StatusCode);
        var doc = await _app.WithDbAsync(db => db.Documents.SingleAsync(d => d.CaseId == seeded.CaseId));
        Assert.Equal("My Evil Name.pdf", doc.FileName);
        Assert.Matches($"^cases/{seeded.CaseId}/[0-9a-f-]{{36}}/My-Evil-Name\\.pdf$", doc.StorageKey);
        Assert.Equal(2048, (await HeadAsync(doc.StorageKey))!.Size);
        Assert.Null(await HeadAsync(path["/clienthub-test/".Length..])); // staging cleaned up
        Assert.Equal(2048, doc.SizeBytes);
        Assert.Equal(UploadRules.Pdf, doc.ContentType);
        Assert.Equal(_app.UserId("lawyer1@clienthub.test"), doc.UploadedById);
    }

    [Fact]
    public async Task Complete_WithRequest_SetsReceivedAndLinks()
    {
        var staffUser = await _app.CreateUserAsync(Roles.Staff);
        var staff = await _app.SignedInAsync(staffUser.Email!);
        var seeded = await _app.NewClientWithCaseAsync(staffUser.Id);

        // Staff adds the request on the case page (R-045).
        var res = await staff.PostFormAsync(Details(seeded.CaseId) + "?handler=Request",
            new Dictionary<string, string> { ["name"] = "Copy of IC", ["note"] = "Both sides" }, Details(seeded.CaseId));
        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
        var request = await _app.WithDbAsync(db => db.DocumentRequests.SingleAsync(r => r.CaseId == seeded.CaseId));
        Assert.Equal(RequestStatus.Pending, request.Status);

        // The client sees it on Home with an upload widget for that request, and uploads.
        var client = await _app.SignedInAsync(seeded.ClientUser.Email!);
        var home = await client.GetStringAsync("/Portal");
        Assert.Contains("Copy of IC", home);
        Assert.Contains($"value=\"{request.Id}\"", home);
        var docId = await UploadAsync(client, "/Portal", seeded.CaseId, request.Id, "ic.jpg");

        request = await _app.WithDbAsync(db => db.DocumentRequests.SingleAsync(r => r.Id == request.Id));
        Assert.Equal(RequestStatus.Received, request.Status);
        Assert.Equal(docId, request.DocumentId);
        var doc = await _app.WithDbAsync(db => db.Documents.SingleAsync(d => d.Id == docId));
        Assert.Equal(UploadRules.Jpeg, doc.ContentType);
        Assert.Equal(seeded.ClientUser.Id, doc.UploadedById);
        Assert.True(await _app.WithDbAsync(db => db.AuditLog.AnyAsync(a => a.Action == AuditActions.DocumentUploaded && a.EntityId == docId && a.UserId == seeded.ClientUser.Id)));

        // Received in both areas; no longer "still needed" on Home.
        Assert.DoesNotContain("Copy of IC", await client.GetStringAsync("/Portal"));
        var portalCase = await client.GetStringAsync($"/Portal/Cases/Details/{seeded.CaseId}");
        Assert.Matches($"data-request=\"{request.Id}\"[\\s\\S]*?Copy of IC[\\s\\S]*?Received", portalCase);
        Assert.DoesNotContain($"name=\"requestId\" value=\"{request.Id}\"", portalCase);
        var page = await staff.GetStringAsync(Details(seeded.CaseId));
        Assert.Matches($"data-request=\"{request.Id}\"[\\s\\S]*?Received", page);
        Assert.Contains($"/Files/Download/{docId}", page);
        Assert.Contains("ic.jpg", page);

        // A received request takes no more uploads.
        Assert.Equal(HttpStatusCode.NotFound, (await StartAsync(client, ClientTokenPage, seeded.CaseId, request.Id, "again.pdf", 10)).Status);
    }

    [Fact]
    public async Task Reupload_AfterReject_LinksNewDocument_FirstStaysActive()
    {
        var lawyer = await _app.SignedInAsync("lawyer1@clienthub.test");
        var seeded = await _app.NewClientWithCaseAsync();
        var requestId = await AddRequestAsync(seeded.CaseId);
        var client = await _app.SignedInAsync(seeded.ClientUser.Email!);
        var first = await UploadAsync(client, ClientTokenPage, seeded.CaseId, requestId);

        var res = await lawyer.PostFormAsync(Details(seeded.CaseId) + "?handler=Reject",
            new Dictionary<string, string> { ["requestId"] = requestId.ToString(), ["reason"] = "Blurry, please rescan" }, Details(seeded.CaseId));
        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);

        var casePage = await client.GetStringAsync($"/Portal/Cases/Details/{seeded.CaseId}");
        Assert.Contains("Blurry, please rescan", casePage);
        Assert.Contains("Re-upload", casePage);
        Assert.Contains("Blurry, please rescan", await client.GetStringAsync("/Portal"));

        var second = await UploadAsync(client, ClientTokenPage, seeded.CaseId, requestId);
        var r = await _app.WithDbAsync(db => db.DocumentRequests.SingleAsync(x => x.Id == requestId));
        Assert.Equal(RequestStatus.Received, r.Status);
        Assert.Equal(second, r.DocumentId);
        Assert.True(await _app.WithDbAsync(db => db.Documents.AnyAsync(d => d.Id == first)));
    }

    [Fact]
    public async Task Complete_Over20MB_DeletesObjectNoRow()
    {
        var lawyer = await _app.SignedInAsync("lawyer1@clienthub.test");
        var seeded = await _app.NewClientWithCaseAsync();
        // The browser claims a small file, then PUTs a larger one: only HeadObject catches it.
        var s = await StartAsync(lawyer, "/Staff", seeded.CaseId, null, "big.pdf", 1000);
        Assert.True((await TestStorage.PutPresignedAsync(s.Url!, TestStorage.Pdf((int)UploadRules.MaxBytes + 1), s.ContentType!)).IsSuccessStatusCode);
        var key = Uri.UnescapeDataString(new Uri(s.Url!).AbsolutePath)["/clienthub-test/".Length..];
        Assert.NotNull(await HeadAsync(key));

        var res = await CompleteAsync(lawyer, "/Staff", s.Ticket!);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("20 MB", await res.Content.ReadAsStringAsync());
        Assert.Null(await HeadAsync(key));
        Assert.False(await _app.WithDbAsync(db => db.Documents.IgnoreQueryFilters().AnyAsync(d => d.CaseId == seeded.CaseId)));
    }

    [Fact]
    public async Task Complete_BadType_DeletesObjectNoRow()
    {
        var lawyer = await _app.SignedInAsync("lawyer1@clienthub.test");
        var seeded = await _app.NewClientWithCaseAsync();
        var s = await StartAsync(lawyer, "/Staff", seeded.CaseId, null, "scan.pdf", 100);
        var key = Uri.UnescapeDataString(new Uri(s.Url!).AbsolutePath)["/clienthub-test/".Length..];
        // Something other than the signed type ends up at the key (e.g. a client bypassing the signed URL).
        await TestStorage.PutRawAsync(key, new byte[] { 0x4D, 0x5A, 0, 0 }, "application/x-msdownload");

        var res = await CompleteAsync(lawyer, "/Staff", s.Ticket!);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Null(await HeadAsync(key));
        Assert.False(await _app.WithDbAsync(db => db.Documents.IgnoreQueryFilters().AnyAsync(d => d.CaseId == seeded.CaseId)));
    }

    [Fact]
    public async Task Complete_NothingUploaded_400_TamperedTicket_400()
    {
        var lawyer = await _app.SignedInAsync("lawyer1@clienthub.test");
        var seeded = await _app.NewClientWithCaseAsync();
        var s = await StartAsync(lawyer, "/Staff", seeded.CaseId, null, "scan.pdf", 100);
        Assert.Equal(HttpStatusCode.BadRequest, (await CompleteAsync(lawyer, "/Staff", s.Ticket!)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await CompleteAsync(lawyer, "/Staff", s.Ticket![..^4] + "AAAA")).StatusCode);
        Assert.False(await _app.WithDbAsync(db => db.Documents.IgnoreQueryFilters().AnyAsync(d => d.CaseId == seeded.CaseId)));
    }

    [Fact]
    public async Task Complete_AccessRevoked_404()
    {
        var (staff, staffUser) = await NewStaffAsync();
        var seeded = await _app.NewClientWithCaseAsync(staffUser.Id);
        var s = await StartAsync(staff, "/Staff", seeded.CaseId, null, "scan.pdf", 2048);
        Assert.Equal(HttpStatusCode.OK, s.Status);
        Assert.True((await TestStorage.PutPresignedAsync(s.Url!, TestStorage.Pdf(), s.ContentType!)).IsSuccessStatusCode);

        await _app.WithDbAsync(async db =>
        {
            db.CaseStaff.RemoveRange(db.CaseStaff.Where(x => x.CaseId == seeded.CaseId));
            return await db.SaveChangesAsync();
        });
        Assert.Equal(HttpStatusCode.NotFound, (await CompleteAsync(staff, "/Staff", s.Ticket!)).StatusCode);
        Assert.False(await _app.WithDbAsync(db => db.Documents.IgnoreQueryFilters().AnyAsync(d => d.CaseId == seeded.CaseId)));
    }

    [Fact]
    public async Task Complete_OtherUsersTicket_404()
    {
        var seeded = await _app.NewClientWithCaseAsync();
        var requestId = await AddRequestAsync(seeded.CaseId);
        var client = await _app.SignedInAsync(seeded.ClientUser.Email!);
        var s = await StartAsync(client, ClientTokenPage, seeded.CaseId, requestId, "scan.pdf", 2048);
        Assert.True((await TestStorage.PutPresignedAsync(s.Url!, TestStorage.Pdf(), s.ContentType!)).IsSuccessStatusCode);

        // Even a Lawyer, who can open the case, cannot complete someone else's ticket.
        var lawyer = await _app.SignedInAsync("lawyer1@clienthub.test");
        Assert.Equal(HttpStatusCode.NotFound, (await CompleteAsync(lawyer, "/Staff", s.Ticket!)).StatusCode);
        Assert.False(await _app.WithDbAsync(db => db.Documents.IgnoreQueryFilters().AnyAsync(d => d.CaseId == seeded.CaseId)));
        Assert.Equal(RequestStatus.Pending, await _app.WithDbAsync(db => db.DocumentRequests.Where(r => r.Id == requestId).Select(r => r.Status).SingleAsync()));

        // The owner can still complete it.
        Assert.Equal(HttpStatusCode.OK, (await CompleteAsync(client, ClientTokenPage, s.Ticket!)).StatusCode);
        // Completing the same ticket twice does not write a second row.
        Assert.Equal(HttpStatusCode.OK, (await CompleteAsync(client, ClientTokenPage, s.Ticket!)).StatusCode);
        Assert.Equal(1, await _app.WithDbAsync(db => db.Documents.CountAsync(d => d.CaseId == seeded.CaseId)));
    }

    [Fact]
    public async Task Client_UploadWithoutRequest_Rejected()
    {
        var seeded = await _app.NewClientWithCaseAsync();
        var client = await _app.SignedInAsync(seeded.ClientUser.Email!);
        Assert.Equal(HttpStatusCode.NotFound, (await StartAsync(client, ClientTokenPage, seeded.CaseId, null, "a.pdf", 10)).Status);
        var received = await AddRequestAsync(seeded.CaseId, RequestStatus.Received);
        Assert.Equal(HttpStatusCode.NotFound, (await StartAsync(client, ClientTokenPage, seeded.CaseId, received, "a.pdf", 10)).Status);
        var rejected = await AddRequestAsync(seeded.CaseId, RequestStatus.Rejected);
        Assert.Equal(HttpStatusCode.OK, (await StartAsync(client, ClientTokenPage, seeded.CaseId, rejected, "a.pdf", 10)).Status);
    }

    [Fact]
    public async Task Reject_WithoutReason_Invalid()
    {
        var lawyer = await _app.SignedInAsync("lawyer1@clienthub.test");
        var seeded = await _app.NewClientWithCaseAsync();
        var requestId = await AddRequestAsync(seeded.CaseId, RequestStatus.Received);
        var res = await lawyer.PostFormAsync(Details(seeded.CaseId) + "?handler=Reject",
            new Dictionary<string, string> { ["requestId"] = requestId.ToString(), ["reason"] = "  " }, Details(seeded.CaseId));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Contains("Give a reason", await res.Content.ReadAsStringAsync());
        Assert.Equal(RequestStatus.Received, await _app.WithDbAsync(db => db.DocumentRequests.Where(r => r.Id == requestId).Select(r => r.Status).SingleAsync()));

        // A Pending request has no file to reject.
        var pending = await AddRequestAsync(seeded.CaseId);
        res = await lawyer.PostFormAsync(Details(seeded.CaseId) + "?handler=Reject",
            new Dictionary<string, string> { ["requestId"] = pending.ToString(), ["reason"] = "x" }, Details(seeded.CaseId));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Staff_UnassignedCase_RequestRejectRemove_404()
    {
        var (staff, _) = await NewStaffAsync();
        var seeded = await _app.NewClientWithCaseAsync();
        var requestId = await AddRequestAsync(seeded.CaseId, RequestStatus.Received);
        var lawyer = await _app.SignedInAsync("lawyer1@clienthub.test");
        var docId = await UploadAsync(lawyer, "/Staff", seeded.CaseId);

        Assert.Equal(HttpStatusCode.NotFound, (await staff.PostFormAsync(Details(seeded.CaseId) + "?handler=Request",
            new Dictionary<string, string> { ["name"] = "x" }, "/Staff")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await staff.PostFormAsync(Details(seeded.CaseId) + "?handler=Reject",
            new Dictionary<string, string> { ["requestId"] = requestId.ToString(), ["reason"] = "x" }, "/Staff")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await staff.PostFormAsync(Details(seeded.CaseId) + "?handler=Remove",
            new Dictionary<string, string> { ["documentId"] = docId.ToString() }, "/Staff")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await staff.GetAsync($"/Files/Download/{docId}")).StatusCode);

        // A document id from another case, through a case the user can open, is 404 too.
        var other = await _app.NewClientWithCaseAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await lawyer.PostFormAsync(Details(other.CaseId) + "?handler=Remove",
            new Dictionary<string, string> { ["documentId"] = docId.ToString() }, "/Staff")).StatusCode);

        Assert.Equal(1, await _app.WithDbAsync(db => db.DocumentRequests.CountAsync(r => r.CaseId == seeded.CaseId)));
        Assert.True(await _app.WithDbAsync(db => db.Documents.AnyAsync(d => d.Id == docId)));
    }

    [Fact]
    public async Task Remove_SetsInactiveAuditObjectStays()
    {
        var staffUser = await _app.CreateUserAsync(Roles.Staff);
        var staff = await _app.SignedInAsync(staffUser.Email!);
        var seeded = await _app.NewClientWithCaseAsync(staffUser.Id);
        var docId = await UploadAsync(staff, "/Staff", seeded.CaseId, name: "contract.pdf");
        var page = await staff.GetStringAsync(Details(seeded.CaseId));
        Assert.Contains("contract.pdf", page);
        Assert.Contains("The file is kept in the records", page);

        var res = await staff.PostFormAsync(Details(seeded.CaseId) + "?handler=Remove",
            new Dictionary<string, string> { ["documentId"] = docId.ToString() }, Details(seeded.CaseId));
        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);

        var doc = await _app.WithDbAsync(db => db.Documents.IgnoreQueryFilters().SingleAsync(d => d.Id == docId));
        Assert.False(doc.IsActive);
        Assert.Equal(staffUser.Id, doc.RemovedById);
        Assert.NotNull(doc.RemovedAt);
        Assert.True(await _app.WithDbAsync(db => db.AuditLog.AnyAsync(a => a.Action == AuditActions.DocumentRemoved && a.EntityId == docId && a.UserId == staffUser.Id)));
        Assert.NotNull(await HeadAsync(doc.StorageKey));
        Assert.DoesNotContain("contract.pdf", await staff.GetStringAsync(Details(seeded.CaseId)));
        var client = await _app.SignedInAsync(seeded.ClientUser.Email!);
        Assert.DoesNotContain("contract.pdf", await client.GetStringAsync($"/Portal/Cases/Details/{seeded.CaseId}?tab=documents"));
    }

    [Fact]
    public async Task Client_Remove_404()
    {
        var seeded = await _app.NewClientWithCaseAsync();
        var lawyer = await _app.SignedInAsync("lawyer1@clienthub.test");
        var docId = await UploadAsync(lawyer, "/Staff", seeded.CaseId);
        var client = await _app.SignedInAsync(seeded.ClientUser.Email!);
        var res = await client.PostFormAsync(Details(seeded.CaseId) + "?handler=Remove",
            new Dictionary<string, string> { ["documentId"] = docId.ToString() }, ClientTokenPage);
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.True(await _app.WithDbAsync(db => db.Documents.AnyAsync(d => d.Id == docId)));
        Assert.DoesNotContain("handler=Remove", await client.GetStringAsync($"/Portal/Cases/Details/{seeded.CaseId}?tab=documents"));
    }

    [Fact]
    public async Task Download_Removed_404()
    {
        var seeded = await _app.NewClientWithCaseAsync();
        var lawyer = await _app.SignedInAsync("lawyer1@clienthub.test");
        var docId = await UploadAsync(lawyer, "/Staff", seeded.CaseId);
        Assert.Equal(HttpStatusCode.Redirect, (await lawyer.GetAsync($"/Files/Download/{docId}")).StatusCode);
        await lawyer.PostFormAsync(Details(seeded.CaseId) + "?handler=Remove", new Dictionary<string, string> { ["documentId"] = docId.ToString() }, "/Staff");
        Assert.Equal(HttpStatusCode.NotFound, (await lawyer.GetAsync($"/Files/Download/{docId}")).StatusCode);
        var client = await _app.SignedInAsync(seeded.ClientUser.Email!);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/Files/Download/{docId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/Files/Download/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task Download_RedirectsAndWritesAudit()
    {
        var seeded = await _app.NewClientWithCaseAsync();
        var lawyer = await _app.SignedInAsync("lawyer1@clienthub.test");
        var docId = await UploadAsync(lawyer, "/Staff", seeded.CaseId, name: "Surat Perjanjian.pdf");

        var client = await _app.SignedInAsync(seeded.ClientUser.Email!);
        Assert.Contains($"/Files/Download/{docId}", await client.GetStringAsync($"/Portal/Cases/Details/{seeded.CaseId}?tab=documents"));
        var res = await client.GetAsync($"/Files/Download/{docId}");
        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
        var url = res.Headers.Location!.ToString();
        Assert.Contains("X-Amz-Expires=300", url);
        Assert.Contains("response-content-disposition", url);
        Assert.True(await _app.WithDbAsync(db => db.AuditLog.AnyAsync(a => a.Action == AuditActions.DocumentDownloaded && a.EntityId == docId && a.UserId == seeded.ClientUser.Id)));

        using var http = new HttpClient();
        var file = await http.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, file.StatusCode);
        Assert.Equal("attachment", file.Content.Headers.ContentDisposition?.DispositionType);

        // Another client cannot download it; no audit row is written for the refusal.
        var other = await _app.NewClientWithCaseAsync();
        var otherClient = await _app.SignedInAsync(other.ClientUser.Email!);
        Assert.Equal(HttpStatusCode.NotFound, (await otherClient.GetAsync($"/Files/Download/{docId}")).StatusCode);
        Assert.False(await _app.WithDbAsync(db => db.AuditLog.AnyAsync(a => a.Action == AuditActions.DocumentDownloaded && a.UserId == other.ClientUser.Id)));
    }

    [Fact]
    public async Task PutUrl_ReusedAfterComplete_DoesNotChangeDocument()
    {
        var lawyer = await _app.SignedInAsync("lawyer1@clienthub.test");
        var seeded = await _app.NewClientWithCaseAsync();
        var s = await StartAsync(lawyer, "/Staff", seeded.CaseId, null, "scan.pdf", 2048);
        Assert.True((await TestStorage.PutPresignedAsync(s.Url!, TestStorage.Pdf(2048), s.ContentType!)).IsSuccessStatusCode);
        Assert.Equal(HttpStatusCode.OK, (await CompleteAsync(lawyer, "/Staff", s.Ticket!)).StatusCode);
        var doc = await _app.WithDbAsync(db => db.Documents.SingleAsync(d => d.CaseId == seeded.CaseId));

        // The URL is still valid for a few minutes: a second, oversized PUT lands in staging only.
        await TestStorage.PutPresignedAsync(s.Url!, TestStorage.Pdf((int)UploadRules.MaxBytes + 10), s.ContentType!);
        Assert.Equal(2048, (await HeadAsync(doc.StorageKey))!.Size);
        // Completing again returns the same document and still does not touch it.
        Assert.Equal(HttpStatusCode.OK, (await CompleteAsync(lawyer, "/Staff", s.Ticket!)).StatusCode);
        Assert.Equal(2048, (await HeadAsync(doc.StorageKey))!.Size);
        Assert.Equal(1, await _app.WithDbAsync(db => db.Documents.CountAsync(d => d.CaseId == seeded.CaseId)));
    }

    [Fact]
    public async Task Complete_Concurrent_ReturnsOneDocument()
    {
        var lawyer = await _app.SignedInAsync("lawyer1@clienthub.test");
        var seeded = await _app.NewClientWithCaseAsync();
        var s = await StartAsync(lawyer, "/Staff", seeded.CaseId, null, "scan.pdf", 2048);
        Assert.True((await TestStorage.PutPresignedAsync(s.Url!, TestStorage.Pdf(), s.ContentType!)).IsSuccessStatusCode);
        var token = AppFactory.Token(await lawyer.GetStringAsync("/Staff"));
        HttpContent Form() => new FormUrlEncodedContent(new Dictionary<string, string> { ["ticket"] = s.Ticket!, ["__RequestVerificationToken"] = token });
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => lawyer.PostAsync("/Uploads/Complete", Form())));
        Assert.All(results, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        var ids = await Task.WhenAll(results.Select(async r => JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid()));
        Assert.Single(ids.Distinct());
        Assert.Equal(1, await _app.WithDbAsync(db => db.Documents.CountAsync(d => d.CaseId == seeded.CaseId)));
    }

    [Fact]
    public async Task Complete_AfterRemoval_404()
    {
        var lawyer = await _app.SignedInAsync("lawyer1@clienthub.test");
        var seeded = await _app.NewClientWithCaseAsync();
        var s = await StartAsync(lawyer, "/Staff", seeded.CaseId, null, "scan.pdf", 2048);
        Assert.True((await TestStorage.PutPresignedAsync(s.Url!, TestStorage.Pdf(), s.ContentType!)).IsSuccessStatusCode);
        var c = await CompleteAsync(lawyer, "/Staff", s.Ticket!);
        var docId = JsonDocument.Parse(await c.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();
        await lawyer.PostFormAsync(Details(seeded.CaseId) + "?handler=Remove", new Dictionary<string, string> { ["documentId"] = docId.ToString() }, "/Staff");
        Assert.Equal(HttpStatusCode.NotFound, (await CompleteAsync(lawyer, "/Staff", s.Ticket!)).StatusCode);
    }
}
