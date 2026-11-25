using System.Text;
using DOAN_BANHANG.Helpers;
using DOAN_BANHANG.Models;
using Microsoft.EntityFrameworkCore;

namespace DOAN_BANHANG.Services.AiChatbot;

/// <summary>
/// Bộ công cụ RAG truy vấn dữ liệu thật từ SQL Server qua EF Core cho Trợ lý AI Sellzy (đa ngành).
/// Mọi truy vấn dữ liệu cá nhân đều kiểm tra đúng AccountId để chống truy cập trái phép (IDOR).
/// </summary>
public class SellzyAiTools
{
    private readonly DataContext _db;

    public SellzyAiTools(DataContext db)
    {
        _db = db;
    }

    private static bool IsVariantOnSale(ProductVariant v, DateTime utcNow)
    {
        if (!v.SalePrice.HasValue || v.SalePrice.Value <= 0 || v.SalePrice.Value >= v.Price)
            return false;
        if (v.SaleStartDate.HasValue && v.SaleStartDate.Value > utcNow)
            return false;
        if (v.SaleEndDate.HasValue && v.SaleEndDate.Value < utcNow)
            return false;
        return true;
    }

    private static decimal EffectivePrice(ProductVariant v, DateTime utcNow)
        => IsVariantOnSale(v, utcNow) ? v.SalePrice!.Value : v.Price;

    private static AiProductCard MapProductCard(Product p, DateTime utcNow)
    {
        var activeVariants = p.Variants.Where(v => v.IsActive && !v.IsDeleted).ToList();
        var prices = activeVariants.Select(v => EffectivePrice(v, utcNow)).ToList();
        var minPrice = prices.Count > 0 ? (int)Math.Round(prices.Min()) : 0;
        var maxPrice = prices.Count > 0 ? (int)Math.Round(prices.Max()) : minPrice;
        var isSale = activeVariants.Any(v => IsVariantOnSale(v, utcNow));
        var primaryImg = p.Images
            .OrderByDescending(i => i.IsPrimary)
            .ThenBy(i => i.SortOrder)
            .Select(i => i.ImageUrl)
            .FirstOrDefault();

        return new AiProductCard
        {
            Id = p.Id,
            Name = p.Name,
            Slug = p.Slug,
            ImageUrl = ImageUrlHelper.NormalizeStaticFileUrl(primaryImg),
            MinPrice = minPrice,
            MaxPrice = maxPrice,
            Rating = (double)p.AverageRating,
            CategoryName = p.Category?.Name,
            BrandName = p.Brand?.Name,
            IsMall = p.ShopAccount?.Profile?.IsMall == true,
            IsFlashSale = isSale
        };
    }

