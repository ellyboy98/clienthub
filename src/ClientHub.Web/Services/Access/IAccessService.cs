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

    /// <summary>
    /// R-090, R-096, R-097, R-101: firm users to notify of a client upload on the case = its lawyer in charge + its assigned Staff.
    /// Active users only; when none remain, all active Lawyers. The Office Manager is never included. Every id returned can open the case.
    /// </summary>
    Task<List<Guid>> FirmRecipientsForUploadAsync(Guid caseId, CancellationToken ct = default);

    /// <summary>
    /// R-092, R-093, R-096, R-097, R-101: lawyers in charge + assigned Staff of all the client's cases (deduplicated); all active
    /// Lawyers when no active recipient remains (no cases, or all deactivated). The Office Manager is never included. Every id returned can open the chat.
    /// </summary>
    Task<List<Guid>> FirmRecipientsForClientMessageAsync(Guid clientId, CancellationToken ct = default);
}
