using System.Security.Claims;
using DOAN_BANHANG.Helpers;
using DOAN_BANHANG.Hubs;
using DOAN_BANHANG.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace DOAN_BANHANG.Controllers;

public class ChatController : Controller
{
    private readonly DataContext _db;
    private readonly IHubContext<ChatHub> _hub;

    private static readonly HashSet<string> AllowedImageExts = new(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".png", ".webp", ".gif" };

    private static readonly HashSet<string> AllowedVideoExts = new(StringComparer.OrdinalIgnoreCase)
        { ".mp4", ".webm", ".mov" };

    public ChatController(DataContext db, IHubContext<ChatHub> hub)
    {
        _db = db;
        _hub = hub;
    }

    private int? GetAccountId()
    {
        var claim = User.FindFirst("AccountId")?.Value ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(claim, out var id) ? id : null;
    }

    // GET /Chat — Hộp thư hỏi đáp của khách hàng
    [HttpGet]
    public async Task<IActionResult> Index(int? id, int? startShopId)
    {
        var accountId = GetAccountId();
        if (!accountId.HasValue)
        {
            var guestReturn = $"{Request.Path}{Request.QueryString}";
            ViewBag.IsGuest = true;
            ViewBag.GuestReturnUrl = string.IsNullOrWhiteSpace(guestReturn) ? "/Chat" : guestReturn;
            return View(new List<Conversation>());
        }

        ViewBag.IsGuest = false;

        if (startShopId.HasValue && startShopId.Value != accountId.Value)
        {
            var isValidShop = await _db.Accounts
                .AsNoTracking()
                .AnyAsync(a => a.Id == startShopId.Value && !a.IsDeleted && a.Status == "Active");
            if (isValidShop)
            {
                var targetConv = await _db.Conversations
                    .FirstOrDefaultAsync(c => c.CustomerId == accountId.Value && c.ShopId == startShopId.Value);
                if (targetConv == null)
                {
                    targetConv = new Conversation
                    {
                        CustomerId = accountId.Value,
                        ShopId = startShopId.Value,
                        CreatedAt = DateTime.UtcNow,
                        LastMessageAt = DateTime.UtcNow
                    };
                    _db.Conversations.Add(targetConv);
                    await _db.SaveChangesAsync();
                }
                id = targetConv.Id;
            }
        }

        var conversations = await _db.Conversations
            .Where(c => c.CustomerId == accountId || c.ShopId == accountId)
            .Include(c => c.Customer).ThenInclude(a => a!.Profile)
            .Include(c => c.Shop).ThenInclude(a => a!.Profile)
            .Include(c => c.Messages.Where(m => !m.IsDeleted).OrderByDescending(m => m.CreatedAt).Take(1))
            .OrderByDescending(c => c.LastMessageAt)
            .ToListAsync();

        var convIds = conversations.Select(c => c.Id).ToList();
        var unreadMap = new Dictionary<int, int>();
        if (convIds.Count > 0)
        {
            var unreadRaw = await _db.Messages
                .AsNoTracking()
                .Where(m => convIds.Contains(m.ConversationId) && !m.IsDeleted && m.SenderId != accountId.Value)
                .Select(m => new
                {
                    m.ConversationId,
                    IsCustomerSide = m.Conversation!.CustomerId == accountId.Value,
                    m.IsReadByCustomer,
                    m.IsReadByShop
                })
                .ToListAsync();

            foreach (var grp in unreadRaw.GroupBy(x => x.ConversationId))
            {
                unreadMap[grp.Key] = grp.Count(x => x.IsCustomerSide ? !x.IsReadByCustomer : !x.IsReadByShop);
            }
        }

        var account = await _db.Accounts.AsNoTracking().Include(a => a.Profile).FirstOrDefaultAsync(a => a.Id == accountId);
        ViewBag.CurrentAccountId = accountId.Value;
        ViewBag.CurrentRole = account?.Role;
        ViewBag.UnreadByConv = unreadMap;
        ViewBag.OnlineIds = ChatHub.GetOnlineAccountIds();

        // Gợi ý gian hàng uy tín để khách có thể bấm nhắn tin ngay khi hộp thư trống
        if (conversations.Count == 0)
        {
            var suggestedShops = await _db.Accounts
                .AsNoTracking()
                .Include(a => a.Profile)
                .Where(a => !a.IsDeleted && a.Status == "Active" && a.Role == "Shop" && a.Id != accountId.Value)
                .OrderByDescending(a => a.Profile != null && a.Profile.IsMall)
                .ThenBy(a => a.Id)
                .Take(4)
                .ToListAsync();
            ViewBag.SuggestedShops = suggestedShops;
        }

        if (int.TryParse(Request.Query["attachProductId"], out int attachProductId))
        {
            ViewBag.AttachProduct = await _db.Products
                .AsNoTracking()
                .Include(p => p.Images)
                .Include(p => p.Variants)
                .FirstOrDefaultAsync(p => p.Id == attachProductId && !p.IsDeleted);
        }

        if (conversations.Count > 0)
        {
            var activeConvId = id ?? conversations.First().Id;
            var activeConv = conversations.FirstOrDefault(c => c.Id == activeConvId) ?? conversations.First();

            ViewBag.ActiveConversation = activeConv;

            // Đánh dấu đã đọc khi mở
            bool isCustomerSide = activeConv.CustomerId == accountId.Value;
            var unreadQ = _db.Messages.Where(m => m.ConversationId == activeConv.Id && !m.IsDeleted);
            if (isCustomerSide)
                await unreadQ.Where(m => !m.IsReadByCustomer).ExecuteUpdateAsync(s => s.SetProperty(m => m.IsReadByCustomer, true));
            else
                await unreadQ.Where(m => !m.IsReadByShop).ExecuteUpdateAsync(s => s.SetProperty(m => m.IsReadByShop, true));

            unreadMap[activeConv.Id] = 0;

            var messages = await _db.Messages
                .AsNoTracking()
                .Where(m => m.ConversationId == activeConv.Id && !m.IsDeleted)
                .Include(m => m.Sender).ThenInclude(a => a!.Profile)
                .Include(m => m.AttachedProduct).ThenInclude(p => p!.Images)
                .Include(m => m.AttachedProduct).ThenInclude(p => p!.Variants)
                .Include(m => m.AttachedVoucher)
                .OrderBy(m => m.CreatedAt)
                .ToListAsync();

            ViewBag.Messages = messages;
        }

        return View(conversations);
    }