    public async Task<AiToolExecutionOutput> SearchProductsAsync(
        string? query = null,
        string? category = null,
        string? brand = null,
        string? optionFilter = null,
        decimal? minPrice = null,
        decimal? maxPrice = null,
        bool onlyFlashSale = false,
        bool onlyMall = false,
        int limit = 5,
        CancellationToken ct = default)
    {
        var output = new AiToolExecutionOutput();
        var utcNow = DateTime.UtcNow;
        limit = Math.Clamp(limit, 1, 8);

        var baseQuery = _db.Products
            .AsNoTracking()
            .Include(p => p.Category)!.ThenInclude(c => c!.Parent)
            .Include(p => p.Brand)
            .Include(p => p.Images)
            .Include(p => p.ShopAccount)!.ThenInclude(a => a!.Profile)
            .Include(p => p.Variants.Where(v => v.IsActive && !v.IsDeleted))
                .ThenInclude(v => v.OptionValue1)
            .Include(p => p.Variants.Where(v => v.IsActive && !v.IsDeleted))
                .ThenInclude(v => v.OptionValue2)
            .Where(p => !p.IsDeleted && p.Status == "Active");

        if (onlyMall)
            baseQuery = baseQuery.Where(p => p.ShopAccount != null && p.ShopAccount.Profile != null && p.ShopAccount.Profile.IsMall);

        if (!string.IsNullOrWhiteSpace(category))
        {
            var catTerm = category.Trim();
            baseQuery = baseQuery.Where(p =>
                p.Category != null && (
                    EF.Functions.Like(p.Category.Name, $"%{catTerm}%") ||
                    (p.Category.Parent != null && EF.Functions.Like(p.Category.Parent.Name, $"%{catTerm}%"))));
        }

        if (!string.IsNullOrWhiteSpace(brand))
        {
            var brandTerm = brand.Trim();
            baseQuery = baseQuery.Where(p => p.Brand != null && EF.Functions.Like(p.Brand.Name, $"%{brandTerm}%"));
        }

        List<Product> candidates;

        if (!string.IsNullOrWhiteSpace(query))
        {
            var q = query.Trim();
            var expandedTerms = ExpandMultiIndustrySynonyms(q);
            if (expandedTerms.Count == 1)
            {
                candidates = await baseQuery
                    .Where(p =>
                        EF.Functions.Like(p.Name, $"%{q}%") ||
                        (p.ShortDescription != null && EF.Functions.Like(p.ShortDescription, $"%{q}%")) ||
                        (p.Category != null && EF.Functions.Like(p.Category.Name, $"%{q}%")) ||
                        (p.Category != null && p.Category.Parent != null && EF.Functions.Like(p.Category.Parent.Name, $"%{q}%")) ||
                        (p.Brand != null && EF.Functions.Like(p.Brand.Name, $"%{q}%")))
                    .OrderByDescending(p => p.AverageRating)
                    .ThenByDescending(p => p.ReviewCount)
                    .Take(60)
                    .ToListAsync(ct);
            }
            else
            {
                // Tìm toàn bộ ID khớp bộ từ đồng nghĩa trực tiếp trong DB (không bị cắt bởi Take(120))
                var allLight = await _db.Products
                    .AsNoTracking()
                    .Where(p => !p.IsDeleted && p.Status == "Active")
                    .Select(p => new
                    {
                        p.Id,
                        p.Name,
                        p.ShortDescription,
                        CatName = p.Category != null ? p.Category.Name : "",
                        ParentCatName = p.Category != null && p.Category.Parent != null ? p.Category.Parent.Name : "",
                        BrandName = p.Brand != null ? p.Brand.Name : ""
                    })
                    .ToListAsync(ct);

                var matchedIds = allLight
                    .Where(p =>
                    {
                        var hay = $"{p.Name} {p.CatName} {p.ParentCatName} {p.BrandName} {p.ShortDescription}".ToLowerInvariant();
                        return expandedTerms.Any(t => hay.Contains(t, StringComparison.OrdinalIgnoreCase));
                    })
                    .Select(p => p.Id)
                    .Take(60)
                    .ToList();

                candidates = matchedIds.Count == 0
                    ? new List<Product>()
                    : await baseQuery
                        .Where(p => matchedIds.Contains(p.Id))
                        .OrderByDescending(p => p.AverageRating)
                        .ThenByDescending(p => p.ReviewCount)
                        .ToListAsync(ct);
            }
        }
        else
        {
            candidates = await baseQuery
                .OrderByDescending(p => p.AverageRating)
                .ThenByDescending(p => p.ReviewCount)
                .Take(60)
                .ToListAsync(ct);
        }

        var result = BuildSearchOutput(candidates, optionFilter, minPrice, maxPrice, onlyFlashSale, limit, utcNow);

        // Nếu thực sự không có sản phẩm nào khớp trong DB → gợi ý danh mục đang bán + top sản phẩm nổi bật
        if (result.Products.Count == 0 && !string.IsNullOrWhiteSpace(query))
        {
            var topCats = await _db.Products
                .AsNoTracking()
                .Where(p => !p.IsDeleted && p.Status == "Active" && p.Category != null)
                .Select(p => p.Category!.Name)
                .Distinct()
                .Take(8)
                .ToListAsync(ct);

            var featured = await _db.Products
                .AsNoTracking()
                .Include(p => p.Category)
                .Include(p => p.Brand)
                .Include(p => p.Images)
                .Include(p => p.ShopAccount)!.ThenInclude(a => a!.Profile)
                .Include(p => p.Variants.Where(v => v.IsActive && !v.IsDeleted))
                .Where(p => !p.IsDeleted && p.Status == "Active")
                .OrderByDescending(p => p.AverageRating)
                .ThenByDescending(p => p.ReviewCount)
                .Take(3)
                .ToListAsync(ct);

            var sb = new StringBuilder();
            sb.AppendLine($"Hiện trên sàn Sellzy chưa có sản phẩm nào khớp với **\"{query.Trim()}\"**.");
            if (topCats.Count > 0)
                sb.AppendLine($"Các danh mục đang có hàng trên sàn gồm: **{string.Join(", ", topCats)}**.");
            if (featured.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Bạn có thể tham khảo một số sản phẩm nổi bật đang bán chạy:");
                for (int i = 0; i < featured.Count; i++)
                {
                    var card = MapProductCard(featured[i], utcNow);
                    result.Products.Add(card);
                    var priceText = card.MinPrice == card.MaxPrice ? $"{card.MinPrice:N0}đ" : $"{card.MinPrice:N0}đ – {card.MaxPrice:N0}đ";
                    sb.AppendLine($"{i + 1}. **{featured[i].Name}** (ID: {featured[i].Id}) — Giá: **{priceText}** | Đánh giá: {featured[i].AverageRating:0.0}⭐");
                }
            }
            result.Text = sb.ToString().Trim();
        }

        return result;
    }

