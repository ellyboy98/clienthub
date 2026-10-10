using System.Security.Claims;
using ClientHub.Web.Data;
using ClientHub.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace ClientHub.Web.Services.Access;

public class AccessService : IAccessService
{
    private readonly AppDbContext _db;

    public AccessService(AppDbContext db) => _db = db;

    public static Guid? UserId(ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    public static string? Role(ClaimsPrincipal user) =>
        user.Identity?.IsAuthenticated == true ? user.FindFirstValue(ClaimTypes.Role) : null;

    public Task<bool> CanAccessCaseAsync(ClaimsPrincipal user, Guid caseId, CancellationToken ct = default) =>
        CasesFor(user).AnyAsync(c => c.Id == caseId, ct);

    public Task<bool> CanAccessClientAsync(ClaimsPrincipal user, Guid clientId, CancellationToken ct = default) =>
        ClientsFor(user).AnyAsync(c => c.Id == clientId, ct);

    public IQueryable<Case> CasesFor(ClaimsPrincipal user)
    {
        var uid = UserId(user);
        if (uid is null) return _db.Cases.Where(_ => false);
        var id = uid.Value;
        return Role(user) switch
        {
            Roles.Lawyer or Roles.OfficeManager => _db.Cases,
            Roles.Staff => _db.Cases.Where(c => c.StaffLinks.Any(s => s.UserId == id)),
            Roles.Client => _db.Cases.Where(c => c.Client!.UserId == id),
            _ => _db.Cases.Where(_ => false)
        };
    }

    public IQueryable<Client> ClientsFor(ClaimsPrincipal user)
    {
        var uid = UserId(user);
        if (uid is null) return _db.Clients.Where(_ => false);
        var id = uid.Value;
        return Role(user) switch
        {
            Roles.Lawyer or Roles.OfficeManager => _db.Clients,
            Roles.Staff => _db.Clients.Where(cl => cl.Cases.Any(c => c.StaffLinks.Any(s => s.UserId == id))),
            Roles.Client => _db.Clients.Where(cl => cl.UserId == id),
            _ => _db.Clients.Where(_ => false)
        };
    }

    public bool IsLawyerOrOfficeManager(ClaimsPrincipal user) =>
        Role(user) is Roles.Lawyer or Roles.OfficeManager;

    public bool CanManageSettings(ClaimsPrincipal user) => Role(user) == Roles.Lawyer;
}