    // GET /Chat/GetMessages/{id} — Chuyển hội thoại mượt không reload trang
    [HttpGet]
    public async Task<IActionResult> GetMessages(int id)
    {
        var accountId = GetAccountId();
        if (!accountId.HasValue) return Unauthorized();

        var conv = await _db.Conversations
            .AsNoTracking()
            .Include(c => c.Customer).ThenInclude(a => a!.Profile)
            .Include(c => c.Shop).ThenInclude(a => a!.Profile)
            .FirstOrDefaultAsync(c => c.Id == id && (c.CustomerId == accountId || c.ShopId == accountId));

        if (conv == null) return Forbid();

        bool isCustomerSide = conv.CustomerId == accountId.Value;
        var unreadQ = _db.Messages.Where(m => m.ConversationId == id && !m.IsDeleted);
        if (isCustomerSide)
            await unreadQ.Where(m => !m.IsReadByCustomer).ExecuteUpdateAsync(s => s.SetProperty(m => m.IsReadByCustomer, true));
        else
            await unreadQ.Where(m => !m.IsReadByShop).ExecuteUpdateAsync(s => s.SetProperty(m => m.IsReadByShop, true));

        await _hub.Clients.Group($"conversation_{id}")
            .SendAsync("MessagesRead", new { conversationId = id, readByAccountId = accountId.Value });

        var messages = await _db.Messages
            .AsNoTracking()
            .Where(m => m.ConversationId == id && !m.IsDeleted)
            .Include(m => m.Sender).ThenInclude(a => a!.Profile)
            .Include(m => m.AttachedProduct).ThenInclude(p => p!.Images)
            .Include(m => m.AttachedProduct).ThenInclude(p => p!.Variants)
            .Include(m => m.AttachedVoucher)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync();

        var partner = isCustomerSide ? conv.Shop : conv.Customer;
        var partnerName = isCustomerSide
            ? (partner?.Profile?.ShopName ?? partner?.Profile?.FullName ?? partner?.Email ?? "Gian hàng")
            : (partner?.Profile?.FullName ?? partner?.Email ?? "Khách hàng");
        var partnerAvatar = ImageUrlHelper.NormalizeStaticFileUrl(
            isCustomerSide ? (partner?.Profile?.ShopLogoUrl ?? partner?.Profile?.AvatarUrl) : partner?.Profile?.AvatarUrl);
        var partnerId = partner?.Id ?? 0;

        var msgDtos = messages.Select(m =>
        {
            var prodImg = m.AttachedProduct?.Images.OrderByDescending(i => i.IsPrimary).Select(i => i.ImageUrl).FirstOrDefault();
            var prices = m.AttachedProduct?.Variants.Where(v => v.IsActive && !v.IsDeleted).Select(v => v.SalePrice ?? v.Price).ToList();
            return new
            {
                id = m.Id,
                conversationId = m.ConversationId,
                senderId = m.SenderId,
                content = m.Content,
                messageType = m.MessageType,
                isReadByCustomer = m.IsReadByCustomer,
                isReadByShop = m.IsReadByShop,
                createdAt = m.CreatedAt.ToString("o"),
                product = m.AttachedProduct == null ? null : new
                {
                    id = m.AttachedProduct.Id,
                    name = m.AttachedProduct.Name,
                    slug = m.AttachedProduct.Slug,
                    imageUrl = ImageUrlHelper.NormalizeStaticFileUrl(prodImg),
                    price = prices != null && prices.Count > 0 ? prices.Min() : 0m
                },
                voucher = m.AttachedVoucher == null ? null : new
                {
                    id = m.AttachedVoucher.Id,
                    code = m.AttachedVoucher.Code,
                    discountType = m.AttachedVoucher.DiscountType,
                    discountValue = m.AttachedVoucher.DiscountValue,
                    minOrderValue = m.AttachedVoucher.MinOrderValue,
                    endDate = m.AttachedVoucher.EndDate.AddHours(7).ToString("dd/MM/yyyy")
                }
            };
        });

        return Json(new
        {
            conversation = new
            {
                id = conv.Id,
                partnerId,
                partnerName,
                partnerAvatar,
                isMall = isCustomerSide && (partner?.Profile?.IsMall == true),
                shopSlug = isCustomerSide ? partner?.Profile?.ShopSlug : null,
                isOnline = ChatHub.IsAccountOnline(partnerId)
            },
            messages = msgDtos
        });
    }