    private static List<string> ExpandMultiIndustrySynonyms(string query)
    {
        var q = query.Trim().ToLowerInvariant();
        var map = new Dictionary<string, string[]>
        {
            ["điện thoại"] = new[] { "điện thoại", "iphone", "samsung", "galaxy", "xiaomi", "oppo", "vivo", "realme", "smartphone", "phone" },
            ["dien thoai"] = new[] { "điện thoại", "iphone", "samsung", "galaxy", "xiaomi", "oppo", "vivo", "realme", "smartphone", "phone" },
            ["laptop"] = new[] { "laptop", "macbook", "notebook", "thinkpad", "vivobook", "zenbook", "máy tính xách tay" },
            ["máy tính"] = new[] { "máy tính", "laptop", "macbook", "pc", "ipad", "tablet" },
            ["tai nghe"] = new[] { "tai nghe", "airpods", "headphone", "earbud", "buds" },
            ["đồng hồ"] = new[] { "đồng hồ", "watch", "smartwatch", "apple watch" },
            ["mỹ phẩm"] = new[] { "mỹ phẩm", "son", "kem", "serum", "sữa rửa mặt", "nước hoa", "chống nắng" },
            ["gia dụng"] = new[] { "gia dụng", "nồi", "máy lọc", "quạt", "hút bụi", "bếp", "máy sấy" },
            ["áo"] = new[] { "áo", "shirt", "t-shirt", "tee", "hoodie", "jacket", "polo" },
            ["quần"] = new[] { "quần", "jean", "kaki", "short", "trouser", "pants" },
            ["giày"] = new[] { "giày", "sneaker", "shoes", "sandal", "boot" }
        };

        foreach (var kv in map)
        {
            if (q.Contains(kv.Key, StringComparison.OrdinalIgnoreCase))
                return kv.Value.ToList();
        }
        return new List<string> { q };
    }

