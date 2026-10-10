using System.Security.Claims;
using ClientHub.Tests.Support;
using ClientHub.Web.Data;
using ClientHub.Web.Data.Entities;
using ClientHub.Web.Services.Access;
using Microsoft.EntityFrameworkCore;

namespace ClientHub.Tests.Access;

public class AccessServiceFixture : IAsyncLifetime
{
    public TestDatabase Db { get; } = new();

    public AppUser Lawyer = null!, OfficeManager = null!, StaffAssigned = null!, StaffUnassigned = null!, ClientUserA = null!, ClientUserB = null!;
    public Client ClientA = null!, ClientB = null!;
    public Case CaseA = null!, CaseB = null!;

    public AppDbContext NewContext() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql(Db.ConnectionString).UseSnakeCaseNamingConvention().Options);

    public async Task InitializeAsync()
    {
        await Db.CreateAsync();
        await using var db = NewContext();
        await db.Database.MigrateAsync();

        AppUser U(string role) => new() { UserName = Guid.NewGuid().ToString("N"), Email = Guid.NewGuid().ToString("N") + "@t.test", Role = role, DisplayName = role };
        Lawyer = U(Roles.Lawyer); OfficeManager = U(Roles.OfficeManager);
        StaffAssigned = U(Roles.Staff); StaffUnassigned = U(Roles.Staff);
        ClientUserA = U(Roles.Client); ClientUserB = U(Roles.Client);
        db.Users.AddRange(Lawyer, OfficeManager, StaffAssigned, StaffUnassigned, ClientUserA, ClientUserB);

        ClientA = new Client { Name = "A", UserId = ClientUserA.Id };
        ClientB = new Client { Name = "B", UserId = ClientUserB.Id };
        db.Clients.AddRange(ClientA, ClientB);
        CaseA = new Case { ClientId = ClientA.Id, Title = "Case A", LawyerId = Lawyer.Id, OpenedAt = DateTime.UtcNow };
        CaseB = new Case { ClientId = ClientB.Id, Title = "Case B", LawyerId = Lawyer.Id, OpenedAt = DateTime.UtcNow };
        db.Cases.AddRange(CaseA, CaseB);
        db.CaseStaff.Add(new CaseStaff { CaseId = CaseA.Id, UserId = StaffAssigned.Id });
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await Db.DisposeAsync();
}

public class AccessServiceTests : IClassFixture<AccessServiceFixture>
{
    private readonly AccessServiceFixture _f;
    public AccessServiceTests(AccessServiceFixture f) => _f = f;

    private static ClaimsPrincipal As(AppUser u) => new(new ClaimsIdentity(new[]
    {
        new Claim(ClaimTypes.NameIdentifier, u.Id.ToString()),
        new Claim(ClaimTypes.Role, u.Role)
    }, "Test"));

    private async Task<bool> Case(AppUser u, Case c)
    {
        await using var db = _f.NewContext();
        return await new AccessService(db).CanAccessCaseAsync(As(u), c.Id);
    }

    private async Task<bool> Client(AppUser u, Client c)
    {
        await using var db = _f.NewContext();
        return await new AccessService(db).CanAccessClientAsync(As(u), c.Id);
    }

    [Fact] public async Task Lawyer_AnyCase_True() { Assert.True(await Case(_f.Lawyer, _f.CaseA)); Assert.True(await Case(_f.Lawyer, _f.CaseB)); }
    [Fact] public async Task OfficeManager_AnyCase_True() { Assert.True(await Case(_f.OfficeManager, _f.CaseA)); Assert.True(await Case(_f.OfficeManager, _f.CaseB)); }
    [Fact] public async Task Staff_Assigned_True() => Assert.True(await Case(_f.StaffAssigned, _f.CaseA));
    [Fact] public async Task Staff_Unassigned_False() { Assert.False(await Case(_f.StaffAssigned, _f.CaseB)); Assert.False(await Case(_f.StaffUnassigned, _f.CaseA)); }
    [Fact] public async Task Client_OwnCase_True() => Assert.True(await Case(_f.ClientUserA, _f.CaseA));
    [Fact] public async Task Client_OtherClientCase_False() => Assert.False(await Case(_f.ClientUserA, _f.CaseB));
    [Fact] public async Task CanAccessClient_StaffAssignedToOneCase_True() => Assert.True(await Client(_f.StaffAssigned, _f.ClientA));
    [Fact] public async Task CanAccessClient_StaffNoCase_False() { Assert.False(await Client(_f.StaffAssigned, _f.ClientB)); Assert.False(await Client(_f.StaffUnassigned, _f.ClientA)); }
    [Fact] public async Task CanAccessClient_ClientOwnOnly() { Assert.True(await Client(_f.ClientUserA, _f.ClientA)); Assert.False(await Client(_f.ClientUserA, _f.ClientB)); }
    [Fact] public async Task CanAccessClient_LawyerAndOfficeManager_All() { Assert.True(await Client(_f.Lawyer, _f.ClientB)); Assert.True(await Client(_f.OfficeManager, _f.ClientB)); }

    [Fact]
    public async Task MissingCase_False()
    {
        await using var db = _f.NewContext();
        Assert.False(await new AccessService(db).CanAccessCaseAsync(As(_f.Lawyer), Guid.NewGuid()));
    }

    [Fact]
    public async Task Anonymous_NothingVisible()
    {
        await using var db = _f.NewContext();
        var svc = new AccessService(db);
        var anon = new ClaimsPrincipal(new ClaimsIdentity());
        Assert.False(await svc.CanAccessCaseAsync(anon, _f.CaseA.Id));
        Assert.Empty(await svc.CasesFor(anon).ToListAsync());
    }

    [Fact]
    public async Task CasesQuery_StaffNoAssignments_Empty()
    {
        await using var db = _f.NewContext();
        var svc = new AccessService(db);
        Assert.Empty(await svc.CasesFor(As(_f.StaffUnassigned)).ToListAsync());
        Assert.Empty(await svc.ClientsFor(As(_f.StaffUnassigned)).ToListAsync());
    }

    [Fact]
    public async Task CasesQuery_FilteredPerRole()
    {
        await using var db = _f.NewContext();
        var svc = new AccessService(db);
        Assert.Equal(new[] { _f.CaseA.Id }, await svc.CasesFor(As(_f.StaffAssigned)).Select(c => c.Id).ToListAsync());
        Assert.Equal(new[] { _f.CaseB.Id }, await svc.CasesFor(As(_f.ClientUserB)).Select(c => c.Id).ToListAsync());
        Assert.Equal(2, await svc.CasesFor(As(_f.OfficeManager)).CountAsync());
        Assert.Equal(new[] { _f.ClientA.Id }, await svc.ClientsFor(As(_f.StaffAssigned)).Select(c => c.Id).ToListAsync());
    }

    [Fact]
    public void RoleCapabilities()
    {
        using var db = _f.NewContext();
        var svc = new AccessService(db);
        Assert.True(svc.IsLawyerOrOfficeManager(As(_f.Lawyer)));
        Assert.True(svc.IsLawyerOrOfficeManager(As(_f.OfficeManager)));
        Assert.False(svc.IsLawyerOrOfficeManager(As(_f.StaffAssigned)));
        Assert.False(svc.IsLawyerOrOfficeManager(As(_f.ClientUserA)));
        Assert.True(svc.CanManageSettings(As(_f.Lawyer)));
        Assert.False(svc.CanManageSettings(As(_f.OfficeManager)));
        Assert.False(svc.CanManageSettings(As(_f.StaffAssigned)));
    }
}
