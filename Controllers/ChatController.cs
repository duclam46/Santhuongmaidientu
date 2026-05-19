using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DOAN_BANHANG.Models;
using System.Security.Claims;

namespace DOAN_BANHANG.Controllers;

public class ChatController : Controller
{
    private readonly DataContext _db;

    public ChatController(DataContext db)
    {
        _db = db;
    }

    private int? GetAccountId()
    {
        var claim = User.FindFirst("AccountId")?.Value ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(claim, out var id) ? id : null;
    }

    // GET /Chat — Inbox của customer
    [HttpGet]
    public async Task<IActionResult> Index(int? id)
    {
        var accountId = GetAccountId();
        if (!accountId.HasValue) return RedirectToAction("Login", "Auth", new { returnUrl = Request.Path + Request.QueryString });

        var conversations = await _db.Conversations
            .Where(c => c.CustomerId == accountId || c.ShopId == accountId)
            .Include(c => c.Customer).ThenInclude(a => a!.Profile)
            .Include(c => c.Shop).ThenInclude(a => a!.Profile)
            .Include(c => c.Messages.Where(m => !m.IsDeleted).OrderByDescending(m => m.CreatedAt).Take(1))
            .OrderByDescending(c => c.LastMessageAt)
            .ToListAsync();

        var account = await _db.Accounts.Include(a => a.Profile).FirstOrDefaultAsync(a => a.Id == accountId);
        ViewBag.CurrentAccountId = accountId;
        ViewBag.CurrentRole = account?.Role;

        if (int.TryParse(Request.Query["attachProductId"], out int attachProductId))
        {
            ViewBag.AttachProduct = await _db.Products
                .Include(p => p.Images)
                .Include(p => p.Variants)
                .FirstOrDefaultAsync(p => p.Id == attachProductId);
        }

        if (conversations.Any())
        {
            var activeConvId = id ?? conversations.First().Id;
            var activeConv = await _db.Conversations
                .Include(c => c.Customer).ThenInclude(a => a!.Profile)
                .Include(c => c.Shop).ThenInclude(a => a!.Profile)
                .FirstOrDefaultAsync(c => c.Id == activeConvId && (c.CustomerId == accountId || c.ShopId == accountId));

            if (activeConv != null)
            {
                ViewBag.ActiveConversation = activeConv;

                var messages = await _db.Messages
                    .Where(m => m.ConversationId == activeConv.Id && !m.IsDeleted)
                    .Include(m => m.Sender).ThenInclude(a => a!.Profile)
                    .Include(m => m.AttachedProduct).ThenInclude(p => p!.Images)
                    .Include(m => m.AttachedProduct).ThenInclude(p => p!.Variants)
                    .Include(m => m.AttachedVoucher)
                    .OrderBy(m => m.CreatedAt)
                    .ToListAsync();
                ViewBag.Messages = messages;
            }
        }

        return View(conversations);
    }