    private static AiToolExecutionOutput BuildSearchOutput(
        List<Product> candidates,
        string? optionFilter,
        decimal? minPrice,
        decimal? maxPrice,
        bool onlyFlashSale,
        int limit,
        DateTime utcNow)
    {
        var output = new AiToolExecutionOutput();

        bool MatchesFilters(Product p, bool enforcePrice)
        {
            var variants = p.Variants.Where(v => v.IsActive && !v.IsDeleted).ToList();
            if (variants.Count == 0) return false;
            if (onlyFlashSale && !variants.Any(v => IsVariantOnSale(v, utcNow))) return false;
            if (enforcePrice && (minPrice.HasValue || maxPrice.HasValue))
            {
                var okPrice = variants.Any(v =>
                {
                    var eff = EffectivePrice(v, utcNow);
                    return (!minPrice.HasValue || eff >= minPrice.Value) &&
                           (!maxPrice.HasValue || eff <= maxPrice.Value);
                });
                if (!okPrice) return false;
            }
            if (!string.IsNullOrWhiteSpace(optionFilter))
            {
                var opt = optionFilter.Trim().ToLowerInvariant();
                var okOpt = variants.Any(v =>
                    (v.OptionValue1?.Value != null && v.OptionValue1.Value.ToLowerInvariant().Contains(opt)) ||
                    (v.OptionValue2?.Value != null && v.OptionValue2.Value.ToLowerInvariant().Contains(opt)));
                if (!okOpt) return false;
            }
            return true;
        }

        var filtered = candidates.Where(p => MatchesFilters(p, enforcePrice: true)).Take(limit).ToList();
        var relaxedPrice = false;

        // Nếu có sản phẩm đúng loại nhưng ngoài khoảng giá khách chọn, gợi ý các mẫu gần nhất thay vì báo trống
        if (filtered.Count == 0 && (minPrice.HasValue || maxPrice.HasValue) && candidates.Count > 0)
        {
            filtered = candidates.Where(p => MatchesFilters(p, enforcePrice: false)).Take(limit).ToList();
            relaxedPrice = filtered.Count > 0;
        }

        if (filtered.Count == 0)
        {
            output.Text = "Không tìm thấy sản phẩm nào khớp hoàn toàn với tiêu chí hiện tại trên sàn Sellzy.";
            output.QuickLinks.Add(new AiQuickLink { Label = "Xem tất cả sản phẩm", Url = "/products" });
            return output;
        }

        var sb = new StringBuilder();
        sb.AppendLine(relaxedPrice
            ? $"Hiện chưa có mẫu nằm đúng khoảng giá bạn chọn, nhưng đây là {filtered.Count} sản phẩm cùng dòng gần nhất trên Sellzy:"
            : $"Tìm thấy {filtered.Count} sản phẩm phù hợp trên Sellzy:");

        for (int i = 0; i < filtered.Count; i++)
        {
            var p = filtered[i];
            var card = MapProductCard(p, utcNow);
            output.Products.Add(card);

            var priceText = card.MinPrice == card.MaxPrice
                ? $"{card.MinPrice:N0}đ"
                : $"{card.MinPrice:N0}đ – {card.MaxPrice:N0}đ";

            var tags = new List<string>();
            if (card.IsMall) tags.Add("Sellzy Mall");
            if (card.IsFlashSale) tags.Add("Flash Sale");
            if (!string.IsNullOrWhiteSpace(card.BrandName)) tags.Add(card.BrandName!);
            if (!string.IsNullOrWhiteSpace(card.CategoryName)) tags.Add(card.CategoryName!);

            var tagStr = tags.Count > 0 ? $" [{string.Join(" • ", tags)}]" : "";
            var options = p.Variants
                .Where(v => v.IsActive && !v.IsDeleted)
                .Select(v => v.ShortVariantLabel)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct()
                .Take(5)
                .ToList();
            var optStr = options.Count > 0 ? $" | Phân loại: {string.Join(", ", options)}" : "";

            sb.AppendLine($"{i + 1}. **{p.Name}** (ID: {p.Id}){tagStr} — Giá: **{priceText}** | Đánh giá: {p.AverageRating:0.0}⭐ ({p.ReviewCount} đánh giá){optStr}");
        }

        output.Text = sb.ToString().Trim();
        return output;
    }

    public async Task<AiToolExecutionOutput> GetMyRecentOrdersAsync(int accountId, int take = 5, CancellationToken ct = default)
    {
        var output = new AiToolExecutionOutput();
        if (accountId <= 0)
        {
            output.Text = "Bạn cần đăng nhập tài khoản Sellzy để tra cứu đơn hàng cá nhân.";
            return output;
        }

        take = Math.Clamp(take, 1, 10);
        var orders = await _db.Orders
            .AsNoTracking()
            .Where(o => o.CustomerId == accountId)
            .OrderByDescending(o => o.Id)
            .Take(take)
            .Select(o => new
            {
                o.Id,
                o.OrderCode,
                o.OrderStatus,
                o.PaymentMethod,
                o.PaymentStatus,
                o.FinalAmount,
                o.TrackingCode
            })
            .ToListAsync(ct);

        if (orders.Count == 0)
        {
            output.Text = "Bạn chưa có đơn hàng nào trên Sellzy.";
            output.QuickLinks.Add(new AiQuickLink { Label = "Khám phá sản phẩm", Url = "/products" });
            return output;
        }

        var sb = new StringBuilder();
        sb.AppendLine($"Danh sách {orders.Count} đơn hàng gần nhất của bạn:");
        foreach (var o in orders)
        {
            var tracking = string.IsNullOrWhiteSpace(o.TrackingCode) ? "" : $" | Vận đơn: `{o.TrackingCode}`";
            sb.AppendLine($"- Mã **{o.OrderCode}**: Trạng thái **{TranslateOrderStatus(o.OrderStatus)}** | Thanh toán: {o.PaymentMethod} ({o.PaymentStatus}) | Tổng: **{o.FinalAmount:N0}đ**{tracking}");
        }

        output.Text = sb.ToString().Trim();
        output.QuickLinks.Add(new AiQuickLink { Label = "Quản lý đơn hàng", Url = "/my-account/orders" });
        return output;
    }

