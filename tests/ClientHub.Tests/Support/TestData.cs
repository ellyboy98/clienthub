using System.Text.RegularExpressions;
using ClientHub.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace ClientHub.Tests.Support;

/// <summary>Per-test data on top of the seed, so tests in one class do not share mutable rows.</summary>
public static class TestData
{
    public record Seeded(AppUser ClientUser, Guid ClientId, Guid CaseId);

    public static Guid UserId(this AppFactory app, string email) =>
        app.WithDb(db => db.Users.Single(u => u.Email == email).Id);

    /// <summary>A client with its own login and one case (lawyer1 in charge), optionally assigned to staff.</summary>
    public static async Task<Seeded> NewClientWithCaseAsync(this AppFactory app, Guid? staffId = null, string status = CaseStatus.New, string? title = null)
    {
        var user = await app.CreateUserAsync(Roles.Client);
        var lawyer = app.UserId("lawyer1@clienthub.test");
        var client = new Client
        {
            ClientType = ClientType.Person, Name = "Client " + user.Id.ToString("N")[..6], IcOrRegNo = "900101-01-0001",
            Phone = "012-0000000", Email = user.Email, Address = "1 Jalan Test", UserId = user.Id
        };
        var c = new Case
        {
            ClientId = client.Id, Title = title ?? "Case " + Guid.NewGuid().ToString("N")[..6], LawyerId = lawyer,
            Status = status, OpenedAt = DateTime.UtcNow, ClosedAt = CaseStatus.IsClosed(status) ? DateTime.UtcNow : null
        };
        await app.WithDbAsync(async db =>
        {
            db.Clients.Add(client);
            db.Cases.Add(c);
            if (staffId is { } s) db.CaseStaff.Add(new CaseStaff { CaseId = c.Id, UserId = s });
            await db.SaveChangesAsync();
            return 0;
        });
        return new Seeded(user, client.Id, c.Id);
    }

    /// <summary>Posts a form with an antiforgery token taken from tokenPage (any page the user can open).</summary>
    public static async Task<HttpResponseMessage> PostFormAsync(this HttpClient client, string url, IEnumerable<KeyValuePair<string, string>> fields, string tokenPage)
    {
        var page = await client.GetStringAsync(tokenPage);
        var all = fields.ToList();
        all.Add(new("__RequestVerificationToken", AppFactory.Token(page)));
        return await client.PostAsync(url, new FormUrlEncodedContent(all));
    }

    public static int Tile(string html, string key)
    {
        var m = Regex.Match(html, $"data-tile=\"{key}\"[\\s\\S]*?data-tile-count>(\\d+)<");
        if (!m.Success) throw new InvalidOperationException($"Tile {key} not found.");
        return int.Parse(m.Groups[1].Value);
    }
}
