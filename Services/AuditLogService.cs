using System.Security.Claims;
using DOAN_BANHANG.Models;

namespace DOAN_BANHANG.Services;

public class AuditLogService
{
    private readonly DataContext _db;

    public AuditLogService(DataContext db)
    {
        _db = db;
    }

    public async Task LogAsync(
        HttpContext? httpContext,
        string action,
        string entityType,
        string? entityId = null,
        string? detail = null)
    {
        try
        {
            int? actorId = null;
            string? actorEmail = null;
            string? actorRole = null;
            string? ip = null;

            if (httpContext != null)
            {
                var idRaw = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (int.TryParse(idRaw, out var parsed) && parsed > 0)
                    actorId = parsed;

                actorEmail = httpContext.User.FindFirstValue(ClaimTypes.Email)
                             ?? httpContext.User.Identity?.Name;
                actorRole = httpContext.User.FindFirstValue(ClaimTypes.Role);
                ip = httpContext.Connection.RemoteIpAddress?.ToString();
            }

            _db.AuditLogs.Add(new AuditLog
            {
                ActorAccountId = actorId,
                ActorEmail = actorEmail,
                ActorRole = actorRole,
                Action = action,
                EntityType = entityType,
                EntityId = entityId,
                Detail = detail != null && detail.Length > 2000 ? detail[..2000] : detail,
                IpAddress = ip,
                CreatedAt = DateTime.UtcNow
            });

            await _db.SaveChangesAsync();
        }
        catch
        {
            // Never fail main business action if audit logging encounters a transient issue
        }
    }
}