    public async Task<AiToolExecutionOutput> GetOrderStatusAsync(int accountId, string orderCode, CancellationToken ct = default)
    {
        var output = new AiToolExecutionOutput();
        if (accountId <= 0)
        {
            output.Text = "Bạn cần đăng nhập để tra cứu chi tiết đơn hàng của mình.";
            return output;
        }

        var code = (orderCode ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(code))
            return await GetMyRecentOrdersAsync(accountId, 5, ct);

        // Bảo mật: CHỈ cho phép xem đơn hàng thuộc đúng CustomerId đang đăng nhập
        var order = await _db.Orders
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.CustomerId == accountId && o.OrderCode == code, ct);

        if (order == null)
        {
            output.Text = $"Không tìm thấy đơn hàng mã **{code}** thuộc tài khoản của bạn. Vui lòng kiểm tra lại mã đơn.";
            output.QuickLinks.Add(new AiQuickLink { Label = "Xem tất cả đơn hàng", Url = "/my-account/orders" });
            return output;
        }

        var logs = await _db.OrderTrackingLogs
            .AsNoTracking()
            .Where(l => l.OrderId == order.Id)
            .OrderByDescending(l => l.CreatedAt)
            .Take(4)
            .ToListAsync(ct);

        var sb = new StringBuilder();
        sb.AppendLine($"**Đơn hàng {order.OrderCode}**:");
        sb.AppendLine($"- Trạng thái: **{TranslateOrderStatus(order.OrderStatus)}**");
        sb.AppendLine($"- Thanh toán: {order.PaymentMethod} (**{order.PaymentStatus}**) — Tổng tiền: **{order.FinalAmount:N0}đ**");
        if (!string.IsNullOrWhiteSpace(order.TrackingCode))
            sb.AppendLine($"- Mã vận đơn: **{order.TrackingCode}**");

        if (logs.Count > 0)
        {
            sb.AppendLine("- Hành trình gần nhất:");
            foreach (var l in logs)
            {
                var localTime = l.CreatedAt.AddHours(7).ToString("dd/MM HH:mm");
                var safeNote = SellzyAiGuardrails.WrapUntrustedData(l.Note, 180);
                sb.AppendLine($"  • [{localTime}] {l.Status}: {safeNote}");
            }
        }

