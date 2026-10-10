using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text.RegularExpressions;
using ClientHub.Tests.Support;
using ClientHub.Web.Data;
using ClientHub.Web.Data.Entities;
using ClientHub.Web.Services.Clients;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClientHub.Tests.Clients;

/// <summary>Client form rules (R-029, R-030), account creation email (R-014), login email edits (Q-14), deactivation (R-019).</summary>
public class ClientTests : IClassFixture<FirmAppFactory>
{
    private readonly FirmAppFactory _app;
    public ClientTests(FirmAppFactory app) => _app = app;

    private static List<string> Invalid(ClientInput input)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(input, new ValidationContext(input), results, validateAllProperties: true);
        return results.SelectMany(r => r.MemberNames).ToList();
    }

    private static ClientInput Valid(string type = ClientType.Person) => new()
    {
        ClientType = type, Name = "Ali", IcOrRegNo = "900101-01-1111", Phone = "012-1111111",
        Email = $"c-{Guid.NewGuid():N}@clienthub.test", Address = "1 Jalan Ampang"
    };

    private static Dictionary<string, string> Form(ClientInput i) => new()
    {
        ["Input.ClientType"] = i.ClientType, ["Input.Name"] = i.Name ?? "", ["Input.IcOrRegNo"] = i.IcOrRegNo ?? "",
        ["Input.Phone"] = i.Phone ?? "", ["Input.Email"] = i.Email ?? "", ["Input.Address"] = i.Address ?? "",
        ["Input.ContactPersonName"] = i.ContactPersonName ?? "", ["Input.ContactPersonPhone"] = i.ContactPersonPhone ?? ""
    };

    [Fact]
    public void Company_WithoutContactPerson_Invalid()
    {
        var input = Valid(ClientType.Company);
        var errors = Invalid(input);
        Assert.Contains(nameof(ClientInput.ContactPersonName), errors);
        Assert.Contains(nameof(ClientInput.ContactPersonPhone), errors);

        input.ContactPersonName = "Siti";
        input.ContactPersonPhone = "013-2222222";
        Assert.Empty(Invalid(input));
    }

    [Fact]
    public void Person_AllFieldsRequired()
    {
        var errors = Invalid(new ClientInput { ClientType = ClientType.Person });
        foreach (var f in new[] { "Name", "IcOrRegNo", "Phone", "Email", "Address" }) Assert.Contains(f, errors);
        Assert.DoesNotContain(nameof(ClientInput.ContactPersonName), errors);
        Assert.Empty(Invalid(Valid()));
    }

    [Fact]
    public async Task Om_CreateCompany_WithoutContactPerson_Refused()
    {
        var om = await _app.SignedInAsync("om@clienthub.test");
        var input = Valid(ClientType.Company);
        var res = await om.PostFormAsync("/Staff/Clients/Create", Form(input), "/Staff/Clients/Create");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Contains("This field is required.", await res.Content.ReadAsStringAsync());
        Assert.False(_app.WithDb(db => db.Clients.Any(c => c.Email == input.Email)));
    }

    [Fact]
    public async Task Create_SendsSetPasswordEmail()
    {
        var om = await _app.SignedInAsync("om@clienthub.test");
        var input = Valid(ClientType.Company);
        input.ContactPersonName = "Siti";
        input.ContactPersonPhone = "013-2222222";
        var res = await om.PostFormAsync("/Staff/Clients/Create", Form(input), "/Staff/Clients/Create");
        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
        Assert.StartsWith("/Staff/Clients/Details/", res.Headers.Location!.OriginalString);

        var client = await _app.WithDbAsync(db => db.Clients.Include(c => c.User).SingleAsync(c => c.Email == input.Email));
        Assert.Equal(Roles.Client, client.User!.Role);
        Assert.Equal(input.Email, client.User.Email);
        Assert.Equal("Siti", client.ContactPersonName);

        var mail = Assert.Single(_app.Emails.Sent, m => m.To == input.Email);
        var link = Regex.Match(mail.Body, @"https://portal\.firm\.test/Account/SetPassword\?userId=([0-9a-f-]+)&code=(\S+)");
        Assert.True(link.Success, mail.Body);
        Assert.Equal(client.UserId.ToString(), link.Groups[1].Value);

        // The link really sets a password and the client can then sign in (R-133 sender side).
        var anon = _app.NewClient();
        var setPage = await anon.GetAsync(new Uri(link.Value).PathAndQuery);
        Assert.Equal(HttpStatusCode.OK, setPage.StatusCode);
        var post = await anon.PostAsync(new Uri(link.Value).PathAndQuery, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["UserId"] = link.Groups[1].Value, ["Code"] = Uri.UnescapeDataString(link.Groups[2].Value),
            ["Password"] = AppFactory.Password, ["ConfirmPassword"] = AppFactory.Password,
            ["__RequestVerificationToken"] = AppFactory.Token(await setPage.Content.ReadAsStringAsync())
        }));
        Assert.Equal(HttpStatusCode.Redirect, post.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await AppFactory.LoginAsync(_app.NewClient(), input.Email!)).StatusCode);
    }

    [Fact]
    public async Task Create_DuplicateEmail_Refused()
    {
        var om = await _app.SignedInAsync("om@clienthub.test");
        var input = Valid();
        input.Email = "staff@clienthub.test";
        var res = await om.PostFormAsync("/Staff/Clients/Create", Form(input), "/Staff/Clients/Create");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Contains("This email is already used by another login.", await res.Content.ReadAsStringAsync());
    }

    private static Match SetPasswordLink(SentEmail mail)
    {
        var link = Regex.Match(mail.Body, @"https://portal\.firm\.test/Account/SetPassword\?userId=([0-9a-f-]+)&code=(\S+)");
        Assert.True(link.Success, mail.Body);
        return link;
    }

    /// <summary>Posts the set-password form for a link; returns the response (302 on success).</summary>
    private async Task<HttpResponseMessage> UseLinkAsync(Match link)
    {
        var anon = _app.NewClient();
        var path = new Uri(link.Value).PathAndQuery;
        var page = await anon.GetAsync(path);
        return await anon.PostAsync(path, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["UserId"] = link.Groups[1].Value, ["Code"] = Uri.UnescapeDataString(link.Groups[2].Value),
            ["Password"] = AppFactory.Password, ["ConfirmPassword"] = AppFactory.Password,
            ["__RequestVerificationToken"] = AppFactory.Token(await page.Content.ReadAsStringAsync())
        }));
    }

    private async Task<(Guid ClientId, ClientInput Input)> OmCreatesClientAsync(HttpClient om)
    {
        var input = Valid();
        var res = await om.PostFormAsync("/Staff/Clients/Create", Form(input), "/Staff/Clients/Create");
        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
        return (_app.WithDb(db => db.Clients.Single(c => c.Email == input.Email).Id), input);
    }

    [Fact]
    public async Task EditEmail_UpdatesLoginEmail()
    {
        var seeded = await _app.NewClientWithCaseAsync();
        var newEmail = $"new-{Guid.NewGuid():N}@clienthub.test";
        var om = await _app.SignedInAsync("om@clienthub.test");
        var input = ClientInput.From(_app.WithDb(db => db.Clients.Single(c => c.Id == seeded.ClientId)));
        input.Email = newEmail;
        var res = await om.PostFormAsync($"/Staff/Clients/Edit/{seeded.ClientId}", Form(input), $"/Staff/Clients/Edit/{seeded.ClientId}");
        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);

        Assert.Equal(newEmail, _app.WithDb(db => db.Users.Single(u => u.Id == seeded.ClientUser.Id).Email));
        Assert.Equal(HttpStatusCode.Redirect, (await AppFactory.LoginAsync(_app.NewClient(), newEmail)).StatusCode);
        Assert.NotEqual(HttpStatusCode.Redirect, (await AppFactory.LoginAsync(_app.NewClient(), seeded.ClientUser.Email!)).StatusCode);
        // The client already has a password, so no new link is sent.
        Assert.DoesNotContain(_app.Emails.Sent, m => m.To == newEmail);
    }

    [Fact]
    public async Task EmailChange_OldSetPasswordLinkFails_NewLinkSent()
    {
        var om = await _app.SignedInAsync("om@clienthub.test");
        var (clientId, input) = await OmCreatesClientAsync(om);
        var oldLink = SetPasswordLink(Assert.Single(_app.Emails.Sent, m => m.To == input.Email));

        var newEmail = $"fixed-{Guid.NewGuid():N}@clienthub.test";
        var oldEmail = input.Email!;
        input.Email = newEmail;
        var res = await om.PostFormAsync($"/Staff/Clients/Edit/{clientId}", Form(input), $"/Staff/Clients/Edit/{clientId}");
        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);

        // The link sent to the old (mistyped) address no longer works.
        var oldUse = await UseLinkAsync(oldLink);
        Assert.Equal(HttpStatusCode.OK, oldUse.StatusCode);
        Assert.NotEqual(HttpStatusCode.Redirect, (await AppFactory.LoginAsync(_app.NewClient(), newEmail)).StatusCode);

        // The new address got a fresh account-created link that works.
        var newLink = SetPasswordLink(Assert.Single(_app.Emails.Sent, m => m.To == newEmail));
        Assert.Equal(HttpStatusCode.Redirect, (await UseLinkAsync(newLink)).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await AppFactory.LoginAsync(_app.NewClient(), newEmail)).StatusCode);
        Assert.NotEqual(HttpStatusCode.Redirect, (await AppFactory.LoginAsync(_app.NewClient(), oldEmail)).StatusCode);
    }

    [Fact]
    public async Task Staff_ChangeEmail_404_OtherFieldsSave()
    {
        var seeded = await _app.NewClientWithCaseAsync(_app.UserId("staff@clienthub.test"));
        var staff = await _app.SignedInAsync("staff@clienthub.test");
        var edit = $"/Staff/Clients/Edit/{seeded.ClientId}";
        var page = await staff.GetStringAsync(edit);
        Assert.Matches("name=\"Input.Email\"[^>]*readonly", page);

        var input = ClientInput.From(_app.WithDb(db => db.Clients.Single(c => c.Id == seeded.ClientId)));
        var originalEmail = input.Email;
        input.Email = $"takeover-{Guid.NewGuid():N}@clienthub.test";
        var res = await staff.PostFormAsync(edit, Form(input), edit);
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.Equal(originalEmail, _app.WithDb(db => db.Users.Single(u => u.Id == seeded.ClientUser.Id).Email));
        Assert.Equal(originalEmail, _app.WithDb(db => db.Clients.Single(c => c.Id == seeded.ClientId).Email));

        input.Email = originalEmail;
        input.Phone = "019-9999999";
        res = await staff.PostFormAsync(edit, Form(input), edit);
        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
        Assert.Equal("019-9999999", _app.WithDb(db => db.Clients.Single(c => c.Id == seeded.ClientId).Phone));
    }

    [Fact]
    public async Task Create_EmailFails_ClientCreated_WarningShown()
    {
        var om = await _app.SignedInAsync("om@clienthub.test");
        var input = Valid();
        HttpResponseMessage res;
        _app.Emails.Fail = true;
        try { res = await om.PostFormAsync("/Staff/Clients/Create", Form(input), "/Staff/Clients/Create"); }
        finally { _app.Emails.Fail = false; }
        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
        Assert.True(_app.WithDb(db => db.Clients.Any(c => c.Email == input.Email)));
        var details = await om.GetStringAsync(res.Headers.Location!.OriginalString);
        Assert.Contains("the set-password email could not be sent", details);
        Assert.DoesNotContain("A set-password email was sent", details);
    }

    [Fact]
    public async Task Deactivate_ClientCannotLogin()
    {
        var seeded = await _app.NewClientWithCaseAsync();
        var clientSession = await _app.SignedInAsync(seeded.ClientUser.Email!);
        var lawyer = await _app.SignedInAsync("lawyer1@clienthub.test");
        var details = $"/Staff/Clients/Details/{seeded.ClientId}";
        var res = await lawyer.PostFormAsync(details + "?handler=Deactivate", Array.Empty<KeyValuePair<string, string>>(), details);
        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);

        Assert.NotEqual(HttpStatusCode.Redirect, (await AppFactory.LoginAsync(_app.NewClient(), seeded.ClientUser.Email!)).StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, (await clientSession.GetAsync("/Portal")).StatusCode);
        // Data stays; the list shows the client under Inactive.
        Assert.True(_app.WithDb(db => db.Clients.Any(c => c.Id == seeded.ClientId)));
        Assert.Contains($"/Staff/Clients/Details/{seeded.ClientId}", await lawyer.GetStringAsync("/Staff/Clients?filter=inactive"));

        // Q-02: reactivation restores the login.
        res = await lawyer.PostFormAsync(details + "?handler=Reactivate", Array.Empty<KeyValuePair<string, string>>(), details);
        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await AppFactory.LoginAsync(_app.NewClient(), seeded.ClientUser.Email!)).StatusCode);
    }

    [Fact]
    public async Task ClientsList_SearchByIcAndFilterCompany()
    {
        var lawyer = await _app.SignedInAsync("lawyer1@clienthub.test");
        var html = await lawyer.GetStringAsync("/Staff/Clients?q=900101-01-1234");
        Assert.Contains("Sample Client", html);
        html = await lawyer.GetStringAsync("/Staff/Clients?filter=company&q=Sample");
        Assert.DoesNotContain("Sample Client", html);
    }
}
