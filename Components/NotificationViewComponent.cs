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
        var principal = HttpContext?.User ?? (User as ClaimsPrincipal);
        if (!(principal?.Identity?.IsAuthenticated ?? false))
        {
            return View(new NotificationViewModel { IsSignedIn = false });
        }

        var idRaw = principal.FindFirst("AccountId")?.Value
                    ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(idRaw, out var accountId) || accountId <= 0)
        {
            return View(new NotificationViewModel { IsSignedIn = false });
        }

        var account = await _db.Accounts
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == accountId && !a.IsDeleted);

        if (account == null)
        {
            return View(new NotificationViewModel { IsSignedIn = false });
        }

        var userRole = !string.IsNullOrWhiteSpace(account.Role)
            ? account.Role
            : (principal.FindFirstValue(ClaimTypes.Role) ?? "Customer");

        var query = _db.SystemNotifications
            .AsNoTracking()
            .Where(n => n.TargetType == "All" ||
                        (n.TargetType == "Role" && n.TargetRole == userRole) ||
                        (n.TargetType == "Account" && n.TargetAccountId == accountId))
            .OrderByDescending(n => n.CreatedAt);

        var totalNotifications = await query.Take(15).ToListAsync();

        var readIds = await _db.SystemNotificationReads
            .AsNoTracking()
            .Where(r => r.AccountId == accountId)
            .Select(r => r.SystemNotificationId)
            .ToListAsync();

        var unreadCount = await query
            .Where(n => !readIds.Contains(n.Id))
            .CountAsync();

        // Preload recent order / return / dispute mapping for smart action links
        var orderMap = await BuildOrderLinkMapAsync(accountId, userRole, totalNotifications);

        var viewModel = new NotificationViewModel
        {
            IsSignedIn = true,
            AccountId = accountId,
            UserRole = userRole,
            UnreadCount = unreadCount,
            Notifications = totalNotifications.Select(n =>
            {
                var meta = ResolveNotificationMeta(n.Title, n.Content, userRole, orderMap, accountId);
                return new NotificationItemViewModel
                {
                    Id = n.Id,
                    Title = n.Title,
                    Content = n.Content,
                    CreatedAt = n.CreatedAt,
                    IsRead = readIds.Contains(n.Id),
                    CategoryKey = meta.CategoryKey,
                    CategoryLabel = meta.CategoryLabel,
                    IconClass = meta.IconClass,
                    ActionUrl = meta.ActionUrl,
                    ActionText = meta.ActionText
                };
            }).ToList()
        };

        return View(viewModel);
    }

    public static async Task<Dictionary<string, (int OrderId, int ShopId, int CustomerId, int? ReturnId, int? DisputeId)>> BuildOrderLinkMapAsync(
        DataContext db,
        int accountId,
        string userRole,
        IEnumerable<SystemNotification> notifications)
    {
        var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var regex = new System.Text.RegularExpressions.Regex(@"#([A-Za-z0-9\-_]+)");
        foreach (var n in notifications)
        {
            foreach (System.Text.RegularExpressions.Match m in regex.Matches($"{n.Title} {n.Content}"))
            {
                if (m.Groups.Count > 1 && !string.IsNullOrWhiteSpace(m.Groups[1].Value))
                {
                    codes.Add(m.Groups[1].Value.Trim());
                }
            }
        }

        var result = new Dictionary<string, (int OrderId, int ShopId, int CustomerId, int? ReturnId, int? DisputeId)>(StringComparer.OrdinalIgnoreCase);
        if (codes.Count == 0) return result;

        var orders = await db.Orders
            .AsNoTracking()
            .Where(o => codes.Contains(o.OrderCode) && (o.ShopId == accountId || o.CustomerId == accountId || userRole == "Admin"))
            .Select(o => new { o.Id, o.OrderCode, o.ShopId, o.CustomerId })
            .ToListAsync();

        var orderIds = orders.Select(o => o.Id).ToList();
        var returnMap = orderIds.Count == 0
            ? new Dictionary<int, int>()
            : await db.ReturnRequests
                .AsNoTracking()
                .Where(r => orderIds.Contains(r.OrderId))
                .GroupBy(r => r.OrderId)
                .Select(g => new { OrderId = g.Key, ReturnId = g.OrderByDescending(x => x.Id).Select(x => x.Id).FirstOrDefault() })
                .ToDictionaryAsync(x => x.OrderId, x => x.ReturnId);

        var disputeMap = orderIds.Count == 0
            ? new Dictionary<int, int>()
            : await db.OrderDisputes
                .AsNoTracking()
                .Where(d => orderIds.Contains(d.OrderId))
                .GroupBy(d => d.OrderId)
                .Select(g => new { OrderId = g.Key, DisputeId = g.OrderByDescending(x => x.Id).Select(x => x.Id).FirstOrDefault() })
                .ToDictionaryAsync(x => x.OrderId, x => x.DisputeId);

        foreach (var o in orders)
        {
            returnMap.TryGetValue(o.Id, out var rId);
            disputeMap.TryGetValue(o.Id, out var dId);
            result[o.OrderCode] = (o.Id, o.ShopId, o.CustomerId, rId > 0 ? rId : null, dId > 0 ? dId : null);
        }

        return result;
    }

    private Task<Dictionary<string, (int OrderId, int ShopId, int CustomerId, int? ReturnId, int? DisputeId)>> BuildOrderLinkMapAsync(
        int accountId,
        string userRole,
        IEnumerable<SystemNotification> notifications)
        => BuildOrderLinkMapAsync(_db, accountId, userRole, notifications);

    public static (string CategoryKey, string CategoryLabel, string IconClass, string? ActionUrl, string? ActionText) ResolveNotificationMeta(
        string? title,
        string? content,
        string? userRole,
        Dictionary<string, (int OrderId, int ShopId, int CustomerId, int? ReturnId, int? DisputeId)>? orderMap = null,
        int currentAccountId = 0)
    {
        var t = title ?? string.Empty;
        var c = content ?? string.Empty;
        var combined = $"{t} {c}";
        bool isShopRole = string.Equals(userRole, "Shop", StringComparison.OrdinalIgnoreCase)
                          || string.Equals(userRole, "Admin", StringComparison.OrdinalIgnoreCase);

        string? matchedCode = null;
        if (orderMap != null && orderMap.Count > 0)
        {
            var regex = new System.Text.RegularExpressions.Regex(@"#([A-Za-z0-9\-_]+)");
            foreach (System.Text.RegularExpressions.Match m in regex.Matches(combined))
            {
                var candidate = m.Groups[1].Value.Trim();
                if (orderMap.ContainsKey(candidate))
                {
                    matchedCode = candidate;
                    break;
                }
            }
        }

        // 1. Trả hàng / Hoàn tiền
        if (combined.Contains("trả hàng", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("đổi trả", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("hoàn hàng", StringComparison.OrdinalIgnoreCase))
        {
            string? url = isShopRole ? "/Admin/ReturnRequests/Index" : "/my-orders";
            string? actionText = isShopRole ? "Xử lý trả hàng" : "Xem chi tiết trả hàng";

            if (matchedCode != null && orderMap != null && orderMap.TryGetValue(matchedCode, out var info))
            {
                bool actAsSeller = isShopRole && (currentAccountId == 0 || info.ShopId == currentAccountId);
                if (actAsSeller)
                {
                    url = info.ReturnId.HasValue ? $"/Admin/ReturnRequests/Details?id={info.ReturnId.Value}" : $"/Admin/Orders/Details?id={info.OrderId}";
                }
                else
                {
                    url = $"/my-orders/return/{info.OrderId}";
                }
            }

            return ("return", "Trả hàng & Hoàn tiền", "hgi-package-process", url, actionText);
        }

        // 2. Khiếu nại / Tranh chấp
        if (combined.Contains("khiếu nại", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("tranh chấp", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("KN-", StringComparison.OrdinalIgnoreCase))
        {
            string? url = isShopRole ? "/Admin/Disputes/Index" : "/my-disputes";
            string? actionText = isShopRole ? "Phản hồi khiếu nại" : "Xem hồ sơ khiếu nại";

            if (matchedCode != null && orderMap != null && orderMap.TryGetValue(matchedCode, out var info) && info.DisputeId.HasValue)
            {
                bool actAsSeller = isShopRole && (currentAccountId == 0 || info.ShopId == currentAccountId);
                url = actAsSeller ? $"/Admin/Disputes/Details?id={info.DisputeId.Value}" : $"/my-disputes/details/{info.DisputeId.Value}";
            }

            return ("dispute", "Khiếu nại & Trọng tài", "hgi-justice-scale-01", url, actionText);
        }

        // 3a. Yêu cầu rút tiền / Duyệt giải ngân
        if (combined.Contains("lệnh rút", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("rút doanh thu", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("WD-", StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(userRole, "Admin", StringComparison.OrdinalIgnoreCase))
            {
                return ("wallet", "Duyệt Rút tiền", "hgi-wallet-02", "/Admin/Payouts/Index", "Duyệt lệnh rút tiền");
            }
            return ("wallet", "Ví & Rút tiền", "hgi-wallet-02", "/Admin/SellerWallet/Index", "Xem Ví Doanh thu");
        }

        // 3b. Quyết toán tự động cho Gian hàng
        if (combined.Contains("quyết toán đơn", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("Ví Gian hàng", StringComparison.OrdinalIgnoreCase))
        {
            return ("wallet", "Quyết toán Doanh thu", "hgi-wallet-02", "/Admin/SellerWallet/Index", "Xem Ví Doanh thu");
        }

        // 3c. Cảnh báo tồn kho thấp cho Gian hàng
        if (combined.Contains("Sắp hết hàng", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("bổ sung tồn kho", StringComparison.OrdinalIgnoreCase))
        {
            return ("order", "Cảnh báo Tồn kho", "hgi-package-box", "/Admin/Products/Inventory", "Bổ sung tồn kho");
        }

        // 3d. Kiểm duyệt sản phẩm & đánh giá
        if (combined.Contains("tạm ẩn bởi Quản trị", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("khôi phục hiển thị", StringComparison.OrdinalIgnoreCase))
        {
            string modUrl = string.Equals(userRole, "Admin", StringComparison.OrdinalIgnoreCase)
                ? "/Admin/Moderation/Index"
                : "/Admin/Products/Index";
            return ("system", "Kiểm duyệt Sản phẩm", "hgi-shield-01", modUrl, "Kiểm tra sản phẩm");
        }

        // 3e. Ví & Giải ngân / Hoàn tiền
        if (combined.Contains("Giải ngân", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("Hoàn tiền", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("Ví Sellzy", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("Ví Doanh thu", StringComparison.OrdinalIgnoreCase))
        {
            string? url = isShopRole && combined.Contains("Giải ngân", StringComparison.OrdinalIgnoreCase)
                ? "/Admin/SellerWallet/Index"
                : "/my-wallet";
            string? actionText = isShopRole && combined.Contains("Giải ngân", StringComparison.OrdinalIgnoreCase)
                ? "Xem Ví Doanh thu"
                : "Kiểm tra số dư Ví";

            if (matchedCode != null && orderMap != null && orderMap.TryGetValue(matchedCode, out var info))
            {
                bool actAsSeller = isShopRole && (currentAccountId == 0 || info.ShopId == currentAccountId) && combined.Contains("Giải ngân", StringComparison.OrdinalIgnoreCase);
                if (actAsSeller) url = $"/Admin/Orders/Details?id={info.OrderId}";
            }

            return ("wallet", "Ví & Tài chính", "hgi-wallet-02", url, actionText);
        }

        // 4. Đơn hàng (Đơn mới cho Shop, hoặc cập nhật trạng thái đơn cho Khách)
        if (combined.Contains("Đơn hàng", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("vận chuyển", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("vận đơn", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("E-Voucher", StringComparison.OrdinalIgnoreCase) ||
            matchedCode != null)
        {
            bool isSellerOrderAlert = combined.Contains("Gian hàng", StringComparison.OrdinalIgnoreCase) ||
                                      combined.Contains("đơn hàng mới", StringComparison.OrdinalIgnoreCase) ||
                                      combined.Contains("Khách hàng đã hủy", StringComparison.OrdinalIgnoreCase) ||
                                      combined.Contains("Shop cần xác nhận", StringComparison.OrdinalIgnoreCase);

            string? url = (isShopRole && isSellerOrderAlert) ? "/Admin/Orders/Index" : "/my-orders";
            string? actionText = (isShopRole && isSellerOrderAlert) ? "Quản lý đơn bán" : "Xem đơn hàng";

            if (matchedCode != null && orderMap != null && orderMap.TryGetValue(matchedCode, out var info))
            {
                bool actAsSeller = isShopRole && (info.ShopId == currentAccountId || (currentAccountId == 0 && isSellerOrderAlert));
                if (actAsSeller)
                {
                    url = $"/Admin/Orders/Details?id={info.OrderId}";
                    actionText = "Xử lý đơn trong Kênh Bán";
                }
                else
                {
                    url = $"/my-orders/{info.OrderId}";
                    actionText = "Xem chi tiết đơn mua";
                }
            }

            return ("order", isSellerOrderAlert ? "Đơn bán hàng" : "Đơn hàng", "hgi-package-box", url, actionText);
        }

        // 5. Khuyến mãi / Flash Sale / Voucher / Shop theo dõi tung mã mới
        if (combined.Contains("Khuyến mãi", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("Flash Sale", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("FlashSale", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("Voucher", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("tung mã mới", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("Ưu đãi", StringComparison.OrdinalIgnoreCase))
        {
            return ("promo", "Ưu đãi & Khuyến mãi", "hgi-ticket-01", "/vouchers", "Săn mã ngay");
        }

        return ("system", "Hệ thống Sellzy", "hgi-notification-02", null, null);
    }
}

public class NotificationViewModel
{
    public bool IsSignedIn { get; set; }
    public int AccountId { get; set; }
    public string UserRole { get; set; } = "Customer";
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
    public string CategoryKey { get; set; } = "system";
    public string CategoryLabel { get; set; } = "Hệ thống";
    public string IconClass { get; set; } = "hgi-notification-02";
    public string? ActionUrl { get; set; }
    public string? ActionText { get; set; }
}
