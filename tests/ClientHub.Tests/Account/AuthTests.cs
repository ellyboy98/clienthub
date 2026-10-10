using System.Net;
using ClientHub.Tests.Support;
using ClientHub.Web.Data.Entities;
using ClientHub.Web.Services.Access;
using ClientHub.Web.Services.Audit;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;

namespace ClientHub.Tests.Account;

public class AuthTests : IClassFixture<AppFactory>
{
    private readonly AppFactory _app;
    public AuthTests(AppFactory app) => _app = app;

    private static async Task Assert404Page(HttpResponseMessage res)
    {
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        var html = await res.Content.ReadAsStringAsync();
        Assert.Contains("404", html);
        Assert.DoesNotContain("forbidden", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("access denied", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LoginPage_ShowsFieldsAndFirmNote_NoSignUp()
    {
        var html = await _app.NewClient().GetStringAsync("/Account/Login");
        Assert.Contains("name=\"Input.Email\"", html);
        Assert.Contains("name=\"Input.Password\"", html);
        Assert.Contains("/Account/ForgotPassword", html);
        Assert.Contains("Your account is created by the firm.", html);
        Assert.Contains("culture=ms", html);
        Assert.DoesNotContain("Register", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Sign up", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CultureSwitch_ShowsMalayLabels()
    {
        var client = _app.NewClient();
        var res = await client.GetAsync("/Account/Culture?culture=ms&returnUrl=%2FAccount%2FLogin");
        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
        var html = await client.GetStringAsync("/Account/Login");
        Assert.Contains("Akaun anda dicipta oleh firma.", html);
        Assert.Contains("Lupa kata laluan", html);
    }

    [Fact]
    public async Task Anonymous_StaffPage_RedirectsToLogin()
    {
        var res = await _app.NewClient().GetAsync("/Staff");
        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
        Assert.StartsWith("/Account/Login", res.Headers.Location!.PathAndQuery);
    }

    [Fact]
    public async Task Client_LandsOnPortal_WithBottomNav()
    {
        var client = _app.NewClient();
        var login = await AppFactory.LoginAsync(client, "client@clienthub.test");
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.Equal("/Portal", login.Headers.Location!.OriginalString);
        var html = await client.GetStringAsync("/Portal");
        foreach (var href in new[] { "href=\"/Portal\"", "href=\"/Portal/Cases\"", "href=\"/Portal/Chat\"", "href=\"/Portal/Bills\"" })
            Assert.Contains(href, html);
    }

    [Fact]
    public async Task Staff_LandsOnStaff_SidebarWithoutSettings()
    {
        var client = _app.NewClient();
        var login = await AppFactory.LoginAsync(client, "staff@clienthub.test");
        Assert.Equal("/Staff", login.Headers.Location!.OriginalString);
        var res = await client.GetAsync("/Staff");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var html = await res.Content.ReadAsStringAsync();
        Assert.Contains("href=\"/Staff/Clients\"", html);
        Assert.DoesNotContain("href=\"/Staff/Settings\"", html);
    }

    [Fact]
    public async Task Client_StaffPage_404()
    {
        var client = await _app.SignedInAsync("client@clienthub.test");
        await Assert404Page(await client.GetAsync("/Staff"));
        await Assert404Page(await client.GetAsync("/Staff/Settings"));
    }

    [Fact]
    public async Task Staff_PortalPage_404()
    {
        var client = await _app.SignedInAsync("staff@clienthub.test");
        await Assert404Page(await client.GetAsync("/Portal"));
    }

    [Fact]
    public async Task Staff_Settings_404()
    {
        var client = await _app.SignedInAsync("staff@clienthub.test");
        await Assert404Page(await client.GetAsync("/Staff/Settings"));
    }

    [Fact]
    public async Task OfficeManager_Settings_404()
    {
        var client = await _app.SignedInAsync("om@clienthub.test");
        await Assert404Page(await client.GetAsync("/Staff/Settings"));
    }

    [Fact]
    public async Task UnknownPage_404Page()
    {
        await Assert404Page(await _app.NewClient().GetAsync("/does-not-exist"));
    }

    [Fact]
    public async Task InactiveUser_CannotLogin()
    {
        var u = await _app.CreateUserAsync(Roles.Staff, active: false);
        var client = _app.NewClient();
        var res = await AppFactory.LoginAsync(client, u.Email!);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Contains("The email or password is incorrect.", await res.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/Staff")).StatusCode); // still anonymous
    }

    [Fact]
    public async Task FiveFailures_LocksOut()
    {
        var u = await _app.CreateUserAsync(Roles.Staff);
        var client = _app.NewClient();
        for (var i = 0; i < 5; i++)
            await AppFactory.LoginAsync(client, u.Email!, "Wrong-Password-1!");
        var res = await AppFactory.LoginAsync(client, u.Email!); // right password, but locked
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Contains("Too many failed attempts", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task LawyerWithout2fa_RedirectedToSetup()
    {
        var client = await _app.SignedInAsync("lawyer1@clienthub.test");
        var res = await client.GetAsync("/Staff");
        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
        Assert.Equal("/Account/Manage/TwoFactor", res.Headers.Location!.OriginalString);
        var setup = await client.GetAsync("/Account/Manage/TwoFactor");
        Assert.Equal(HttpStatusCode.OK, setup.StatusCode);
        Assert.Contains("<svg", await setup.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task OfficeManagerWithout2fa_RedirectedToSetup()
    {
        var client = await _app.SignedInAsync("om@clienthub.test");
        var res = await client.GetAsync("/Staff");
        Assert.Equal("/Account/Manage/TwoFactor", res.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Login_WritesAudit()
    {
        var u = await _app.CreateUserAsync(Roles.Client);
        await _app.SignedInAsync(u.Email!);
        await AppFactory.LoginAsync(_app.NewClient(), u.Email!, "Wrong-Password-1!");
        var actions = _app.WithDb(db => db.AuditLog.Where(a => a.UserId == u.Id).Select(a => a.Action).ToList());
        Assert.Contains(AuditActions.UserLogin, actions);
        Assert.Contains(AuditActions.UserLoginFailed, actions);
    }

    [Fact]
    public async Task MyAccount_LanguageChange_AppliesAfterLogin()
    {
        var u = await _app.CreateUserAsync(Roles.Client);
        var client = await _app.SignedInAsync(u.Email!);
        var page = await client.GetStringAsync("/Account/Manage");
        var res = await client.PostAsync("/Account/Manage?handler=Language", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Language"] = "ms",
            ["__RequestVerificationToken"] = AppFactory.Token(page)
        }));
        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
        Assert.Equal("ms", _app.WithDb(db => db.Users.AsNoTracking().Single(x => x.Id == u.Id).Language));
        var portal = await client.GetStringAsync("/Portal");
        Assert.Contains("Utama", portal);
        Assert.Contains("Sembang", portal);
    }

    [Fact]
    public async Task ForgotPassword_SameAnswerForUnknownEmail()
    {
        var client = _app.NewClient();
        var page = await client.GetStringAsync("/Account/ForgotPassword");
        var res = await client.PostAsync("/Account/ForgotPassword", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = "nobody@clienthub.test",
            ["__RequestVerificationToken"] = AppFactory.Token(page)
        }));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Contains("If an account exists", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ForgotPassword_ForgedHost_LinkUsesAppBaseUrl()
    {
        var u = await _app.CreateUserAsync(Roles.Client);
        var client = _app.NewClient();
        var page = await client.GetStringAsync("/Account/ForgotPassword");
        var req = new HttpRequestMessage(HttpMethod.Post, "/Account/ForgotPassword")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Email"] = u.Email!,
                ["__RequestVerificationToken"] = AppFactory.Token(page)
            })
        };
        req.Headers.Host = "evil.example";
        req.Headers.Add("X-Forwarded-Host", "evil.example");
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(req)).StatusCode);

        var mail = Assert.Single(_app.Emails.Sent, m => m.To == u.Email);
        Assert.Contains(AppFactory.BaseUrl + "/Account/SetPassword?userId=" + u.Id, mail.Body);
        Assert.DoesNotContain("evil.example", mail.Body);
    }

    [Fact]
    public async Task DeactivatedUser_LosesSession()
    {
        var u = await _app.CreateUserAsync(Roles.Client);
        var client = await _app.SignedInAsync(u.Email!);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Portal")).StatusCode);

        using (var scope = _app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var fresh = await users.FindByIdAsync(u.Id.ToString());
            Assert.True((await users.DeactivateAsync(fresh!)).Succeeded);
        }

        var res = await client.GetAsync("/Portal");
        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
        Assert.StartsWith("/Account/Login", res.Headers.Location!.PathAndQuery);
    }

    [Fact]
    public async Task BadAntiforgery_Stays400_Not404()
    {
        var res = await _app.NewClient().PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Email"] = "x@clienthub.test",
            ["Input.Password"] = "x"
        }));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task PageTitle_IsLocalised()
    {
        var html = await _app.NewClient().GetStringAsync("/Account/Login");
        Assert.Matches("<title>Sign in [^<]+Client Hub</title>", html);
    }
}
