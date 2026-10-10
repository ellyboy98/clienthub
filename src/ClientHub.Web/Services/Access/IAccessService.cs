using System.Security.Claims;
using ClientHub.Web.Data.Entities;

namespace ClientHub.Web.Services.Access;

/// <summary>The only place with role rules. Pages call it first and return NotFound() on false.</summary>
public interface IAccessService
{
    /// <summary>False when the case does not exist or the user may not open it.</summary>
    Task<bool> CanAccessCaseAsync(ClaimsPrincipal user, Guid caseId, CancellationToken ct = default);

    /// <summary>Client record and its chat. False when the client does not exist or the user may not open it.</summary>
    Task<bool> CanAccessClientAsync(ClaimsPrincipal user, Guid clientId, CancellationToken ct = default);

    /// <summary>Cases the user may open, filtered in SQL.</summary>
    IQueryable<Case> CasesFor(ClaimsPrincipal user);

    /// <summary>Clients the user may open, filtered in SQL.</summary>
    IQueryable<Client> ClientsFor(ClaimsPrincipal user);

    /// <summary>Create clients, create cases, assign staff.</summary>
    bool IsLawyerOrOfficeManager(ClaimsPrincipal user);

    bool CanManageSettings(ClaimsPrincipal user);

    /// <summary>Lawyer, OfficeManager or Staff (uploads without a request, request handling, removal).</summary>
    bool IsFirm(ClaimsPrincipal user);
}
