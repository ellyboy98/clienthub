using ClientHub.Web.Data;
using ClientHub.Web.Data.Seed;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrEmpty(port)) builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

var raw = builder.Configuration.GetConnectionString("Default")
    ?? Environment.GetEnvironmentVariable("DATABASE_URL")
    ?? throw new InvalidOperationException("No database connection string (ConnectionStrings__Default or DATABASE_URL).");
builder.Services.AddClientHubData(DataServiceExtensions.NormalizeConnectionString(raw));
builder.Services.AddRazorPages();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    if (args.Contains("seed"))
    {
        var pw = Environment.GetEnvironmentVariable("SEED_PASSWORD")
            ?? throw new InvalidOperationException("SEED_PASSWORD is not set.");
        await SeedData.RunAsync(scope.ServiceProvider, pw);
        Console.WriteLine("Seed complete.");
        return;
    }
}

app.UseStaticFiles();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapRazorPages();
app.Run();

public partial class Program { }
