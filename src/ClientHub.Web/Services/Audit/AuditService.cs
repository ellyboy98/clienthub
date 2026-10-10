using System.Text.Json;
using ClientHub.Web.Data;
using ClientHub.Web.Data.Entities;
using ClientHub.Web.Services.Access;

namespace ClientHub.Web.Services.Audit;

public class AuditService : IAuditService
{
    private readonly AppDbContext _db;
    private readonly IHttpContextAccessor _http;

    public AuditService(AppDbContext db, IHttpContextAccessor http) { _db = db; _http = http; }

    public async Task LogAsync(string action, string entity, Guid? entityId, object? details = null, Guid? userId = null, CancellationToken ct = default)
    {
        var user = _http.HttpContext?.User;
        _db.AuditLog.Add(new AuditLog
        {
            UserId = userId ?? (user is null ? null : AccessService.UserId(user)),
            Action = action,
            Entity = entity,
            EntityId = entityId,
            At = DateTime.UtcNow,
            Details = details is null ? null : JsonSerializer.Serialize(details)
        });
        await _db.SaveChangesAsync(ct);
    }
}