    // POST /Chat/StartOrOpen — Khách mở cuộc trò chuyện với Shop
    [HttpPost]
    public async Task<IActionResult> StartOrOpen([FromBody] StartChatRequest req)
    {
        try
        {
            if (req == null) return BadRequest(new { error = "Dữ liệu không hợp lệ." });

            var accountId = GetAccountId();
            if (!accountId.HasValue) return Unauthorized(new { error = "Cần đăng nhập để nhắn tin với Shop." });

            if (accountId.Value == req.ShopId)
                return BadRequest(new { error = "Bạn không thể tự nhắn tin cho gian hàng của chính mình." });

            var shop = await _db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == req.ShopId && !a.IsDeleted);
            if (shop == null) return BadRequest(new { error = "Gian hàng không tồn tại." });

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

            // Chỉ tự động gửi thẻ sản phẩm nếu client yêu cầu rõ (AutoSendCard = true)
            if (req.ProductId.HasValue && req.AutoSendCard)
            {
                var product = await _db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == req.ProductId && !p.IsDeleted);
                if (product != null)
                {
                    var text = string.IsNullOrWhiteSpace(req.Message) ? "Tôi muốn hỏi về sản phẩm này." : req.Message.Trim();
                    var msg = new Message
                    {
                        ConversationId = conv.Id,
                        SenderId = accountId.Value,
                        Content = text,
                        MessageType = "product_card",
                        AttachedProductId = req.ProductId,
                        IsReadByCustomer = true,
                        IsReadByShop = false,
                        CreatedAt = DateTime.UtcNow
                    };
                    _db.Messages.Add(msg);
                    conv.LastMessageAt = DateTime.UtcNow;
                    await _db.SaveChangesAsync();

                    var dto = await ChatHub.BuildMessageDtoAsync(_db, msg.Id);
                    if (dto != null)
                    {
                        await _hub.Clients.Group($"conversation_{conv.Id}").SendAsync("ReceiveMessage", dto);
                        await _hub.Clients.Group($"shop_{conv.ShopId}")
                            .SendAsync("NewMessageFromCustomer", new { conversationId = conv.Id, content = text, createdAt = DateTime.UtcNow.ToString("o") });
                    }
                }
            }

