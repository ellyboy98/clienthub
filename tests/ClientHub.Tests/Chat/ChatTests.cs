using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using ClientHub.Tests.Support;
using ClientHub.Web.Data.Entities;
using ClientHub.Web.Services.Audit;
using ClientHub.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClientHub.Tests.Chat;

/// <summary>
/// One chat per client (R-063..R-069, R-073, R-074, R-136, R-146, R-153). Every chat handler, the polling endpoint,
/// chat uploads and chat downloads go through CanAccessClient and return 404 on failure.
/// "Save to case" is cut for v1, so its tests from the plan are not here.
/// </summary>
public class ChatTests : IClassFixture<FirmAppFactory>
{
    private readonly FirmAppFactory _app;
    public ChatTests(FirmAppFactory app) => _app = app;

    private const string ClientTokenPage = "/Account/Manage";
    private static string Thread(Guid clientId) => $"/Staff/Chats/{clientId}";

    private static Task<HttpResponseMessage> StaffSendAsync(HttpClient http, Guid clientId, string body) =>
        http.PostFormAsync(Thread(clientId) + "?handler=Send", new Dictionary<string, string> { ["body"] = body }, "/Staff");

    private static Task<HttpResponseMessage> ClientSendAsync(HttpClient http, string body) =>
        http.PostFormAsync("/Portal/Chat?handler=Send", new Dictionary<string, string> { ["body"] = body }, ClientTokenPage);

    private async Task<(HttpClient Http, AppUser User)> NewStaffAsync()
    {
        var u = await _app.CreateUserAsync(Roles.Staff);
        return (await _app.SignedInAsync(u.Email!), u);
    }

    private Task<List<Message>> MessagesAsync(Guid clientId) =>
        _app.WithDbAsync(db => db.Messages.Where(m => m.ClientId == clientId).OrderBy(m => m.SentAt).ToListAsync());

    private static long Cursor(string html)
    {
        var m = Regex.Match(html, "id=\"chat-poll\"[^>]*hx-get=\"[^\"]*after=(\\d+)\"");
        Assert.True(m.Success, "No polling sentinel.");
        return long.Parse(m.Groups[1].Value);
    }

