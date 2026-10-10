using Microsoft.AspNetCore.Identity;
using ClientHub.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace ClientHub.Web.Data;

public static class DataServiceExtensions
{
    public static IServiceCollection AddClientHubData(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<AppDbContext>(o => o
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention());
        services.AddIdentityCore<AppUser>(o =>
            {
                o.Password.RequiredLength = 10;
                o.Lockout.MaxFailedAccessAttempts = 5;
                o.User.RequireUniqueEmail = true;
            })
            .AddRoles<AppRole>()
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders();
        return services;
    }

    /// <summary>Accepts an Npgsql string or a postgres:// URL (Neon / Vercel DATABASE_URL).</summary>
    public static string NormalizeConnectionString(string raw)
    {
        if (!raw.StartsWith("postgres://") && !raw.StartsWith("postgresql://")) return raw;
        var uri = new Uri(raw);
        var user = uri.UserInfo.Split(':', 2);
        var b = new Npgsql.NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.Port > 0 ? uri.Port : 5432,
            Database = uri.AbsolutePath.TrimStart('/'),
            Username = Uri.UnescapeDataString(user[0]),
            Password = user.Length > 1 ? Uri.UnescapeDataString(user[1]) : null,
            SslMode = Npgsql.SslMode.Require
        };
        return b.ConnectionString;
    }
}