    // POST /Chat/StartOrOpen — Customer bắt đầu chat với shop (có thể đính kèm sản phẩm)
    [HttpPost]
    public async Task<IActionResult> StartOrOpen([FromBody] StartChatRequest req)
    {
        try
        {
            if (req == null) return BadRequest(new { error = "Request is null" });

            var accountId = GetAccountId();
            if (!accountId.HasValue) return Unauthorized(new { error = "Cần đăng nhập" });

            var account = await _db.Accounts.FindAsync(accountId.Value);
            if (account == null) return BadRequest(new { error = "Tài khoản không tồn tại" });

            if (accountId.Value == req.ShopId) return BadRequest(new { error = "Bạn không thể tự nhắn tin cho chính mình" });

            var shop = await _db.Accounts.FindAsync(req.ShopId);
            if (shop == null) return BadRequest(new { error = "Shop không tồn tại" });

            var conv = await _db.Conversations
                .FirstOrDefaultAsync(c => c.CustomerId == accountId && c.ShopId == req.ShopId);

            if (conv == null)
            {
                conv = new Conversation
                {
                    CustomerId = accountId.Value,
                    ShopId = req.ShopId,
                    CreatedAt = DateTime.UtcNow,
                    LastMessageAt = DateTime.UtcNow
                };
                _db.Conversations.Add(conv);
                await _db.SaveChangesAsync();
            }

            if (req.ProductId.HasValue)
            {
                var product = await _db.Products
                    .Include(p => p.Images)
                    .Include(p => p.Variants)
                    .FirstOrDefaultAsync(p => p.Id == req.ProductId);

                if (product != null)
                {
                    var msg = new Message
                    {
                        ConversationId = conv.Id,
                        SenderId = accountId.Value,
                        Content = req.Message ?? "Tôi muốn hỏi về sản phẩm này.",
                        MessageType = "product_card",
                        AttachedProductId = req.ProductId,
                        CreatedAt = DateTime.UtcNow
                    };
                    _db.Messages.Add(msg);
                    conv.LastMessageAt = DateTime.UtcNow;
                    await _db.SaveChangesAsync();
                }
            }

            return Ok(new { conversationId = conv.Id });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    // GET /Chat/UnreadCount — Badge số tin chưa đọc
    [HttpGet]
    public async Task<IActionResult> UnreadCount()
    {
        var accountId = GetAccountId();
        if (!accountId.HasValue) return Ok(new { count = 0 });

        var account = await _db.Accounts.FindAsync(accountId.Value);
        int count = 0;

        if (account?.Role == "Customer")
        {
            count = await _db.Messages
                .Where(m => !m.IsDeleted && !m.IsReadByCustomer
                    && m.Conversation!.CustomerId == accountId
                    && m.SenderId != accountId)
                .CountAsync();
        }
        else if (account?.Role == "Shop")
        {
            count = await _db.Messages
                .Where(m => !m.IsDeleted && !m.IsReadByShop
                    && m.Conversation!.ShopId == accountId
                    && m.SenderId != accountId)
                .CountAsync();
        }

        // Cộng thêm thông báo hệ thống chưa đọc
        var sysUnread = await _db.SystemNotifications
            .Where(n => (n.TargetType == "All"
                        || (n.TargetType == "Role" && n.TargetRole == account!.Role)
                        || (n.TargetType == "Account" && n.TargetAccountId == accountId))
                    && !n.Reads.Any(r => r.AccountId == accountId))
            .CountAsync();

        return Ok(new { count = count + sysUnread });
    }

    // GET /Chat/GetLatestNotifications — Lấy 5 thông báo mới nhất cho dropdown admin
    [HttpGet]
    public async Task<IActionResult> GetLatestNotifications()
    {
        var accountId = GetAccountId();
        if (!accountId.HasValue) return Unauthorized();

        var account = await _db.Accounts.FindAsync(accountId.Value);
        
        var notifications = await _db.SystemNotifications
            .Where(n => n.TargetType == "All"
                     || (n.TargetType == "Role" && n.TargetRole == account!.Role)
                     || (n.TargetType == "Account" && n.TargetAccountId == accountId))
            .Include(n => n.Reads.Where(r => r.AccountId == accountId))
            .OrderByDescending(n => n.CreatedAt)
            .Take(5)
            .Select(n => new {
                id = n.Id,
                title = n.Title,
                content = n.Content,
                createdAt = n.CreatedAt,
                isRead = n.Reads.Any()
            })
            .ToListAsync();

        return Ok(notifications);
    }

    // GET /Chat/SystemNotifications — Xem thông báo hệ thống
    public async Task<IActionResult> SystemNotifications()
    {
        var accountId = GetAccountId();
        if (!accountId.HasValue) return RedirectToAction("Login", "Auth", new { returnUrl = Request.Path + Request.QueryString });

        var account = await _db.Accounts.FindAsync(accountId.Value);

        var notifications = await _db.SystemNotifications
            .Where(n => n.TargetType == "All"
                     || (n.TargetType == "Role" && n.TargetRole == account!.Role)
                     || (n.TargetType == "Account" && n.TargetAccountId == accountId))
            .Include(n => n.Reads.Where(r => r.AccountId == accountId))
            .OrderByDescending(n => n.CreatedAt)
            .ToListAsync();

        ViewBag.AccountId = accountId;
        return View(notifications);
    }

    // POST /Chat/MarkSystemNotificationRead
    [HttpPost]
    public async Task<IActionResult> MarkSystemNotificationRead([FromBody] int notificationId)
    {
        var accountId = GetAccountId();
        if (!accountId.HasValue) return Unauthorized();

        var exists = await _db.SystemNotificationReads
            .AnyAsync(r => r.SystemNotificationId == notificationId && r.AccountId == accountId);
        if (!exists)
        {
            _db.SystemNotificationReads.Add(new SystemNotificationRead
            {
                SystemNotificationId = notificationId,
                AccountId = accountId.Value,
                ReadAt = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();
        }
        return Ok();
    }
    // POST /Chat/UploadMedia
    [HttpPost]
    public async Task<IActionResult> UploadMedia(IFormFile file)
    {
        if (file == null || file.Length == 0) return BadRequest("Không có file nào được tải lên");
        var accountId = GetAccountId();
        if (!accountId.HasValue) return Unauthorized();

        var env = HttpContext.RequestServices.GetRequiredService<IWebHostEnvironment>();
        var uploads = Path.Combine(env.WebRootPath, "uploads", "chat");
        if (!Directory.Exists(uploads)) Directory.CreateDirectory(uploads);

        var ext = Path.GetExtension(file.FileName).ToLower();
        var fileName = $"{Guid.NewGuid()}{ext}";
        var filePath = Path.Combine(uploads, fileName);

        using (var stream = new FileStream(filePath, FileMode.Create))
        {
            await file.CopyToAsync(stream);
        }

        return Ok(new { url = $"/uploads/chat/{fileName}", type = file.ContentType.StartsWith("video") ? "video" : "image" });
    }
}

public class StartChatRequest
{
    public int ShopId { get; set; }
    public int? ProductId { get; set; }
    public string? Message { get; set; }
}
