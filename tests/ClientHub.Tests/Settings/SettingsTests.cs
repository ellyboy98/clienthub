using System.Net;
using System.Text.Json;
using ClientHub.Tests.Support;
using ClientHub.Web.Data.Entities;
using ClientHub.Web.Services.Email;
using ClientHub.Web.Services.Storage;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClientHub.Tests.Settings;

/// <summary>Settings (R-016..R-018, R-111, R-112): Lawyer only, SMTP password encrypted, firm accounts, DuitNow QR upload.</summary>
public class SettingsTests : IClassFixture<FirmAppFactory>
{
    private readonly FirmAppFactory _app;
    public SettingsTests(FirmAppFactory app) => _app = app;
    private const string Lawyer = "lawyer1@clienthub.test";

    private static Dictionary<string, string> SettingsForm(string? password = null) => new()
    {
        ["Input.FirmName"] = "Wilter & Co", ["Input.BankName"] = "Maybank", ["Input.BankAccountName"] = "Wilter & Co",
        ["Input.BankAccountNo"] = "514012345678", ["Input.SmtpHost"] = "smtp.example.test", ["Input.SmtpPort"] = "587",
        ["Input.SmtpUser"] = "mailer", ["Input.SmtpFrom"] = "no-reply@example.test", ["Input.SmtpPassword"] = password ?? ""
    };

    [Fact]
    public async Task SmtpPassword_StoredEncrypted()
    {
        const string secret = "Sup3r-Secret-Smtp!";
        var lawyer = await _app.SignedInAsync(Lawyer);
        var res = await lawyer.PostFormAsync("/Staff/Settings", SettingsForm(secret), "/Staff/Settings");
        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);

        var s = await _app.WithDbAsync(db => db.FirmSettings.AsNoTracking().FirstAsync());
        Assert.Equal("Wilter & Co", s.FirmName);
        Assert.Equal("Maybank", s.BankName);
        Assert.Equal(587, s.SmtpPort);
        Assert.NotNull(s.SmtpPassword);
        Assert.DoesNotContain(secret, s.SmtpPassword);
        var protector = _app.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector(SmtpEmailSender.ProtectorPurpose);
        Assert.Equal(secret, protector.Unprotect(s.SmtpPassword!));

        // Never rendered back; a blank password field keeps the stored one.
        var page = await lawyer.GetStringAsync("/Staff/Settings");
        Assert.DoesNotContain(secret, page);
        Assert.DoesNotContain(s.SmtpPassword!, page);
        await lawyer.PostFormAsync("/Staff/Settings", SettingsForm(), "/Staff/Settings");
        Assert.Equal(s.SmtpPassword, await _app.WithDbAsync(db => db.FirmSettings.Select(x => x.SmtpPassword).FirstAsync()));