            return Ok(new { conversationId = conv.Id, attachProductId = req.ProductId });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    // GET /Chat/UnreadCount — Badge số tin chưa đọc tách rõ Chat & Thông báo hệ thống
    [HttpGet("/Chat/UnreadCount")]
    public async Task<IActionResult> UnreadCount()
    {
        var accountId = GetAccountId();
        if (!accountId.HasValue) return Ok(new { count = 0, chatUnread = 0, sysUnread = 0, isSignedIn = false });

        var account = await _db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == accountId.Value && !a.IsDeleted);
        if (account == null) return Ok(new { count = 0, chatUnread = 0, sysUnread = 0, isSignedIn = false });

        int chatUnread = await _db.Messages
            .AsNoTracking()
            .Where(m => !m.IsDeleted && m.SenderId != accountId.Value &&
                ((m.Conversation!.CustomerId == accountId.Value && !m.IsReadByCustomer) ||
                 (m.Conversation!.ShopId == accountId.Value && !m.IsReadByShop)))
            .CountAsync();

        var sysUnread = await _db.SystemNotifications
            .AsNoTracking()
            .Where(n => (n.TargetType == "All"
                        || (n.TargetType == "Role" && n.TargetRole == account.Role)
                        || (n.TargetType == "Account" && n.TargetAccountId == accountId.Value))
                    && !n.Reads.Any(r => r.AccountId == accountId.Value))
            .CountAsync();

