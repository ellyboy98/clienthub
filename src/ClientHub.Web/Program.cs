using ClientHub.Web.Data;
using ClientHub.Web.Data.Seed;
using ClientHub.Web.Infrastructure;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrEmpty(port)) builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

var raw = builder.Configuration.GetConnectionString("Default")
    ?? Environment.GetEnvironmentVariable("DATABASE_URL")
    ?? throw new InvalidOperationException("No database connection string (ConnectionStrings__Default or DATABASE_URL).");
builder.Services.AddClientHubData(DataServiceExtensions.NormalizeConnectionString(raw));
builder.Services.AddClientHubWeb(builder.Configuration, builder.Environment);

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

app.UseForwardedHeaders();
if (!app.Environment.IsDevelopment()) app.UseHsts();
// Only 404 (missing rows, access failures, 403 re-mapped by the cookie handler) shows the 404 page;
// 400/405/500 keep their own status and body.
app.UseStatusCodePagesWithReExecute("/NotFound");
app.Use(async (ctx, next) =>
{
    await next();
    if (ctx.Response.StatusCode == StatusCodes.Status403Forbidden && !ctx.Response.HasStarted)
        ctx.Response.StatusCode = StatusCodes.Status404NotFound;
    if (ctx.Response.StatusCode != StatusCodes.Status404NotFound
        && ctx.Features.Get<Microsoft.AspNetCore.Diagnostics.IStatusCodePagesFeature>() is { } f)
        f.Enabled = false;
});
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseRequestLocalization();
app.UseAuthorization();
app.UseMiddleware<FirmTwoFactorMiddleware>();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapRazorPages();
app.Run();

public partial class Program { }
