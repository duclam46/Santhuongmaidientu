using System.Security.Claims;
using DOAN_BANHANG.Helpers;
using DOAN_BANHANG.Hubs;
using DOAN_BANHANG.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace DOAN_BANHANG.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = "Shop,Admin")]
public class ChatController : Controller
{
    private readonly DataContext _db;
    private readonly IHubContext<ChatHub> _hub;

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

    // GET /Admin/Chat — Shopee Seller Chat Pro (3-Pane)
    [HttpGet]
    public async Task<IActionResult> Index(int? id)
    {
        var accountId = GetAccountId();
        if (!accountId.HasValue) return Redirect("/");

        var isAdmin = User.IsInRole("Admin");

        var conversationsQuery = _db.Conversations.AsQueryable();
        if (!isAdmin)
            conversationsQuery = conversationsQuery.Where(c => c.ShopId == accountId.Value);

        var conversations = await conversationsQuery
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
                .Where(m => convIds.Contains(m.ConversationId) && !m.IsDeleted && !m.IsReadByShop && m.SenderId == m.Conversation!.CustomerId)
                .GroupBy(m => m.ConversationId)
                .Select(g => new { ConvId = g.Key, Count = g.Count() })
                .ToListAsync();

            foreach (var u in unreadRaw)
                unreadMap[u.ConvId] = u.Count;
        }

        ViewBag.CurrentAccountId = accountId.Value;
        ViewBag.UnreadByConv = unreadMap;
        ViewBag.OnlineIds = ChatHub.GetOnlineAccountIds();

        // Lấy Voucher & Sản phẩm của Shop để gửi nhanh trong khung chat
        var vouchers = await _db.Vouchers
            .AsNoTracking()
            .Where(v => (isAdmin || v.ShopId == accountId.Value) && v.IsActive && v.EndDate > DateTime.UtcNow)
            .OrderByDescending(v => v.CreatedAt)
            .Take(20)
            .ToListAsync();
        ViewBag.Vouchers = vouchers;

