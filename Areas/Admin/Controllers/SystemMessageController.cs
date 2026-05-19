using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DOAN_BANHANG.Models;
using Microsoft.AspNetCore.SignalR;
using DOAN_BANHANG.Hubs;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace DOAN_BANHANG.Areas.Admin.Controllers;

[Area("Admin")]
[Microsoft.AspNetCore.Authorization.Authorize(Roles = "Admin")]
public class SystemMessageController : Controller
{
    private readonly DataContext _db;
    private readonly IHubContext<ChatHub> _hub;

    public SystemMessageController(DataContext db, IHubContext<ChatHub> hub)
    {
        _db = db;
        _hub = hub;
    }

    private int? GetAccountId()
    {
        var claim = User.FindFirst("AccountId")?.Value ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(claim, out var id) ? id : null;
    }

    // GET /Admin/SystemMessage
    public async Task<IActionResult> Index()
    {
        var accountId = GetAccountId();
        if (!accountId.HasValue) return RedirectToAction("Login", "Auth", new { area = "" });

        var account = await _db.Accounts.FindAsync(accountId.Value);
        // if (account?.Role != "Admin") return Forbid();

        var notifications = await _db.SystemNotifications
            .Include(n => n.SentByAdmin).ThenInclude(a => a!.Profile)
            .OrderByDescending(n => n.CreatedAt)
            .ToListAsync();

        return View(notifications);
    }

    // GET /Admin/SystemMessage/Create
    public IActionResult Create()
    {
        var accountId = GetAccountId();
        if (!accountId.HasValue) return RedirectToAction("Login", "Auth", new { area = "" });

        // Lấy danh sách account để Admin có thể chọn (nếu gửi đích danh)
        var accounts = _db.Accounts.Include(a => a.Profile).ToList();
        ViewBag.Accounts = accounts;

        return View();
    }

    // POST /Admin/SystemMessage/Create
    [HttpPost]
    public async Task<IActionResult> Create(SystemNotification model)
    {
        var accountId = GetAccountId();
        if (!accountId.HasValue) return RedirectToAction("Login", "Auth", new { area = "" });

        var account = await _db.Accounts.FindAsync(accountId.Value);
        // if (account?.Role != "Admin") return Forbid();

        model.SentByAdminId = accountId.Value;
        model.CreatedAt = DateTime.UtcNow;

        // Reset các field không liên quan tuỳ theo TargetType
        if (model.TargetType == "All")
        {
            model.TargetRole = null;
            model.TargetAccountId = null;
        }
        else if (model.TargetType == "Role")
        {
            model.TargetAccountId = null;
            if (string.IsNullOrEmpty(model.TargetRole)) model.TargetRole = "Customer";
        }
        else if (model.TargetType == "Account")
        {
            model.TargetRole = null;
        }

        _db.SystemNotifications.Add(model);
        await _db.SaveChangesAsync();

        // Chuẩn bị DTO để gửi qua SignalR
        var notificationDto = new
        {
            id = model.Id,
            title = model.Title,
            content = model.Content,
            createdAt = model.CreatedAt.ToString("o"),
            type = "system_notification"
        };

        // Push qua SignalR tuỳ theo TargetType
        if (model.TargetType == "All")
        {
            await _hub.Clients.All.SendAsync("ReceiveSystemNotification", notificationDto);
        }
        else if (model.TargetType == "Role")
        {
            // Trong OnConnectedAsync, ta chưa add user vào group "role_{Role}" nên tạm thời ta có thể broadcast
            // Tuy nhiên, để tối ưu ta có thể lấy danh sách account thuộc role đang online (nếu quản lý connectionId)
            // Hoặc gửi broadcast và client tự lọc. Tạm thời broadcast.
            await _hub.Clients.All.SendAsync("ReceiveSystemNotification", notificationDto);
        }
        else if (model.TargetType == "Account" && model.TargetAccountId.HasValue)
        {
            await _hub.Clients.Group($"user_{model.TargetAccountId.Value}").SendAsync("ReceiveSystemNotification", notificationDto);
        }

        TempData["SuccessMessage"] = "Gửi thông báo hệ thống thành công!";
        return RedirectToAction(nameof(Index));
    }
}
