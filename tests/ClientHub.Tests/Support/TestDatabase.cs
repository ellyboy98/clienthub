using Npgsql;

namespace ClientHub.Tests.Support;

/// <summary>Creates a fresh PostgreSQL database on the compose "db" service and drops it afterwards.</summary>
public sealed class TestDatabase : IAsyncDisposable
{
    public string Name { get; } = "clienthub_test_" + Guid.NewGuid().ToString("N");

    public static string AdminConnection => Environment.GetEnvironmentVariable("TEST_ADMIN_CONNECTION")
        ?? $"Host={Environment.GetEnvironmentVariable("TEST_DB_HOST") ?? "localhost"};Port=5432;Database=postgres;Username=clienthub;Password=clienthub";

    public string ConnectionString => new NpgsqlConnectionStringBuilder(AdminConnection) { Database = Name }.ConnectionString;

    public async Task CreateAsync()
    {
        await using var c = new NpgsqlConnection(AdminConnection);
        await c.OpenAsync();
        await using var cmd = new NpgsqlCommand($"CREATE DATABASE \"{Name}\"", c);
        await cmd.ExecuteNonQueryAsync();
    }

    public async ValueTask DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools();
        await using var c = new NpgsqlConnection(AdminConnection);
        await c.OpenAsync();
        await using var cmd = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{Name}\" WITH (FORCE)", c);
        await cmd.ExecuteNonQueryAsync();
    }
}
