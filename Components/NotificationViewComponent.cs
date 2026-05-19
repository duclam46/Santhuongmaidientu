using DOAN_BANHANG.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace DOAN_BANHANG.Components;

public class NotificationViewComponent : ViewComponent
{
    private readonly DataContext _db;

    public NotificationViewComponent(DataContext db)
    {
        _db = db;
    }

    public async Task<IViewComponentResult> InvokeAsync()
    {
        if (!(User?.Identity?.IsAuthenticated ?? false))
        {
            return View(new NotificationViewModel { IsSignedIn = false });
        }

        var idRaw = ((ClaimsPrincipal)User).FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(idRaw, out var accountId))
        {
            return View(new NotificationViewModel { IsSignedIn = false });
        }

        var userRole = ((ClaimsPrincipal)User).FindFirstValue(ClaimTypes.Role);

        // Fetch notifications for this user:
        // 1. TargetType = 'All'
        // 2. TargetType = 'Role' and TargetRole = userRole
        // 3. TargetType = 'Account' and TargetAccountId = accountId
        
        var query = _db.SystemNotifications
            .AsNoTracking()
            .Where(n => n.TargetType == "All" || 
                        (n.TargetType == "Role" && n.TargetRole == userRole) || 
                        (n.TargetType == "Account" && n.TargetAccountId == accountId))
            .OrderByDescending(n => n.CreatedAt);

        var totalNotifications = await query.Take(10).ToListAsync();
        
        var readIds = await _db.SystemNotificationReads
            .Where(r => r.AccountId == accountId)
            .Select(r => r.SystemNotificationId)
            .ToListAsync();

        var unreadCount = await query
            .Where(n => !readIds.Contains(n.Id))
            .CountAsync();

        var viewModel = new NotificationViewModel
        {
            IsSignedIn = true,
            UnreadCount = unreadCount,
            Notifications = totalNotifications.Select(n => new NotificationItemViewModel
            {
                Id = n.Id,
                Title = n.Title,
                Content = n.Content,
                CreatedAt = n.CreatedAt,
                IsRead = readIds.Contains(n.Id)
            }).ToList()
        };

        return View(viewModel);
    }
}

public class NotificationViewModel
{
    public bool IsSignedIn { get; set; }
    public int UnreadCount { get; set; }
    public List<NotificationItemViewModel> Notifications { get; set; } = new();
}

public class NotificationItemViewModel
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public bool IsRead { get; set; }
}