        if (conversations.Count > 0)
        {
            var activeConvId = id ?? conversations.First().Id;
            var activeConv = conversations.FirstOrDefault(c => c.Id == activeConvId) ?? conversations.First();
            ViewBag.ActiveConversation = activeConv;

            // Đánh dấu đã đọc
            await _db.Messages
                .Where(m => m.ConversationId == activeConv.Id && !m.IsDeleted && !m.IsReadByShop)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.IsReadByShop, true));
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

    // GET /Admin/Chat/GetMessages/{id} — Tin nhắn + Cột ngữ cảnh Đơn hàng & Khiếu nại của khách (Phương án 3)
    [HttpGet]
    public async Task<IActionResult> GetMessages(int id)
    {
        var accountId = GetAccountId();
        if (!accountId.HasValue) return Unauthorized();

        var isAdmin = User.IsInRole("Admin");
        var conv = await _db.Conversations
            .AsNoTracking()
            .Include(c => c.Customer).ThenInclude(a => a!.Profile)
            .Include(c => c.Shop).ThenInclude(a => a!.Profile)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (conv == null || (!isAdmin && conv.ShopId != accountId.Value)) return Forbid();

        // Đánh dấu đã đọc & thông báo realtime cho khách
        await _db.Messages
            .Where(m => m.ConversationId == id && !m.IsDeleted && !m.IsReadByShop)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.IsReadByShop, true));

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
                senderAvatar = ImageUrlHelper.NormalizeStaticFileUrl(m.Sender?.Profile?.AvatarUrl ?? m.Sender?.Profile?.ShopLogoUrl),
                senderName = m.Sender?.Profile?.FullName ?? m.Sender?.Profile?.ShopName ?? m.Sender?.Email,
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

        // ── Cột phải Phương án 3: Đơn hàng gần nhất & Khiếu nại của khách với Shop này ──
        var recentOrders = await _db.Orders
            .AsNoTracking()
            .Where(o => o.CustomerId == conv.CustomerId && (isAdmin || o.ShopId == conv.ShopId))
            .OrderByDescending(o => o.Id)
            .Take(5)
            .Select(o => new
            {
                id = o.Id,
                orderCode = o.OrderCode,
                orderStatus = o.OrderStatus,
                paymentMethod = o.PaymentMethod,
                paymentStatus = o.PaymentStatus,
                finalAmount = o.FinalAmount,
                trackingCode = o.TrackingCode
            })
            .ToListAsync();

        var totalOrdersCount = await _db.Orders
            .AsNoTracking()
            .CountAsync(o => o.CustomerId == conv.CustomerId && (isAdmin || o.ShopId == conv.ShopId));

        var totalSpent = await _db.Orders
            .AsNoTracking()
            .Where(o => o.CustomerId == conv.CustomerId && (isAdmin || o.ShopId == conv.ShopId)
                        && o.OrderStatus != "Cancelled" && o.OrderStatus != "Returned")
            .SumAsync(o => (decimal?)o.FinalAmount) ?? 0m;

        var activeDisputes = await _db.OrderDisputes
            .AsNoTracking()
            .Include(d => d.Order)
            .Where(d => d.CustomerId == conv.CustomerId && (isAdmin || d.ShopId == conv.ShopId))
            .OrderByDescending(d => d.CreatedAt)
            .Take(3)
            .Select(d => new
            {
                id = d.Id,
                disputeCode = d.DisputeCode,
                orderCode = d.Order != null ? d.Order.OrderCode : "",
                reasonTitle = d.ReasonTitle,
                status = d.Status,
                requestedAmount = d.RequestedAmount
            })
            .ToListAsync();

        string custName = conv.Customer?.Profile?.FullName
            ?? (conv.Customer?.Email?.Split('@')[0])
            ?? "Khách hàng";

        return Json(new
        {
            conversation = new
            {
                id = conv.Id,
                customerId = conv.CustomerId,
                customerName = custName,
                customerEmail = conv.Customer?.Email ?? "",
                customerPhone = conv.Customer?.Profile?.PhoneNumber ?? "Chưa cập nhật",
                customerAvatar = ImageUrlHelper.NormalizeStaticFileUrl(conv.Customer?.Profile?.AvatarUrl),
                isOnline = ChatHub.IsAccountOnline(conv.CustomerId),
                shopName = conv.Shop?.Profile?.ShopName ?? conv.Shop?.Email ?? ""
            },
            customerContext = new
            {
                totalOrders = totalOrdersCount,
                totalSpent,
                recentOrders,
                activeDisputes
            },
            messages = msgDtos
        });
    }

    // GET /Admin/Chat/SearchProducts?q=... — Shop tìm sản phẩm của mình để gửi thẻ gợi ý cho khách
    [HttpGet]
    public async Task<IActionResult> SearchProducts(string? q)
    {
        var accountId = GetAccountId();
        if (!accountId.HasValue) return Unauthorized();

        var isAdmin = User.IsInRole("Admin");
        var query = _db.Products
            .AsNoTracking()
            .Include(p => p.Images)
            .Include(p => p.Variants)
            .Where(p => !p.IsDeleted && p.Status == "Active");

        if (!isAdmin)
            query = query.Where(p => p.ShopId == accountId.Value);

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query = query.Where(p => EF.Functions.Like(p.Name, $"%{term}%"));
        }

        var items = await query
            .OrderByDescending(p => p.AverageRating)
            .Take(12)
            .ToListAsync();

        var result = items.Select(p =>
        {
            var img = p.Images.OrderByDescending(i => i.IsPrimary).Select(i => i.ImageUrl).FirstOrDefault();
            var prices = p.Variants.Where(v => v.IsActive && !v.IsDeleted).Select(v => v.SalePrice ?? v.Price).ToList();
            return new
            {
                id = p.Id,
                name = p.Name,
                slug = p.Slug,
                imageUrl = ImageUrlHelper.NormalizeStaticFileUrl(img),
                price = prices.Count > 0 ? prices.Min() : 0m
            };
        });

        return Json(result);
    }
}
