using System.Net;
using ClientHub.Tests.Support;
using ClientHub.Web.Data.Entities;
using ClientHub.Web.Services.Notifications;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClientHub.Tests.Notifications;

/// <summary>The real Notify service, except that NotifyAsync always throws (as a DB failure after commit would).</summary>
public class ThrowingNotifyAppFactory : FirmAppFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(s =>
        {
            s.AddScoped<NotificationService>();
            s.AddScoped<INotificationService>(sp => new ThrowingNotificationService(sp.GetRequiredService<NotificationService>()));
        });
    }
}

public class ThrowingNotificationService : INotificationService
{
    private readonly NotificationService _inner;
    public ThrowingNotificationService(NotificationService inner) => _inner = inner;

    public Task NotifyAsync(IEnumerable<Guid> userIds, string kind, string linkUrl, string context, CancellationToken ct = default) =>
        throw new InvalidOperationException("notifications table unavailable (test)");
    public Task NotifyCaseClientAsync(Guid caseId, string kind, string linkUrl, CancellationToken ct = default) =>
        throw new OperationCanceledException("request aborted (test)");
    public Task AfterCommitAsync(string kind, Func<CancellationToken, Task> work) => _inner.AfterCommitAsync(kind, work);
    public Task<int> UnreadCountAsync(Guid userId, CancellationToken ct = default) => _inner.UnreadCountAsync(userId, ct);
    public Task<List<Notification>> ListAsync(Guid userId, int skip, int take, CancellationToken ct = default) => _inner.ListAsync(userId, skip, take, ct);
    public Task<string?> OpenAsync(Guid userId, Guid notificationId, CancellationToken ct = default) => _inner.OpenAsync(userId, notificationId, ct);
    public Task MarkAllReadAsync(Guid userId, CancellationToken ct = default) => _inner.MarkAllReadAsync(userId, ct);
}

/// <summary>Review Should fix 1: once the action is saved, a failing Notify step never reaches the user.</summary>
public class NotifyFailureTests : IClassFixture<ThrowingNotifyAppFactory>
{
    private readonly ThrowingNotifyAppFactory _app;
    public NotifyFailureTests(ThrowingNotifyAppFactory app) => _app = app;

    [Fact]
    public async Task NotifyThrows_ChatSend_SucceedsAndSavesOneMessage()
    {
        var s = await _app.NewClientWithCaseAsync();
        var client = await _app.SignedInAsync(s.ClientUser.Email!);
        var res = await client.PostFormAsync("/Portal/Chat?handler=Send", new Dictionary<string, string> { ["body"] = "only once" }, "/Account/Manage");
        Assert.True(res.IsSuccessStatusCode || res.StatusCode == HttpStatusCode.Redirect, $"Got {(int)res.StatusCode}");
        Assert.Equal(1, await _app.WithDbAsync(db => db.Messages.CountAsync(m => m.ClientId == s.ClientId && m.Body == "only once")));
        Assert.Equal(0, await _app.WithDbAsync(db => db.Notifications.CountAsync(n => n.LinkUrl == $"/Staff/Chats/{s.ClientId}")));
    }

    [Fact]
    public async Task NotifyThrows_StatusChange_Succeeds()
    {
        var s = await _app.NewClientWithCaseAsync();
        var lawyer = await _app.SignedInAsync("lawyer1@clienthub.test");
        var url = $"/Staff/Cases/Details/{s.CaseId}";
        var res = await lawyer.PostFormAsync(url + "?handler=Status", new Dictionary<string, string> { ["status"] = CaseStatus.Filed }, url);
        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
        Assert.Equal(CaseStatus.Filed, await _app.WithDbAsync(db => db.Cases.Where(c => c.Id == s.CaseId).Select(c => c.Status).SingleAsync()));
    }
}