        output.Text = sb.ToString().Trim();
        output.QuickLinks.Add(new AiQuickLink { Label = $"Chi tiết đơn {order.OrderCode}", Url = $"/my-account/orders/{order.Id}" });
        return output;
    }

    public async Task<AiToolExecutionOutput> GetMyCartAsync(int accountId, CancellationToken ct = default)
    {
        var output = new AiToolExecutionOutput();
        if (accountId <= 0)
        {
            output.Text = "Bạn cần đăng nhập để xem giỏ hàng cá nhân.";
            return output;
        }

        var utcNow = DateTime.UtcNow;
        var items = await _db.CartItems
            .AsNoTracking()
            .Include(ci => ci.Cart)
            .Include(ci => ci.Variant)!.ThenInclude(v => v!.Product)
            .Include(ci => ci.Variant)!.ThenInclude(v => v!.OptionValue1)
            .Include(ci => ci.Variant)!.ThenInclude(v => v!.OptionValue2)
            .Where(ci => ci.Cart != null && ci.Cart.AccountId == accountId && ci.Variant != null && ci.Variant.Product != null)
            .ToListAsync(ct);

        if (items.Count == 0)
        {
            output.Text = "Giỏ hàng của bạn hiện đang trống.";
            output.QuickLinks.Add(new AiQuickLink { Label = "Mua sắm ngay", Url = "/products" });
            return output;
        }

        var sb = new StringBuilder();
        sb.AppendLine($"Giỏ hàng của bạn đang có **{items.Sum(i => i.Quantity)} sản phẩm**:");
        decimal total = 0m;
        foreach (var item in items)
        {
            var price = EffectivePrice(item.Variant!, utcNow);
            var lineTotal = price * item.Quantity;
            total += lineTotal;
            var label = string.IsNullOrWhiteSpace(item.Variant!.ShortVariantLabel)
                ? ""
                : $" ({item.Variant.ShortVariantLabel})";
            sb.AppendLine($"- **{item.Variant.Product!.Name}**{label} × {item.Quantity} = **{lineTotal:N0}đ**");
        }
        sb.AppendLine($"Tổng tạm tính: **{total:N0}đ**");

        output.Text = sb.ToString().Trim();
        output.QuickLinks.Add(new AiQuickLink { Label = "Mở giỏ hàng", Url = "/cart" });
        output.QuickLinks.Add(new AiQuickLink { Label = "Thanh toán ngay", Url = "/checkout" });
        return output;
    }

    public async Task<AiToolExecutionOutput> GetMyWalletAndDisputesAsync(int accountId, CancellationToken ct = default)
    {
        var output = new AiToolExecutionOutput();
        if (accountId <= 0)
        {
            output.Text = "Bạn cần đăng nhập để xem số dư Ví Sellzy và trạng thái khiếu nại.";
            return output;
        }

        var wallet = await _db.Wallets
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.UserId == accountId, ct);

        var recentTxns = wallet == null
            ? new List<WalletTransaction>()
            : await _db.WalletTransactions
                .AsNoTracking()
                .Where(t => t.WalletId == wallet.WalletId)
                .OrderByDescending(t => t.CreatedAt)
                .Take(3)
                .ToListAsync(ct);

        var disputes = await _db.OrderDisputes
            .AsNoTracking()
            .Include(d => d.Order)
            .Where(d => d.CustomerId == accountId)
            .OrderByDescending(d => d.CreatedAt)
            .Take(3)
            .ToListAsync(ct);

        var sb = new StringBuilder();
        var balance = wallet?.Balance ?? 0m;
        sb.AppendLine($"**Ví Sellzy của bạn**: Số dư hiện tại là **{balance:N0}đ**.");
        if (recentTxns.Count > 0)
        {
            sb.AppendLine("Giao dịch ví gần nhất:");
            foreach (var t in recentTxns)
            {
                var sign = t.Amount >= 0 ? "+" : "";
                sb.AppendLine($"- [{t.CreatedAt.AddHours(7):dd/MM HH:mm}] {t.TransactionType}: **{sign}{t.Amount:N0}đ** ({t.Description})");
            }
        }

        if (disputes.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("**Khiếu nại đơn hàng gần đây**:");
            foreach (var d in disputes)
            {
                var safeReason = SellzyAiGuardrails.WrapUntrustedData(d.ReasonTitle, 120);
                sb.AppendLine($"- Mã **{d.DisputeCode}** (Đơn {d.Order?.OrderCode}): {safeReason} — Trạng thái: **{TranslateDisputeStatus(d.Status)}**");
            }
        }
        else
        {
            sb.AppendLine("- Hiện bạn không có khiếu nại đơn hàng nào đang mở.");
        }

        output.Text = sb.ToString().Trim();
        output.QuickLinks.Add(new AiQuickLink { Label = "Mở Ví Sellzy", Url = "/my-wallet" });
        output.QuickLinks.Add(new AiQuickLink { Label = "Trung tâm khiếu nại", Url = "/disputes" });
        return output;
    }

    public async Task<AiToolExecutionOutput> GetActiveVouchersAndFlashSalesAsync(CancellationToken ct = default)
    {
        var output = new AiToolExecutionOutput();
        var utcNow = DateTime.UtcNow;

        var vouchers = await _db.Vouchers
            .AsNoTracking()
            .Where(v => v.IsActive
                        && v.StartDate <= utcNow
                        && v.EndDate >= utcNow
                        && (v.UsageLimit == 0 || v.UsedCount < v.UsageLimit))
            .OrderByDescending(v => v.DiscountValue)
            .Take(5)
            .ToListAsync(ct);

        var flashSaleSearch = await SearchProductsAsync(onlyFlashSale: true, limit: 4, ct: ct);
        output.Products.AddRange(flashSaleSearch.Products);

        var sb = new StringBuilder();
        if (vouchers.Count > 0)
        {
            sb.AppendLine("**Mã giảm giá (Voucher) đang hiệu lực:**");
            foreach (var v in vouchers)
            {
                var desc = string.Equals(v.DiscountType, "Percent", StringComparison.OrdinalIgnoreCase)
                    ? $"Giảm **{v.DiscountValue:0.#}%**" + (v.MaxDiscountValue.HasValue ? $" (tối đa {v.MaxDiscountValue.Value:N0}đ)" : "")
                    : $"Giảm **{v.DiscountValue:N0}đ**";
                var minOrder = v.MinOrderValue > 0 ? $" cho đơn từ {v.MinOrderValue:N0}đ" : "";
                sb.AppendLine($"- Mã **`{v.Code}`**: {desc}{minOrder} (HSD: {v.EndDate.AddHours(7):dd/MM/yyyy})");
            }
        }
        else
        {
            sb.AppendLine("Hiện chưa có mã Voucher công khai nào đang mở.");
        }

        sb.AppendLine();
        if (flashSaleSearch.Products.Count > 0)
        {
            sb.AppendLine("**Sản phẩm Flash Sale nổi bật hôm nay:**");
            sb.AppendLine(flashSaleSearch.Text);
        }
        else
        {
            sb.AppendLine("Hiện chưa có khung giờ Flash Sale nào đang chạy, nhưng bạn có thể xem thêm nhiều sản phẩm ưu đãi trên trang chủ.");
        }

        output.Text = sb.ToString().Trim();
        output.QuickLinks.Add(new AiQuickLink { Label = "Săn ưu đãi ngay", Url = "/products" });
        return output;
    }

    public async Task<AiToolExecutionOutput> GetMyProfileAsync(int accountId, CancellationToken ct = default)
    {
        var output = new AiToolExecutionOutput();
        if (accountId <= 0)
        {
            output.Text = "Bạn chưa đăng nhập nên mình chưa xem được thông tin tài khoản.";
            return output;
        }

        var acc = await _db.Accounts
            .AsNoTracking()
            .Include(a => a.Profile)
            .FirstOrDefaultAsync(a => a.Id == accountId && !a.IsDeleted, ct);

        if (acc == null)
        {
            output.Text = "Không tìm thấy thông tin tài khoản của bạn.";
            return output;
        }

        var sb = new StringBuilder();
        sb.AppendLine("**Thông tin tài khoản Sellzy của bạn (đã che một phần để bảo mật):**");
        sb.AppendLine($"- Email: **{SellzyAiGuardrails.MaskEmail(acc.Email)}**");
        if (!string.IsNullOrWhiteSpace(acc.Profile?.FullName))
            sb.AppendLine($"- Họ tên: **{acc.Profile.FullName}**");
        if (!string.IsNullOrWhiteSpace(acc.Profile?.PhoneNumber))
            sb.AppendLine($"- Số điện thoại: {SellzyAiGuardrails.MaskPhone(acc.Profile.PhoneNumber)}");
        if (!string.IsNullOrWhiteSpace(acc.Profile?.Address))
            sb.AppendLine($"- Địa chỉ: {acc.Profile.Address}");
        sb.AppendLine($"- Vai trò: {acc.Role} | Trạng thái: {acc.Status}");

        output.Text = sb.ToString().Trim();
        output.QuickLinks.Add(new AiQuickLink { Label = "Trang tài khoản", Url = "/my-account" });
        return output;
    }

    /// <summary>
    /// RAG chính sách động: Đọc trực tiếp cấu hình chính sách từ bảng SystemSettings trong DB
    /// để AI luôn trả lời đúng số liệu hiện hành, không tự bịa chính sách đổi trả hay phí sàn.
    /// </summary>
    public async Task<AiToolExecutionOutput> GetStorePoliciesAsync(string? topic = null, CancellationToken ct = default)
    {
        var output = new AiToolExecutionOutput();
        var settings = await _db.SystemSettings
            .AsNoTracking()
            .ToDictionaryAsync(s => s.Id, s => s.Value, StringComparer.OrdinalIgnoreCase, ct);

        string GetSetting(string key, string fallback)
            => settings.TryGetValue(key, out var val) && !string.IsNullOrWhiteSpace(val) ? val : fallback;

        var returnDays = GetSetting("ReturnWindowDays", "7");
        var mallSurcharge = GetSetting("Fee_Mall_Surcharge", "2");
        var paymentFee = GetSetting("Fee_Payment_Gateway", "2");

        var sb = new StringBuilder();
        sb.AppendLine("**Chính sách chính thức hiện hành của Sàn TMĐT Sellzy (dữ liệu thực từ hệ thống):**");
        sb.AppendLine($"- **Đổi trả & Hoàn tiền**: Hỗ trợ yêu cầu trả hàng/hoàn tiền trong vòng **{returnDays} ngày** kể từ khi nhận hàng nếu sản phẩm lỗi, hư hỏng, sai mẫu hoặc thiếu số lượng (nên có video mở hộp).");
        sb.AppendLine("- **Trọng tài khiếu nại 3 bên**: Nếu gian hàng từ chối hoặc phản hồi chưa thỏa đáng, khách có thể mở **Khiếu nại đơn hàng** để Ban quản trị Sellzy phân xử độc lập và hoàn tiền trực tiếp vào **Ví Sellzy** khi hợp lệ.");
        sb.AppendLine("- **Phương thức thanh toán**: Hỗ trợ thanh toán khi nhận hàng (**COD**), cổng thanh toán **VNPAY** và số dư **Ví Sellzy**.");
        sb.AppendLine("- **Sellzy Mall (Chính hãng)**: Gian hàng có nhãn Mall đã qua thẩm định giấy phép kinh doanh & ủy quyền thương hiệu, cam kết hàng chính hãng và chế độ bảo hành minh bạch.");
        sb.AppendLine($"- **Thông số đối soát đối tác (tham khảo)**: Phí cổng thanh toán {paymentFee}%, phụ phí gian hàng Sellzy Mall +{mallSurcharge}%.");

        output.Text = sb.ToString().Trim();
        output.QuickLinks.Add(new AiQuickLink { Label = "Trung tâm khiếu nại", Url = "/disputes" });
        output.QuickLinks.Add(new AiQuickLink { Label = "Đăng ký bán hàng", Url = "/partners" });
        return output;
    }

    public async Task<AiToolExecutionOutput> CompareProductsAsync(string product1, string product2, CancellationToken ct = default)
    {
        var output = new AiToolExecutionOutput();
        var utcNow = DateTime.UtcNow;

        var res1 = await SearchProductsAsync(query: product1, limit: 1, ct: ct);
        var res2 = await SearchProductsAsync(query: product2, limit: 1, ct: ct);

        if (res1.Products.Count == 0 && res2.Products.Count == 0)
        {
            output.Text = $"Mình chưa tìm thấy sản phẩm khớp với '{product1}' và '{product2}' để so sánh.";
            return output;
        }

        var ids = res1.Products.Select(p => p.Id).Concat(res2.Products.Select(p => p.Id)).Distinct().ToList();
        var fullProducts = await _db.Products
            .AsNoTracking()
            .Include(p => p.Category)
            .Include(p => p.Brand)
            .Include(p => p.Images)
            .Include(p => p.ShopAccount)!.ThenInclude(a => a!.Profile)
            .Include(p => p.Variants.Where(v => v.IsActive && !v.IsDeleted))
            .Where(p => ids.Contains(p.Id))
            .ToListAsync(ct);

        var sb = new StringBuilder();
        sb.AppendLine("**So sánh nhanh sản phẩm trên Sellzy:**");
        for (int i = 0; i < fullProducts.Count; i++)
        {
            var p = fullProducts[i];
            var card = MapProductCard(p, utcNow);
            output.Products.Add(card);
            var priceStr = card.MinPrice == card.MaxPrice ? $"{card.MinPrice:N0}đ" : $"{card.MinPrice:N0}đ – {card.MaxPrice:N0}đ";
            var warranty = p.WarrantyMonths > 0 ? $"{p.WarrantyMonths} tháng ({p.WarrantyType ?? "Chính hãng"})" : "Theo chính sách sàn";
            sb.AppendLine($"{i + 1}. **{p.Name}** (ID: {p.Id})");
            sb.AppendLine($"   - Giá: **{priceStr}** | Đánh giá: **{p.AverageRating:0.0}⭐** ({p.ReviewCount} đánh giá)");
            sb.AppendLine($"   - Thương hiệu: {p.Brand?.Name ?? "Đang cập nhật"} | Gian hàng: {(card.IsMall ? "Sellzy Mall" : "Đối tác tiêu chuẩn")}");
            sb.AppendLine($"   - Bảo hành: {warranty}");
        }

        output.Text = sb.ToString().Trim();
        output.QuickLinks.Add(new AiQuickLink { Label = "Mở trang so sánh chi tiết", Url = "/compare" });
        return output;
    }

    private static string TranslateOrderStatus(string status) => status switch
    {
        "Pending" => "Chờ xác nhận",
        "Confirmed" => "Đã xác nhận",
        "Processing" => "Đang chuẩn bị hàng",
        "Shipping" => "Đang giao hàng",
        "Delivered" => "Đã giao thành công",
        "Completed" => "Hoàn tất",
        "Cancelled" => "Đã hủy",
        "Returned" => "Đã trả hàng / Hoàn tiền",
        _ => status
    };

    private static string TranslateDisputeStatus(string status) => status switch
    {
        "Open" => "Đang chờ gian hàng phản hồi",
        "ShopResponded" => "Gian hàng đã phản hồi",
        "EscalatedToAdmin" => "Ban quản trị Sellzy đang phân xử",
        "ResolvedBuyerWinShopFault" => "Đã hoàn tiền (Lỗi gian hàng)",
        "ResolvedBuyerWinShippingFault" => "Đã hoàn tiền (Lỗi vận chuyển)",
        "ResolvedShopWin" => "Đã đóng (Gian hàng thắng)",
        "Closed" => "Đã đóng",
        _ => status
    };
}
