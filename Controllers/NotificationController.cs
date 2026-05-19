using DOAN_BANHANG.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace DOAN_BANHANG.Controllers;

[Authorize]
[Route("notifications")]
public class NotificationController : Controller
{
    private readonly DataContext _db;

    public NotificationController(DataContext db)
    {
        _db = db;
    }

    [HttpPost("mark-read")]
    public async Task<IActionResult> MarkRead(int id)
    {
        var idRaw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(idRaw, out var accountId)) return Unauthorized();

        var notification = await _db.SystemNotifications
            .FirstOrDefaultAsync(n => n.Id == id);

        if (notification == null) return NotFound();

        var alreadyRead = await _db.SystemNotificationReads
            .AnyAsync(r => r.SystemNotificationId == id && r.AccountId == accountId);

        if (!alreadyRead)
        {
            _db.SystemNotificationReads.Add(new SystemNotificationRead
            {
                SystemNotificationId = id,
                AccountId = accountId,
                ReadAt = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();
        }

        return Ok();
    }

    [HttpPost("mark-all-read")]
    public async Task<IActionResult> MarkAllRead()
    {
        var idRaw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(idRaw, out var accountId)) return Unauthorized();

        var userRole = User.FindFirstValue(ClaimTypes.Role);

        // Find all notifications targeting this user
        var notifications = await _db.SystemNotifications
            .Where(n => n.TargetType == "All" || 
                        (n.TargetType == "Role" && n.TargetRole == userRole) || 
                        (n.TargetType == "Account" && n.TargetAccountId == accountId))
            .ToListAsync();

        var readIds = await _db.SystemNotificationReads
            .Where(r => r.AccountId == accountId)
            .Select(r => r.SystemNotificationId)
            .ToListAsync();

        var unreadNotifications = notifications.Where(n => !readIds.Contains(n.Id)).ToList();

        foreach (var n in unreadNotifications)
        {
            _db.SystemNotificationReads.Add(new SystemNotificationRead
            {
                SystemNotificationId = n.Id,
                AccountId = accountId,
                ReadAt = DateTime.UtcNow
            });
        }

        if (unreadNotifications.Any())
        {
            await _db.SaveChangesAsync();
        }

        return Ok();
    }
}