    private async Task<Guid> UploadChatFileAsync(HttpClient http, string tokenPage, Guid clientId, string name = "photo.png", string? caption = null)
    {
        var res = await CompleteChatUploadAsync(http, tokenPage, clientId, name, caption);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<HttpResponseMessage> CompleteChatUploadAsync(HttpClient http, string tokenPage, Guid clientId, string name, string? caption)
    {
        var start = await http.PostFormAsync("/Uploads/Start", new Dictionary<string, string>
        {
            ["kind"] = "Chat", ["clientId"] = clientId.ToString(), ["fileName"] = name, ["size"] = "2048"
        }, tokenPage);
        Assert.Equal(HttpStatusCode.OK, start.StatusCode);
        var json = JsonDocument.Parse(await start.Content.ReadAsStringAsync()).RootElement;
        var url = json.GetProperty("url").GetString()!;
        Assert.Matches("/staging/", url);
        Assert.True((await TestStorage.PutPresignedAsync(url, TestStorage.Pdf(), json.GetProperty("contentType").GetString()!)).IsSuccessStatusCode);
        var fields = new Dictionary<string, string> { ["ticket"] = json.GetProperty("ticket").GetString()! };
        if (caption is not null) fields["caption"] = caption;
        return await http.PostFormAsync("/Uploads/Complete", fields, tokenPage);
    }

    [Fact]
    public async Task Staff_UnassignedClient_404()
    {
        var (staff, _) = await NewStaffAsync();
        var seeded = await _app.NewClientWithCaseAsync();
        var client = await _app.SignedInAsync(seeded.ClientUser.Email!);
        Assert.True((await ClientSendAsync(client, "hello")).StatusCode is HttpStatusCode.Redirect or HttpStatusCode.OK);

        Assert.Equal(HttpStatusCode.NotFound, (await staff.GetAsync(Thread(seeded.ClientId))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await staff.GetAsync(Thread(seeded.ClientId) + "?handler=New&after=0")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await StaffSendAsync(staff, seeded.ClientId, "hi")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await staff.GetAsync(Thread(Guid.NewGuid()))).StatusCode);

        // No entry in the list and no unread pill in the sidebar.
        var list = await staff.GetStringAsync("/Staff/Chats");
        Assert.DoesNotContain(seeded.ClientId.ToString(), list);
        Assert.DoesNotContain("data-nav-unread", list);
        // Nothing was written or marked read by the refused requests.
        var messages = await MessagesAsync(seeded.ClientId);
        Assert.Single(messages);
        Assert.Null(messages[0].ReadAt);
    }

    [Fact]
    public async Task Staff_AssignedToAnyCase_ReadsAndSends()
    {
        var (staff, staffUser) = await NewStaffAsync();
        var seeded = await _app.NewClientWithCaseAsync(staffUser.Id);

        var list = await staff.GetStringAsync("/Staff/Chats");
        Assert.Contains($"data-chat-item=\"{seeded.ClientId}\"", list);
        Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync(Thread(seeded.ClientId))).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await StaffSendAsync(staff, seeded.ClientId, "We received your IC.")).StatusCode);

        var m = Assert.Single(await MessagesAsync(seeded.ClientId));
        Assert.Equal(staffUser.Id, m.SenderId);
        Assert.Equal("We received your IC.", m.Body);
    }

    [Fact]
    public async Task LawyerAndOfficeManager_SeeEveryChat()
    {
        var seeded = await _app.NewClientWithCaseAsync();
        foreach (var email in new[] { "lawyer2@clienthub.test", "om@clienthub.test" })
        {
            var http = await _app.SignedInAsync(email);
            Assert.Contains($"data-chat-item=\"{seeded.ClientId}\"", await http.GetStringAsync("/Staff/Chats"));
            Assert.Equal(HttpStatusCode.Redirect, (await StaffSendAsync(http, seeded.ClientId, "From " + email)).StatusCode);
        }
        Assert.Equal(2, (await MessagesAsync(seeded.ClientId)).Count);
    }

    [Fact]
    public async Task Client_OtherClientChat_404()
    {
        var a = await _app.NewClientWithCaseAsync();
        var b = await _app.NewClientWithCaseAsync();
        var lawyer = await _app.SignedInAsync("lawyer1@clienthub.test");
        Assert.Equal(HttpStatusCode.Redirect, (await StaffSendAsync(lawyer, b.ClientId, "Secret for B")).StatusCode);
        var bFile = await UploadChatFileAsync(lawyer, "/Staff", b.ClientId, "b-only.png");

        var clientA = await _app.SignedInAsync(a.ClientUser.Email!);
        // The staff route is outside the portal policy; the portal chat only ever shows the caller's own client.
        Assert.Equal(HttpStatusCode.NotFound, (await clientA.GetAsync(Thread(b.ClientId))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await clientA.GetAsync(Thread(b.ClientId) + "?handler=New&after=0")).StatusCode);
        var own = await clientA.GetStringAsync("/Portal/Chat");
        Assert.DoesNotContain("Secret for B", own);
        Assert.DoesNotContain("Secret for B", await clientA.GetStringAsync("/Portal/Chat?handler=New&after=0&clientId=" + b.ClientId));
        // A clientId in the form is ignored: the message lands in A's own chat.
        await clientA.PostFormAsync("/Portal/Chat?handler=Send", new Dictionary<string, string> { ["body"] = "mine", ["clientId"] = b.ClientId.ToString() }, ClientTokenPage);
        Assert.Single(await MessagesAsync(a.ClientId));
        Assert.Equal(2, (await MessagesAsync(b.ClientId)).Count);

        // Chat upload into B's chat and download of B's chat file are 404.
        var start = await clientA.PostFormAsync("/Uploads/Start", new Dictionary<string, string>
        {
            ["kind"] = "Chat", ["clientId"] = b.ClientId.ToString(), ["fileName"] = "x.pdf", ["size"] = "10"
        }, ClientTokenPage);
        Assert.Equal(HttpStatusCode.NotFound, start.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await clientA.GetAsync($"/Files/Download/{bFile}?handler=Message")).StatusCode);
    }

    [Fact]
    public async Task PollingEndpoint_ReturnsOnlyNewMessages_EscapesText()
    {
        var seeded = await _app.NewClientWithCaseAsync();
        var client = await _app.SignedInAsync(seeded.ClientUser.Email!);
        var lawyer = await _app.SignedInAsync("lawyer1@clienthub.test");
        Assert.Equal(HttpStatusCode.Redirect, (await StaffSendAsync(lawyer, seeded.ClientId, "First")).StatusCode);

        var page = await client.GetStringAsync("/Portal/Chat");
        Assert.Contains("First", page);
        Assert.Contains("hx-trigger=\"every 5s", page);
        var cursor = Cursor(page);

        var nothing = await client.GetStringAsync($"/Portal/Chat?handler=New&after={cursor}");
        Assert.DoesNotContain("data-message", nothing);
        Assert.Equal(cursor, Cursor(nothing));

        Assert.Equal(HttpStatusCode.Redirect, (await StaffSendAsync(lawyer, seeded.ClientId, "<script>alert(1)</script>")).StatusCode);
        var fresh = await client.GetStringAsync($"/Portal/Chat?handler=New&after={cursor}");
        Assert.DoesNotContain("First", fresh);
        Assert.DoesNotContain("<script>alert(1)</script>", fresh);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", fresh);
        Assert.Single(Regex.Matches(fresh, "data-message="));
        Assert.True(Cursor(fresh) > cursor);
        // Firm sender's name shows on the client's side.
        Assert.Contains("data-sender", fresh);

        // Staff poll of the same chat, assigned vs not.
        var (staff, staffUser) = await NewStaffAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await staff.GetAsync(Thread(seeded.ClientId) + "?handler=New&after=0")).StatusCode);
        await _app.WithDbAsync(async db => { db.CaseStaff.Add(new CaseStaff { CaseId = seeded.CaseId, UserId = staffUser.Id }); return await db.SaveChangesAsync(); });
        var all = await staff.GetStringAsync(Thread(seeded.ClientId) + "?handler=New&after=0");
        Assert.Equal(2, Regex.Matches(all, "data-message=").Count);
    }

    [Fact]
    public async Task Send_Htmx_TriggersRefresh_EmptyAndTooLongRefused()
    {
        var seeded = await _app.NewClientWithCaseAsync();
        var client = await _app.SignedInAsync(seeded.ClientUser.Email!);
        client.DefaultRequestHeaders.Add("HX-Request", "true");

        var ok = await ClientSendAsync(client, "  Line one\nLine two  ");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal("chat:refresh", ok.Headers.GetValues("HX-Trigger").Single());
        Assert.Equal("", await ok.Content.ReadAsStringAsync());

        var empty = await ClientSendAsync(client, "   ");
        Assert.Equal(HttpStatusCode.OK, empty.StatusCode);
        Assert.NotEqual("", await empty.Content.ReadAsStringAsync());
        Assert.False(empty.Headers.Contains("HX-Trigger"));
        Assert.Equal(HttpStatusCode.OK, (await ClientSendAsync(client, new string('a', 4001))).StatusCode);

        var m = Assert.Single(await MessagesAsync(seeded.ClientId));
        Assert.Equal("Line one\nLine two", m.Body);
        Assert.Equal(seeded.ClientUser.Id, m.SenderId);
    }

    [Fact]
    public async Task OpenChat_MarksOtherSideRead()
    {
        var (staff, staffUser) = await NewStaffAsync();
        var seeded = await _app.NewClientWithCaseAsync(staffUser.Id);
        var client = await _app.SignedInAsync(seeded.ClientUser.Email!);
        Assert.Equal(HttpStatusCode.Redirect, (await ClientSendAsync(client, "Any news?")).StatusCode);

        // Unread pill in the sidebar and the list before opening.
        var before = await staff.GetStringAsync("/Staff/Chats");
        Assert.Contains("data-nav-unread=\"1\"", before);
        Assert.Contains("data-unread=\"1\"", before);
        Assert.Equal(1, TestData.Tile(await staff.GetStringAsync("/Staff"), "chats"));

        // Staff opens the thread: the client's message is read; the pill clears on that same page.
        var thread = await staff.GetStringAsync(Thread(seeded.ClientId));
        Assert.Contains("Any news?", thread);
        Assert.DoesNotContain("data-nav-unread", thread);
        Assert.DoesNotContain("data-unread=", thread);
        Assert.NotNull(Assert.Single(await MessagesAsync(seeded.ClientId)).ReadAt);

        // Staff replies; opening the staff side again does not mark the firm's own reply read.
        Assert.Equal(HttpStatusCode.Redirect, (await StaffSendAsync(staff, seeded.ClientId, "Filed today.")).StatusCode);
        await staff.GetStringAsync(Thread(seeded.ClientId));
        var reply = (await MessagesAsync(seeded.ClientId)).Single(m => m.SenderId == staffUser.Id);
        Assert.Null(reply.ReadAt);
        Assert.Equal(1, TestData.Tile(await client.GetStringAsync("/Portal"), "messages"));

        // The client opens the chat: the firm's reply is read.
        await client.GetStringAsync("/Portal/Chat");
        reply = (await MessagesAsync(seeded.ClientId)).Single(m => m.SenderId == staffUser.Id);
        Assert.NotNull(reply.ReadAt);
        Assert.Equal(0, TestData.Tile(await client.GetStringAsync("/Portal"), "messages"));
    }

    [Fact]
    public async Task AttachmentKey_UnderChatsClientId()
    {
        var seeded = await _app.NewClientWithCaseAsync();
        var client = await _app.SignedInAsync(seeded.ClientUser.Email!);
        var id = await UploadChatFileAsync(client, "/Portal/Chat", seeded.ClientId, "My Photo.png");

        var m = await _app.WithDbAsync(db => db.Messages.SingleAsync(x => x.Id == id));
        Assert.Equal(seeded.ClientId, m.ClientId);
        Assert.Equal(seeded.ClientUser.Id, m.SenderId);
        Assert.Matches($"^chats/{seeded.ClientId}/[0-9a-f-]{{36}}/My-Photo\\.png$", m.AttachmentKey);
        Assert.Equal("My Photo.png", m.AttachmentName);
        Assert.Equal(2048, m.AttachmentSizeBytes);
        // R-073: a chat file is not a case document.
        Assert.False(await _app.WithDbAsync(db => db.Documents.IgnoreQueryFilters().AnyAsync(d => d.CaseId == seeded.CaseId)));
        Assert.True(await _app.WithDbAsync(db => db.AuditLog.AnyAsync(a => a.Action == AuditActions.MessageFileUploaded && a.EntityId == id)));

        // The file bubble (name, size, download link) shows in the staff thread.
        var lawyer = await _app.SignedInAsync("lawyer1@clienthub.test");
        var thread = await lawyer.GetStringAsync(Thread(seeded.ClientId));
        Assert.Contains($"/Files/Download/{id}?handler=Message", thread);
        Assert.Contains("My Photo.png", thread);
        Assert.Contains("2 KB", thread);
    }

    [Fact]
    public async Task ChatFile_Download_WritesAudit()
    {
        var (staff, staffUser) = await NewStaffAsync();
        var seeded = await _app.NewClientWithCaseAsync(staffUser.Id);
        var id = await UploadChatFileAsync(staff, "/Staff", seeded.ClientId, "letter.png");

        var client = await _app.SignedInAsync(seeded.ClientUser.Email!);
        var res = await client.GetAsync($"/Files/Download/{id}?handler=Message");
        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
        var url = res.Headers.Location!.ToString();
        Assert.Contains("letter.png", Uri.UnescapeDataString(url));
        Assert.Contains("X-Amz-Expires=300", url);
        Assert.True(await _app.WithDbAsync(db => db.AuditLog.AnyAsync(a =>
            a.Action == AuditActions.MessageFileDownloaded && a.EntityId == id && a.UserId == seeded.ClientUser.Id)));

        // Staff no longer assigned, an unknown id, and a text message id are all 404.
        await _app.WithDbAsync(async db => { db.CaseStaff.RemoveRange(db.CaseStaff.Where(s => s.UserId == staffUser.Id)); return await db.SaveChangesAsync(); });
        Assert.Equal(HttpStatusCode.NotFound, (await staff.GetAsync($"/Files/Download/{id}?handler=Message")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/Files/Download/{Guid.NewGuid()}?handler=Message")).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await ClientSendAsync(client, "text only")).StatusCode);
        var textId = (await MessagesAsync(seeded.ClientId)).Single(m => m.AttachmentKey == null).Id;
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/Files/Download/{textId}?handler=Message")).StatusCode);
    }

    [Fact]
    public async Task ChatUpload_TicketForOtherClient_Refused()
    {
        var a = await _app.NewClientWithCaseAsync();
        var lawyer = await _app.SignedInAsync("lawyer1@clienthub.test");
        var start = await lawyer.PostFormAsync("/Uploads/Start", new Dictionary<string, string>
        {
            ["kind"] = "Chat", ["clientId"] = a.ClientId.ToString(), ["fileName"] = "a.png", ["size"] = "2048"
        }, "/Staff");
        var ticket = JsonDocument.Parse(await start.Content.ReadAsStringAsync()).RootElement.GetProperty("ticket").GetString()!;

        // Another user cannot complete it (ticket bound to the starter), and bad types are refused at start.
        var clientA = await _app.SignedInAsync(a.ClientUser.Email!);
        Assert.Equal(HttpStatusCode.NotFound, (await clientA.PostFormAsync("/Uploads/Complete",
            new Dictionary<string, string> { ["ticket"] = ticket }, ClientTokenPage)).StatusCode);
        var bad = await clientA.PostFormAsync("/Uploads/Start", new Dictionary<string, string>
        {
            ["kind"] = "Chat", ["clientId"] = a.ClientId.ToString(), ["fileName"] = "run.exe", ["size"] = "10"
        }, ClientTokenPage);
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Empty(await MessagesAsync(a.ClientId));
    }

    [Fact]
    public async Task Poll_LateCommittedEarlierStamp_StillShown_HiddenPollLeavesUnread()
    {
        var seeded = await _app.NewClientWithCaseAsync();
        var lawyer = await _app.SignedInAsync("lawyer1@clienthub.test");
        var client = await _app.SignedInAsync(seeded.ClientUser.Email!);
        Assert.Equal(HttpStatusCode.Redirect, (await ClientSendAsync(client, "First")).StatusCode);
        var cursor = Cursor(await lawyer.GetStringAsync(Thread(seeded.ClientId)));

        // A message stamped an hour before the one already shown (clock skew / late commit) still has a higher seq.
        await _app.WithDbAsync(async db =>
        {
            db.Messages.Add(new Message { ClientId = seeded.ClientId, SenderId = seeded.ClientUser.Id, Body = "Late one", SentAt = DateTime.UtcNow.AddHours(-1) });
            return await db.SaveChangesAsync();
        });

        // Background tab: fetched, not marked read.
        var hidden = await lawyer.GetStringAsync(Thread(seeded.ClientId) + $"?handler=New&after={cursor}&visible=0");
        Assert.Contains("Late one", hidden);
        Assert.Null((await MessagesAsync(seeded.ClientId)).Single(m => m.Body == "Late one").ReadAt);

        // Visible again: a poll from the new cursor returns nothing new and marks what is on screen read.
        var next = Cursor(hidden);
        Assert.True(next > cursor);
        Assert.DoesNotContain("data-message", await lawyer.GetStringAsync(Thread(seeded.ClientId) + $"?handler=New&after={next}&visible=1"));
        Assert.NotNull((await MessagesAsync(seeded.ClientId)).Single(m => m.Body == "Late one").ReadAt);
    }

    [Fact]
    public async Task Poll_UncommittedInsert_NotSkippedNorMarkedRead()
    {
        var seeded = await _app.NewClientWithCaseAsync();
        var lawyer = await _app.SignedInAsync("lawyer1@clienthub.test");
        var client = await _app.SignedInAsync(seeded.ClientUser.Email!);
        Assert.Equal(HttpStatusCode.Redirect, (await StaffSendAsync(lawyer, seeded.ClientId, "Hello")).StatusCode);
        var cursor = Cursor(await client.GetStringAsync("/Portal/Chat"));

        // A slow insert into this chat: holds the per-chat lock and an uncommitted row (lower seq) like ChatService does.
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var tx = await db.Database.BeginTransactionAsync();
        var key = BitConverter.ToInt64(seeded.ClientId.ToByteArray(), 0);
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({key})");
        db.Messages.Add(new Message { ClientId = seeded.ClientId, SenderId = _app.UserId("lawyer2@clienthub.test"), Body = "Slow one", SentAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        // A second send into the same chat waits for the lock instead of committing a higher seq first.
        var fast = StaffSendAsync(lawyer, seeded.ClientId, "Fast one");
        await Task.Delay(700);
        Assert.False(fast.IsCompleted);
        // A visible poll meanwhile sees neither and moves no cursor past them.
        var during = await client.GetStringAsync($"/Portal/Chat?handler=New&after={cursor}&visible=1");
        Assert.DoesNotContain("data-message", during);
        Assert.Equal(cursor, Cursor(during));

        await tx.CommitAsync();
        Assert.Equal(HttpStatusCode.Redirect, (await fast).StatusCode);
        var rows = await MessagesAsync(seeded.ClientId);
        Assert.True(rows.Single(m => m.Body == "Slow one").Seq < rows.Single(m => m.Body == "Fast one").Seq);
        Assert.Null(rows.Single(m => m.Body == "Slow one").ReadAt);

        var after = await client.GetStringAsync($"/Portal/Chat?handler=New&after={cursor}&visible=1");
        Assert.True(after.IndexOf("Slow one", StringComparison.Ordinal) is var i && i >= 0 && i < after.IndexOf("Fast one", StringComparison.Ordinal));
        Assert.All(await MessagesAsync(seeded.ClientId), m => Assert.NotNull(m.ReadAt));
    }

    [Fact]
    public async Task ConcurrentSends_AllShownInSeqOrder()
    {
        var seeded = await _app.NewClientWithCaseAsync();
        var lawyer = await _app.SignedInAsync("lawyer1@clienthub.test");
        var client = await _app.SignedInAsync(seeded.ClientUser.Email!);
        var sends = Enumerable.Range(0, 10).Select(i => i % 2 == 0
            ? ClientSendAsync(client, "c" + i)
            : StaffSendAsync(lawyer, seeded.ClientId, "f" + i));
        foreach (var res in await Task.WhenAll(sends)) Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);

        var rows = await _app.WithDbAsync(db => db.Messages.Where(m => m.ClientId == seeded.ClientId).OrderBy(m => m.Seq).ToListAsync());
        Assert.Equal(10, rows.Select(m => m.Seq).Distinct().Count());
        var all = await lawyer.GetStringAsync(Thread(seeded.ClientId) + "?handler=New&after=0&visible=0");
        Assert.Equal(rows.Select(m => m.Id.ToString()), Regex.Matches(all, "data-message=\"([^\"]+)\"").Select(m => m.Groups[1].Value));
    }

    [Fact]
    public async Task ChatFile_CarriesCaption_TooLongRefused()
    {
        var seeded = await _app.NewClientWithCaseAsync();
        var client = await _app.SignedInAsync(seeded.ClientUser.Email!);
        var id = await UploadChatFileAsync(client, "/Portal/Chat", seeded.ClientId, "receipt.png", "  Receipt for the filing fee  ");
        var m = await _app.WithDbAsync(db => db.Messages.SingleAsync(x => x.Id == id));
        Assert.Equal("Receipt for the filing fee", m.Body);
        Assert.Equal("receipt.png", m.AttachmentName);

        var tooLong = await CompleteChatUploadAsync(client, "/Portal/Chat", seeded.ClientId, "b.png", new string('x', 4001));
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.Single(await MessagesAsync(seeded.ClientId));

        var lawyer = await _app.SignedInAsync("lawyer1@clienthub.test");
        var thread = await lawyer.GetStringAsync(Thread(seeded.ClientId));
        Assert.Contains("Receipt for the filing fee", thread);
        Assert.Contains($"/Files/Download/{id}?handler=Message", thread);
    }
}