        return Ok(new { count = chatUnread + sysUnread, chatUnread, sysUnread, isSignedIn = true, role = account.Role });
    }

    [HttpGet("/Chat/GetLatestNotifications")]
    public async Task<IActionResult> GetLatestNotifications(int take = 12)
    {
        var accountId = GetAccountId();
        if (!accountId.HasValue) return Unauthorized();

        var account = await _db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == accountId.Value && !a.IsDeleted);
        if (account == null) return Unauthorized();

        take = Math.Clamp(take, 1, 30);

        var rawList = await _db.SystemNotifications
            .AsNoTracking()
            .Where(n => n.TargetType == "All"
                     || (n.TargetType == "Role" && n.TargetRole == account.Role)
                     || (n.TargetType == "Account" && n.TargetAccountId == accountId.Value))
            .Include(n => n.Reads.Where(r => r.AccountId == accountId.Value))
            .OrderByDescending(n => n.CreatedAt)
            .Take(take)
            .ToListAsync();

        var orderMap = await DOAN_BANHANG.Components.NotificationViewComponent.BuildOrderLinkMapAsync(
            _db, accountId.Value, account.Role, rawList);

        var notifications = rawList.Select(n =>
        {
            var meta = DOAN_BANHANG.Components.NotificationViewComponent.ResolveNotificationMeta(
                n.Title, n.Content, account.Role, orderMap, accountId.Value);
            return new
            {
                id = n.Id,
                title = n.Title,
                content = n.Content,
                createdAt = n.CreatedAt,
                isRead = n.Reads.Any(),
                categoryKey = meta.CategoryKey,
                categoryLabel = meta.CategoryLabel,
                iconClass = meta.IconClass,
                actionUrl = meta.ActionUrl,
                actionText = meta.ActionText
            };
        }).ToList();

        return Ok(notifications);
    }

    [HttpGet("/Chat/SystemNotifications")]
    public async Task<IActionResult> SystemNotifications()
    {
        var accountId = GetAccountId();
        if (!accountId.HasValue)
            return RedirectToAction("Index", "Home", new { requireLogin = "notifications", returnUrl = "/Chat/SystemNotifications" });

        var account = await _db.Accounts
            .AsNoTracking()
            .Include(a => a.Profile)
            .FirstOrDefaultAsync(a => a.Id == accountId.Value && !a.IsDeleted);
        if (account == null)
            return RedirectToAction("Index", "Home", new { requireLogin = "notifications", returnUrl = "/Chat/SystemNotifications" });

        var notifications = await _db.SystemNotifications
            .AsNoTracking()
            .Where(n => n.TargetType == "All"
                     || (n.TargetType == "Role" && n.TargetRole == account.Role)
                     || (n.TargetType == "Account" && n.TargetAccountId == accountId.Value))
            .Include(n => n.Reads.Where(r => r.AccountId == accountId.Value))
            .OrderByDescending(n => n.CreatedAt)
            .ToListAsync();

        var orderMap = await DOAN_BANHANG.Components.NotificationViewComponent.BuildOrderLinkMapAsync(
            _db, accountId.Value, account.Role, notifications);

        ViewBag.AccountId = accountId.Value;
        ViewBag.UserRole = account.Role;
        ViewBag.DisplayName = account.Profile?.ShopName ?? account.Profile?.FullName ?? account.Email;
        ViewBag.OrderLinkMap = orderMap;
        return View(notifications);
    }

    [HttpPost("/Chat/MarkSystemNotificationRead")]
    public async Task<IActionResult> MarkSystemNotificationRead([FromBody] int notificationId)
    {
        var accountId = GetAccountId();
        if (!accountId.HasValue) return Unauthorized();

        var exists = await _db.SystemNotificationReads
            .AnyAsync(r => r.SystemNotificationId == notificationId && r.AccountId == accountId.Value);
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
        return Ok(new { ok = true });
    }

    // POST /Chat/UploadMedia — Bảo mật chặt đuôi file, MIME & dung lượng
    [HttpPost]
    public async Task<IActionResult> UploadMedia(IFormFile file)
    {
        var accountId = GetAccountId();
        if (!accountId.HasValue) return Unauthorized(new { error = "Cần đăng nhập để gửi tệp." });

        if (file == null || file.Length == 0)
            return BadRequest(new { error = "Không có tệp nào được tải lên." });

        var ext = Path.GetExtension(file.FileName ?? string.Empty).ToLowerInvariant();
        var isImage = AllowedImageExts.Contains(ext);
        var isVideo = AllowedVideoExts.Contains(ext);

        if (!isImage && !isVideo)
            return BadRequest(new { error = "Chỉ hỗ trợ ảnh (JPG, PNG, WEBP, GIF) hoặc video (MP4, WEBM, MOV)." });

        if (isImage && file.Length > 5 * 1024 * 1024)
            return BadRequest(new { error = "Dung lượng ảnh tối đa là 5MB." });

        if (isVideo && file.Length > 20 * 1024 * 1024)
            return BadRequest(new { error = "Dung lượng video tối đa là 20MB." });

        var env = HttpContext.RequestServices.GetRequiredService<IWebHostEnvironment>();
        var uploads = Path.Combine(env.WebRootPath, "uploads", "chat");
        if (!Directory.Exists(uploads)) Directory.CreateDirectory(uploads);

        var fileName = $"{Guid.NewGuid():N}{ext}";
        var filePath = Path.Combine(uploads, fileName);

        await using (var stream = new FileStream(filePath, FileMode.Create))
        {
            await file.CopyToAsync(stream);
        }

        return Ok(new { url = $"/uploads/chat/{fileName}", type = isVideo ? "video" : "image" });
    }
}

public class StartChatRequest
{
    public int ShopId { get; set; }
    public int? ProductId { get; set; }
    public string? Message { get; set; }
    public bool AutoSendCard { get; set; } = false;
}