        // The bill pages show what was saved.
        var bank = await lawyer.GetStringAsync("/Staff/Settings");
        Assert.Contains("514012345678", bank);
    }

    [Fact]
    public async Task OfficeManager_PostStaffAccount_404()
    {
        var om = await _app.SignedInAsync("om@clienthub.test");
        var staffUser = await _app.CreateUserAsync(Roles.Staff);
        var staff = await _app.SignedInAsync(staffUser.Email!);
        var target = await _app.CreateUserAsync(Roles.Staff);
        foreach (var http in new[] { om, staff })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync("/Staff/Settings")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync("/Staff/Settings/StaffAccounts")).StatusCode);
            var email = $"new-{Guid.NewGuid():N}@clienthub.test";
            var res = await http.PostFormAsync("/Staff/Settings/StaffAccounts", new Dictionary<string, string>
                { ["Input.DisplayName"] = "New", ["Input.Email"] = email, ["Input.Role"] = Roles.Lawyer }, "/Staff");
            Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
            Assert.False(_app.WithDb(db => db.Users.Any(u => u.Email == email)));
            var deactivate = await http.PostFormAsync("/Staff/Settings?handler=Active", new Dictionary<string, string>
                { ["userId"] = target.Id.ToString(), ["active"] = "false" }, "/Staff");
            Assert.Equal(HttpStatusCode.NotFound, deactivate.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await http.PostFormAsync("/Staff/Settings", SettingsForm("x"), "/Staff")).StatusCode);
            Assert.DoesNotContain("href=\"/Staff/Settings\"", await http.GetStringAsync("/Staff"));
        }
        Assert.True(_app.WithDb(db => db.Users.Single(u => u.Id == target.Id).IsActive));
    }

    [Fact]
    public async Task DeactivatedStaff_CannotLogin()
    {
        var lawyer = await _app.SignedInAsync(Lawyer);
        var target = await _app.CreateUserAsync(Roles.Staff);
        var session = await _app.SignedInAsync(target.Email!);
        Assert.Equal(HttpStatusCode.OK, (await session.GetAsync("/Staff")).StatusCode);

        var res = await lawyer.PostFormAsync("/Staff/Settings?handler=Active", new Dictionary<string, string>
            { ["userId"] = target.Id.ToString(), ["active"] = "false" }, "/Staff/Settings");
        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
        Assert.False(_app.WithDb(db => db.Users.Single(u => u.Id == target.Id).IsActive));

        Assert.NotEqual(HttpStatusCode.Redirect, (await AppFactory.LoginAsync(_app.NewClient(), target.Email!)).StatusCode);
        var open = await session.GetAsync("/Staff");
        Assert.Equal(HttpStatusCode.Redirect, open.StatusCode);
        Assert.Contains("/Account/Login", open.Headers.Location!.OriginalString);

        // Q-02: a Lawyer can reactivate.
        await lawyer.PostFormAsync("/Staff/Settings?handler=Active", new Dictionary<string, string>
            { ["userId"] = target.Id.ToString(), ["active"] = "true" }, "/Staff/Settings");
        Assert.Equal(HttpStatusCode.Redirect, (await AppFactory.LoginAsync(_app.NewClient(), target.Email!)).StatusCode);
    }

    [Fact]
    public async Task Lawyer_CannotDeactivateSelf()
    {
        var me = await _app.CreateUserAsync(Roles.Lawyer);
        var lawyer = await _app.SignedInAsync(me.Email!);
        await lawyer.PostFormAsync("/Staff/Settings?handler=Active", new Dictionary<string, string>
            { ["userId"] = me.Id.ToString(), ["active"] = "false" }, "/Staff/Settings");
        Assert.True(_app.WithDb(db => db.Users.Single(u => u.Id == me.Id).IsActive));
        // Client logins are not managed here.
        var seeded = await _app.NewClientWithCaseAsync();
        var res = await lawyer.PostFormAsync("/Staff/Settings?handler=Active", new Dictionary<string, string>
            { ["userId"] = seeded.ClientUser.Id.ToString(), ["active"] = "false" }, "/Staff/Settings");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task NewStaff_GetsSetPasswordEmail()
    {
        var lawyer = await _app.SignedInAsync(Lawyer);
        var email = $"newstaff-{Guid.NewGuid():N}@clienthub.test";
        var res = await lawyer.PostFormAsync("/Staff/Settings/StaffAccounts", new Dictionary<string, string>
            { ["Input.DisplayName"] = "Aisyah", ["Input.Email"] = email, ["Input.Role"] = Roles.OfficeManager }, "/Staff/Settings/StaffAccounts");
        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);

        var user = _app.WithDb(db => db.Users.Single(u => u.Email == email));
        Assert.Equal(Roles.OfficeManager, user.Role);
        Assert.Null(user.PasswordHash);
        Assert.Equal("en", user.Language);
        var mail = Assert.Single(_app.Emails.Sent, m => m.To == email);
        Assert.Contains($"{AppFactory.BaseUrl}/Account/SetPassword?userId={user.Id}", mail.Body);
        Assert.Contains("Aisyah", (await lawyer.GetStringAsync("/Staff/Settings")));

        // Same email again and a Client role are refused.
        var again = await lawyer.PostFormAsync("/Staff/Settings/StaffAccounts", new Dictionary<string, string>
            { ["Input.DisplayName"] = "Dup", ["Input.Email"] = email, ["Input.Role"] = Roles.Staff }, "/Staff/Settings/StaffAccounts");
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        var clientRole = $"c-{Guid.NewGuid():N}@clienthub.test";
        await lawyer.PostFormAsync("/Staff/Settings/StaffAccounts", new Dictionary<string, string>
            { ["Input.DisplayName"] = "C", ["Input.Email"] = clientRole, ["Input.Role"] = Roles.Client }, "/Staff/Settings/StaffAccounts");
        Assert.False(_app.WithDb(db => db.Users.Any(u => u.Email == clientRole)));
    }

    [Fact]
    public async Task QrUpload_LawyerOnly_ImagesOnly()
    {
        var lawyer = await _app.SignedInAsync(Lawyer);
        var om = await _app.SignedInAsync("om@clienthub.test");
        Dictionary<string, string> Start(string name) => new() { ["kind"] = UploadTargetKind.SettingsQr, ["fileName"] = name, ["size"] = "2048" };

        Assert.Equal(HttpStatusCode.NotFound, (await om.PostFormAsync("/Uploads/Start", Start("qr.png"), "/Staff")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await lawyer.PostFormAsync("/Uploads/Start", Start("qr.pdf"), "/Staff")).StatusCode);

        var start = await lawyer.PostFormAsync("/Uploads/Start", Start("qr.png"), "/Staff");
        Assert.Equal(HttpStatusCode.OK, start.StatusCode);
        var json = JsonDocument.Parse(await start.Content.ReadAsStringAsync()).RootElement;
        Assert.True((await TestStorage.PutPresignedAsync(json.GetProperty("url").GetString()!, TestStorage.Pdf(), "image/png")).IsSuccessStatusCode);
        var ticket = json.GetProperty("ticket").GetString()!;
        // The ticket is bound to the Lawyer: the Office Manager cannot complete it.
        Assert.Equal(HttpStatusCode.NotFound, (await om.PostFormAsync("/Uploads/Complete", new Dictionary<string, string> { ["ticket"] = ticket }, "/Staff")).StatusCode);
        var complete = await lawyer.PostFormAsync("/Uploads/Complete", new Dictionary<string, string> { ["ticket"] = ticket }, "/Staff");
        Assert.Equal(HttpStatusCode.OK, complete.StatusCode);

        var key = await _app.WithDbAsync(db => db.FirmSettings.Select(s => s.QrStorageKey).FirstAsync());
        Assert.StartsWith(StorageKeys.QrPrefix, key);
        Assert.EndsWith("/qr.png", key);
        Assert.Contains("data-qr-image", await lawyer.GetStringAsync("/Staff/Settings"));
    }
}
