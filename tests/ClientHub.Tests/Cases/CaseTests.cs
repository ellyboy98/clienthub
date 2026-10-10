using ClientHub.Tests.Support;
using ClientHub.Web.Data;
using ClientHub.Web.Data.Entities;
using ClientHub.Web.Services.Audit;
using ClientHub.Web.Services.Cases;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClientHub.Tests.Cases;

/// <summary>Case status rules (R-035, R-037..R-039, Q-09) and audit rows (R-044, Q-17).</summary>
public class CaseTests : IClassFixture<FirmAppFactory>
{
    private readonly FirmAppFactory _app;
    public CaseTests(FirmAppFactory app) => _app = app;

    private static Case NewCase(string status = CaseStatus.New) => new() { Title = "T", Status = status, OpenedAt = DateTime.UtcNow };

    [Fact]
    public async Task Create_StatusNew_OpenedAtSet()
    {
        var seeded = await _app.NewClientWithCaseAsync();
        var lawyer = _app.UserId("lawyer1@clienthub.test");
        var staff = _app.UserId("staff@clienthub.test");
        var before = DateTime.UtcNow.AddSeconds(-1);

        var (c, error) = await _app.WithServiceAsync<ICaseService, (Case?, CaseError)>(s =>
            s.CreateAsync(new CaseInput(seeded.ClientId, " New matter ", CaseType.Criminal, lawyer, null, new[] { staff })));

        Assert.Equal(CaseError.None, error);
        var saved = await _app.WithDbAsync(db => db.Cases.Include(x => x.StaffLinks).SingleAsync(x => x.Id == c!.Id));
        Assert.Equal(CaseStatus.New, saved.Status);
        Assert.True(saved.OpenedAt >= before && saved.OpenedAt <= DateTime.UtcNow.AddSeconds(1));
        Assert.Null(saved.ClosedAt);
        Assert.Equal("New matter", saved.Title);
        Assert.Equal(staff, Assert.Single(saved.StaffLinks).UserId);
    }

    [Fact]
    public async Task Create_InactiveOrNonLawyer_Rejected()
    {
        var seeded = await _app.NewClientWithCaseAsync();
        var inactiveLawyer = await _app.CreateUserAsync(Roles.Lawyer, active: false);
        var staff = _app.UserId("staff@clienthub.test");

        var (_, e1) = await _app.WithServiceAsync<ICaseService, (Case?, CaseError)>(s =>
            s.CreateAsync(new CaseInput(seeded.ClientId, "X", CaseType.Civil, inactiveLawyer.Id, null, Array.Empty<Guid>())));
        var (_, e2) = await _app.WithServiceAsync<ICaseService, (Case?, CaseError)>(s =>
            s.CreateAsync(new CaseInput(seeded.ClientId, "X", CaseType.Civil, staff, null, Array.Empty<Guid>())));
        Assert.Equal(CaseError.InvalidLawyer, e1);
        Assert.Equal(CaseError.InvalidLawyer, e2);
        var lawyers = await _app.WithServiceAsync<ICaseService, List<AppUser>>(s => s.LawyerChoicesAsync());
        Assert.DoesNotContain(lawyers, l => l.Id == inactiveLawyer.Id);
    }

    [Fact]
    public void SetCompleted_SetsClosedAt()
    {
        var c = NewCase(CaseStatus.Hearing);
        var now = new DateTime(2026, 10, 10, 1, 0, 0, DateTimeKind.Utc);
        c.SetStatus(CaseStatus.Completed, now);
        Assert.Equal(CaseStatus.Completed, c.Status);
        Assert.Equal(now, c.ClosedAt);
        Assert.True(c.IsClosed);
    }

    [Theory]
    [InlineData(CaseStatus.New)]
    [InlineData(CaseStatus.DocumentsCollected)]
    [InlineData(CaseStatus.Filed)]
    [InlineData(CaseStatus.Hearing)]
    [InlineData(CaseStatus.Completed)]
    public void SetCancelled_FromAnyStage(string from)
    {
        var c = NewCase(from);
        c.SetStatus(CaseStatus.Cancelled, DateTime.UtcNow);
        Assert.Equal(CaseStatus.Cancelled, c.Status);
        Assert.NotNull(c.ClosedAt);
    }

    [Fact]
    public void MovesForwardAndBackward()
    {
        var c = NewCase();
        c.SetStatus(CaseStatus.Hearing, DateTime.UtcNow);
        c.SetStatus(CaseStatus.DocumentsCollected, DateTime.UtcNow);
        Assert.Equal(CaseStatus.DocumentsCollected, c.Status);
        Assert.Null(c.ClosedAt);
        Assert.Throws<ArgumentException>(() => c.SetStatus("Archived", DateTime.UtcNow));
    }

    [Fact]
    public void Reopen_ClearsClosedAt()
    {
        var c = NewCase(CaseStatus.Filed);
        c.SetStatus(CaseStatus.Completed, DateTime.UtcNow);
        c.SetStatus(CaseStatus.Hearing, DateTime.UtcNow);
        Assert.Null(c.ClosedAt);
        Assert.False(c.IsClosed);
    }

    [Fact]
    public void CompletedToCancelled_KeepsClosedAt()
    {
        var c = NewCase(CaseStatus.Hearing);
        var first = DateTime.UtcNow.AddDays(-2);
        c.SetStatus(CaseStatus.Completed, first);
        c.SetStatus(CaseStatus.Cancelled, DateTime.UtcNow);
        Assert.Equal(first, c.ClosedAt);
    }

    [Fact]
    public async Task StatusChange_WritesAudit()
    {
        var seeded = await _app.NewClientWithCaseAsync();
        var actor = _app.UserId("om@clienthub.test");
        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var c = await db.Cases.SingleAsync(x => x.Id == seeded.CaseId);
            await scope.ServiceProvider.GetRequiredService<ICaseService>().ChangeStatusAsync(c, CaseStatus.Filed, actor);
        }
        var row = await _app.WithDbAsync(db => db.AuditLog.SingleAsync(a => a.Action == AuditActions.CaseStatusChanged && a.EntityId == seeded.CaseId));
        Assert.Equal(actor, row.UserId);
        Assert.Equal("Case", row.Entity);
        Assert.Contains("\"to\": \"Filed\"", row.Details);
        Assert.Contains("\"from\": \"New\"", row.Details);
    }

    [Fact]
    public async Task PostUpdate_WritesAudit()
    {
        var seeded = await _app.NewClientWithCaseAsync();
        var actor = _app.UserId("staff@clienthub.test");
        var update = await _app.WithServiceAsync<ICaseService, CaseUpdate>(s => s.PostUpdateAsync(seeded.CaseId, actor, "  Filed at court.  "));

        var saved = await _app.WithDbAsync(db => db.CaseUpdates.SingleAsync(u => u.Id == update.Id));
        Assert.Equal("Filed at court.", saved.Body);
        Assert.True(saved.PostedAt > DateTime.UtcNow.AddMinutes(-1));
        var row = await _app.WithDbAsync(db => db.AuditLog.SingleAsync(a => a.Action == AuditActions.CaseUpdatePosted && a.EntityId == update.Id));
        Assert.Equal(actor, row.UserId);
    }
}
