using ClientHub.Web.Data.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ClientHub.Web.Data.Seed;

public static class SeedData
{
    public static async Task RunAsync(IServiceProvider sp, string password)
    {
        var db = sp.GetRequiredService<AppDbContext>();
        var users = sp.GetRequiredService<UserManager<AppUser>>();
        var roles = sp.GetRequiredService<RoleManager<AppRole>>();

        foreach (var r in Roles.All)
            if (!await roles.RoleExistsAsync(r)) await roles.CreateAsync(new AppRole(r));

        if (!await db.FirmSettings.AnyAsync())
        {
            db.FirmSettings.Add(new FirmSettings { FirmName = "Sample Law Firm" });
            await db.SaveChangesAsync();
        }

        var lawyer1 = await EnsureUser(users, "lawyer1@clienthub.test", "Lawyer One", Roles.Lawyer, password);
        await EnsureUser(users, "lawyer2@clienthub.test", "Lawyer Two", Roles.Lawyer, password);
        await EnsureUser(users, "om@clienthub.test", "Office Manager", Roles.OfficeManager, password);
        var staff = await EnsureUser(users, "staff@clienthub.test", "Staff One", Roles.Staff, password);
        var clientUser = await EnsureUser(users, "client@clienthub.test", "Sample Client", Roles.Client, password);

        var client = await db.Clients.FirstOrDefaultAsync(c => c.UserId == clientUser.Id);
        if (client == null)
        {
            client = new Client
            {
                ClientType = ClientType.Person,
                Name = "Sample Client",
                IcOrRegNo = "900101-01-1234",
                Email = clientUser.Email,
                Phone = "012-3456789",
                UserId = clientUser.Id
            };
            db.Clients.Add(client);
            await db.SaveChangesAsync();
        }

        var theCase = await db.Cases.FirstOrDefaultAsync(c => c.ClientId == client.Id);
        if (theCase == null)
        {
            theCase = new Case
            {
                ClientId = client.Id,
                Title = "Sample tenancy dispute",
                CaseType = CaseType.Civil,
                Status = CaseStatus.New,
                LawyerId = lawyer1.Id,
                OpenedAt = DateTime.UtcNow
            };
            db.Cases.Add(theCase);
            await db.SaveChangesAsync();
        }

        if (!await db.CaseStaff.AnyAsync(cs => cs.CaseId == theCase.Id && cs.UserId == staff.Id))
        {
            db.CaseStaff.Add(new CaseStaff { CaseId = theCase.Id, UserId = staff.Id });
            await db.SaveChangesAsync();
        }
    }

    private static async Task<AppUser> EnsureUser(UserManager<AppUser> users, string email, string name, string role, string password)
    {
        var u = await users.FindByEmailAsync(email);
        if (u != null) return u;
        u = new AppUser { UserName = email, Email = email, EmailConfirmed = true, DisplayName = name, Role = role, Language = "en" };
        var res = await users.CreateAsync(u, password);
        if (!res.Succeeded) throw new InvalidOperationException("Seed user failed: " + string.Join("; ", res.Errors.Select(e => e.Description)));
        await users.AddToRoleAsync(u, role);
        return u;
    }
}
