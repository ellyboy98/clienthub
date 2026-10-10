using ClientHub.Web.Data;
using ClientHub.Web.Data.Entities;
using ClientHub.Web.Services.Access;
using ClientHub.Web.Services.Cases;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace ClientHub.Web.Pages.Staff.Clients;

/// <summary>Clients list (R-139, R-140). Staff see only clients of their assigned cases (R-012).</summary>
public class IndexModel : PageModel
{
    public const string FilterAll = "all", FilterPerson = "person", FilterCompany = "company", FilterInactive = "inactive";
    private readonly AppDbContext _db;
    private readonly IAccessService _access;

    public IndexModel(AppDbContext db, IAccessService access) { _db = db; _access = access; }

    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public string Filter { get; set; } = FilterAll;

    public record Row(Guid Id, string Name, string ClientType, string? IcOrRegNo, string? Phone, int ActiveCases, bool LoginActive);
    public List<Row> Rows { get; private set; } = new();
    public bool CanCreate { get; private set; }

    public async Task OnGetAsync()
    {
        CanCreate = _access.IsLawyerOrOfficeManager(User);
        var q = _access.ClientsFor(User);
        if (!string.IsNullOrWhiteSpace(Q))
        {
            var term = "%" + Q.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            q = q.Where(c => EF.Functions.ILike(c.Name, term) || EF.Functions.ILike(c.IcOrRegNo ?? "", term));
        }
        q = Filter switch
        {
            FilterPerson => q.Where(c => c.ClientType == ClientType.Person),
            FilterCompany => q.Where(c => c.ClientType == ClientType.Company),
            FilterInactive => q.Where(c => c.User != null && !c.User.IsActive),
            _ => q
        };
        // Active-case count only over cases this user can open.
        var visibleActive = _access.CasesFor(User).Active();
        Rows = await q.OrderBy(c => c.Name)
            .Select(c => new Row(c.Id, c.Name, c.ClientType, c.IcOrRegNo, c.Phone,
                visibleActive.Count(k => k.ClientId == c.Id), c.User == null || c.User.IsActive))
            .ToListAsync();
    }
}
