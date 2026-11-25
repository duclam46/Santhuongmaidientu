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

    private int? GetCurrentAccountId()
    {
        var raw = User.FindFirst("AccountId")?.Value ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(raw, out var id) && id > 0 ? id : null;
    }

    [HttpPost("mark-read")]
    public async Task<IActionResult> MarkRead(int id)
    {
        var accountId = GetCurrentAccountId();
        if (!accountId.HasValue) return Unauthorized();

        var notification = await _db.SystemNotifications
            .AsNoTracking()
            .FirstOrDefaultAsync(n => n.Id == id);

        if (notification == null) return NotFound();

        var alreadyRead = await _db.SystemNotificationReads
            .AnyAsync(r => r.SystemNotificationId == id && r.AccountId == accountId.Value);

        if (!alreadyRead)
        {
            _db.SystemNotificationReads.Add(new SystemNotificationRead
            {
                SystemNotificationId = id,
                AccountId = accountId.Value,
                ReadAt = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();
        }

        return Ok(new { ok = true });
    }

    [HttpPost("mark-all-read")]
    public async Task<IActionResult> MarkAllRead()
    {
        var accountId = GetCurrentAccountId();
        if (!accountId.HasValue) return Unauthorized();

        var account = await _db.Accounts
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == accountId.Value);

        var userRole = account?.Role ?? User.FindFirstValue(ClaimTypes.Role) ?? "Customer";

        var notificationIds = await _db.SystemNotifications
            .AsNoTracking()
            .Where(n => n.TargetType == "All" ||
                        (n.TargetType == "Role" && n.TargetRole == userRole) ||
                        (n.TargetType == "Account" && n.TargetAccountId == accountId.Value))
            .Select(n => n.Id)
            .ToListAsync();

        var readIds = await _db.SystemNotificationReads
            .AsNoTracking()
            .Where(r => r.AccountId == accountId.Value)
            .Select(r => r.SystemNotificationId)
            .ToListAsync();

        var unreadIds = notificationIds.Except(readIds).ToList();

        foreach (var nId in unreadIds)
        {
            _db.SystemNotificationReads.Add(new SystemNotificationRead
            {
                SystemNotificationId = nId,
                AccountId = accountId.Value,
                ReadAt = DateTime.UtcNow
            });
        }

        if (unreadIds.Count > 0)
        {
            await _db.SaveChangesAsync();
        }

        return Ok(new { ok = true, markedCount = unreadIds.Count });
    }
}
