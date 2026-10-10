using ClientHub.Web.Data;
using ClientHub.Web.Data.Entities;
using ClientHub.Web.Data.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace ClientHub.Tests;

public class SmokeTests : IAsyncLifetime
{
    private readonly string _dbName = "clienthub_test_" + Guid.NewGuid().ToString("N");
    private string AdminConn => Environment.GetEnvironmentVariable("TEST_ADMIN_CONNECTION")
        ?? $"Host={Environment.GetEnvironmentVariable("TEST_DB_HOST") ?? "localhost"};Port=5432;Database=postgres;Username=clienthub;Password=clienthub";

    public async Task InitializeAsync()
    {
        await using var c = new NpgsqlConnection(AdminConn);
        await c.OpenAsync();
        await using var cmd = new NpgsqlCommand($"CREATE DATABASE \"{_dbName}\"", c);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools();
        await using var c = new NpgsqlConnection(AdminConn);
        await c.OpenAsync();
        await using var cmd = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_dbName}\" WITH (FORCE)", c);
        await cmd.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task Database_MigrationsApplied_SeedUsersExist()
    {
        var cs = new NpgsqlConnectionStringBuilder(AdminConn) { Database = _dbName }.ConnectionString;
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddFilter(_ => false));
        services.AddDataProtection();
        services.AddClientHubData(cs);
        await using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
        await SeedData.RunAsync(scope.ServiceProvider, "Test-Password-123!");
        await SeedData.RunAsync(scope.ServiceProvider, "Test-Password-123!"); // idempotent

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(2, await db.Users.CountAsync(u => u.Role == Roles.Lawyer));
        Assert.Equal(1, await db.Users.CountAsync(u => u.Role == Roles.OfficeManager));
        Assert.Equal(1, await db.Users.CountAsync(u => u.Role == Roles.Staff));
        Assert.Equal(1, await db.Users.CountAsync(u => u.Role == Roles.Client));
        Assert.Equal(1, await db.Clients.CountAsync());
        Assert.Equal(1, await db.Cases.CountAsync());
        Assert.Equal(1, await db.FirmSettings.CountAsync());
    }
}
