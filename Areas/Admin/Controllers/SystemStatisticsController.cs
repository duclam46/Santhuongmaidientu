using DOAN_BANHANG.Helpers;
using DOAN_BANHANG.Models;
using DOAN_BANHANG.Services;
using DOAN_BANHANG.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Security.Claims;

namespace DOAN_BANHANG.Areas.Admin.Controllers;

[Area("Admin")]
[Route("Admin/[controller]/[action]")]
[Authorize]
public class SystemStatisticsController : Controller
{
    private readonly StatisticsService _statisticsService;
    private readonly DataContext _context;

    public SystemStatisticsController(StatisticsService statisticsService, DataContext context)
    {
        _statisticsService = statisticsService;
        _context = context;
    }

    [HttpGet]
    [HttpGet("/Admin/SystemStatistics")]
    public async Task<IActionResult> Index()
    {
        if (User.IsInRole("Shop")) return await Seller(null);
        return await Admin();
    }

    [HttpGet]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Admin()
    {
        var adminStats = await _statisticsService.GetAdminDashboardStatsAsync();
        return View("~/Areas/Admin/Views/Statistics/Admin.cshtml", adminStats);
    }

    [HttpGet]
    [Authorize(Roles = "Shop,Admin")]
    public async Task<IActionResult> Seller(int? shopId)
    {
        var role = User.FindFirstValue(ClaimTypes.Role);
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        int.TryParse(userIdStr, out int currentUserId);

        if (role == "Shop")
        {
            shopId = currentUserId;
        }
        else if (role == "Admin" && (!shopId.HasValue || shopId <= 0))
        {
            var firstShop = await _context.Accounts.FirstOrDefaultAsync(a => a.Role == "Shop" && !a.IsDeleted);
            shopId = firstShop?.Id ?? 0;
        }

        if (!shopId.HasValue || shopId <= 0)
        {
            return Content("Không tìm thấy shop nào để hiển thị thống kê.");
        }

        var sellerStats = await _statisticsService.GetSellerDashboardStatsAsync(shopId.Value);
        return View("~/Areas/Admin/Views/Statistics/Seller.cshtml", sellerStats);
    }

    [HttpGet]
    [Authorize(Roles = "Shop,Admin")]
    public async Task<IActionResult> ProfitCalculator(int? shopId = null, CancellationToken ct = default)
    {
        var role = User.FindFirstValue(ClaimTypes.Role);
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        int.TryParse(userIdStr, out int currentUserId);
        bool isAdmin = string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase);

        int targetShopId = currentUserId;
        var availableShops = new List<ShopSelectOptionVm>();

        if (isAdmin)
        {
            var shops = await _context.Accounts
                .AsNoTracking()
                .Include(a => a.Profile)
                .Where(a => a.Role == "Shop" && !a.IsDeleted)
                .OrderBy(a => a.Id)
                .ToListAsync(ct);

            availableShops = shops.Select(s => new ShopSelectOptionVm
            {
                ShopId = s.Id,
                ShopName = s.Profile?.ShopName ?? s.Profile?.FullName ?? s.Email,
                IsMall = s.Profile?.IsMall ?? false
            }).ToList();

            if (shopId.HasValue && shopId.Value > 0 && availableShops.Any(x => x.ShopId == shopId.Value))
            {
                targetShopId = shopId.Value;
            }
            else if (availableShops.Count > 0)
            {
                targetShopId = availableShops[0].ShopId;
            }
        }

        var shopAcc = await _context.Accounts
            .AsNoTracking()
            .Include(a => a.Profile)
            .FirstOrDefaultAsync(a => a.Id == targetShopId && !a.IsDeleted, ct);

        bool isMall = shopAcc?.Profile?.IsMall ?? false;
        string shopName = shopAcc?.Profile?.ShopName ?? shopAcc?.Profile?.FullName ?? "Gian hàng Sellzy";

        var settings = await _context.SystemSettings.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Value, ct);
        decimal ParseSetting(string key, decimal fallback)
        {
            if (settings.TryGetValue(key, out var raw) &&
                decimal.TryParse(raw.Replace(",", "."), NumberStyles.Any, CultureInfo.InvariantCulture, out var val))
            {
                return val;
            }
            return fallback;
        }

        decimal defaultComm = ParseSetting("Fee_Commission_Default", 8m);
        decimal sellerTaxRate = isMall ? 0m : ParseSetting("Tax_Seller_Rate", 1.5m);

        var allCategories = await _context.Categories
            .AsNoTracking()
            .Where(c => !c.IsDeleted)
            .ToDictionaryAsync(c => c.Id, ct);

        int ResolveRootCategoryId(int catId)
        {
            int cur = catId;
            int guard = 0;
            while (guard++ < 10 && allCategories.TryGetValue(cur, out var node) && node.ParentId.HasValue && allCategories.ContainsKey(node.ParentId.Value))
            {
                cur = node.ParentId.Value;
            }
            return cur;
        }

        var now = DateTime.Now;
        var activeVouchers = await _context.Vouchers
            .AsNoTracking()
            .Where(v => v.ShopId == targetShopId
                        && v.IsActive
                        && v.EndDate >= now
                        && (v.UsageLimit <= 0 || v.UsedCount < v.UsageLimit))
            .ToListAsync(ct);

        static HashSet<int> ParseIds(string? csv)
        {
            var set = new HashSet<int>();
            if (string.IsNullOrWhiteSpace(csv)) return set;
            foreach (var part in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (int.TryParse(part, out var id) && id > 0) set.Add(id);
            }
            return set;
        }

        var parsedVouchers = activeVouchers.Select(v => new
        {
            Voucher = v,
            CatIds = ParseIds(v.TargetCategoryIds),
            ProdIds = ParseIds(v.TargetProductIds)
        }).ToList();

        (string? Code, decimal Discount) FindWorstCaseShopVoucher(int productId, int categoryId, int rootCategoryId, decimal unitPrice, bool isFlashSalePrice)
        {
            if (unitPrice <= 0m || parsedVouchers.Count == 0) return (null, 0m);

            string? bestCode = null;
            decimal maxDisc = 0m;

            foreach (var item in parsedVouchers)
            {
                var v = item.Voucher;
                if (isFlashSalePrice && v.ExcludeFlashSaleItems) continue;
                if (v.MinOrderValue > unitPrice) continue;

                var scope = (v.ScopeType ?? "All").Trim();
                if (scope == "SpecificProducts" && !item.ProdIds.Contains(productId)) continue;
                if (scope == "ByCategory" && !item.CatIds.Contains(categoryId) && !item.CatIds.Contains(rootCategoryId)) continue;

                decimal disc = 0m;
                if (string.Equals(v.DiscountType, "Percent", StringComparison.OrdinalIgnoreCase))
                {
                    disc = Math.Round(unitPrice * (v.DiscountValue / 100m), 0, MidpointRounding.AwayFromZero);
                    if (v.MaxDiscountValue.HasValue && v.MaxDiscountValue.Value > 0m && disc > v.MaxDiscountValue.Value)
                    {
                        disc = v.MaxDiscountValue.Value;
                    }
                }
                else
                {
                    disc = v.DiscountValue;
                }

                if (disc > unitPrice) disc = unitPrice;
                if (disc > maxDisc)
                {
                    maxDisc = disc;
                    bestCode = v.Code;
                }
            }

            return (bestCode, maxDisc);
        }

        var products = await _context.Products
            .AsNoTracking()
            .Where(p => p.ShopId == targetShopId && !p.IsDeleted)
            .Include(p => p.Category)
            .Include(p => p.Images)
            .Include(p => p.Variants.Where(v => !v.IsDeleted))
                .ThenInclude(v => v.OptionValue1)
            .Include(p => p.Variants.Where(v => !v.IsDeleted))
                .ThenInclude(v => v.OptionValue2)
            .OrderByDescending(p => p.Id)
            .ToListAsync(ct);

        var rows = new List<SkuMarginRowVm>();
        foreach (var p in products)
        {
            var policy = CommissionHelper.ResolveCategoryPolicy(p.CategoryId, allCategories, defaultComm);
            var effectiveComm = CommissionHelper.GetEffectiveCommissionRate(policy.CommissionRatePercent, isMall);
            var rootCatId = ResolveRootCategoryId(p.CategoryId);
            var primaryImg = ImageUrlHelper.NormalizeStaticFileUrl(
                p.Images.FirstOrDefault(i => i.IsPrimary)?.ImageUrl
                ?? p.Images.FirstOrDefault()?.ImageUrl) ?? "/assets/images/placeholder.png";

            foreach (var v in p.Variants.Where(x => !x.IsDeleted).OrderByDescending(x => x.IsDefault).ThenBy(x => x.Id))
            {
                var parts = new[] { v.OptionValue1?.Value, v.OptionValue2?.Value }
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .ToArray();
                var variantLabel = parts.Length > 0 ? string.Join(" - ", parts) : "Mặc định";

                bool hasFlash = v.SalePrice.HasValue
                                && v.SalePrice.Value > 0m
                                && v.SalePrice.Value < v.Price
                                && (!v.SaleEndDate.HasValue || v.SaleEndDate.Value >= now);

                var (normCode, normDisc) = FindWorstCaseShopVoucher(p.Id, p.CategoryId, rootCatId, v.Price, false);
                var (flashCode, flashDisc) = hasFlash && v.SalePrice.HasValue
                    ? FindWorstCaseShopVoucher(p.Id, p.CategoryId, rootCatId, v.SalePrice.Value, true)
                    : (null, 0m);

                rows.Add(new SkuMarginRowVm
                {
                    VariantId = v.Id,
                    ProductId = p.Id,
                    ProductName = p.Name,
                    ProductStatus = p.Status ?? "Active",
                    ImageUrl = primaryImg,
                    Sku = string.IsNullOrWhiteSpace(v.Sku) ? $"SP{p.Id}-V{v.Id}" : v.Sku,
                    VariantLabel = variantLabel,
                    AvailableStock = Math.Max(0, v.StockQuantity - v.ReservedStock),
                    CategoryId = p.CategoryId,
                    CategoryName = p.Category?.Name ?? "Chưa phân loại",
                    BaseCommissionPercent = policy.CommissionRatePercent,
                    EffectiveCommissionPercent = effectiveComm,
                    MaxCommissionCap = policy.MaxCommissionAmount,
                    CostPrice = v.CostPrice,
                    SellingPrice = v.Price,
                    HasActiveFlashSale = hasFlash,
                    FlashSalePrice = hasFlash ? v.SalePrice : null,
                    NormalVoucherCode = normCode,
                    NormalVoucherDiscount = normDisc,
                    FlashVoucherCode = flashCode,
                    FlashVoucherDiscount = flashDisc
                });
            }
        }

        var vm = new PricingMarginGuardVm
        {
            ShopId = targetShopId,
            ShopName = shopName,
            IsMall = isMall,
            IsAdminViewer = isAdmin,
            DefaultCommissionPercent = defaultComm,
            MallSurchargePercent = CommissionHelper.MallSurchargePercent,
            SellerTaxRatePercent = sellerTaxRate,
            ActiveShopVoucherCount = activeVouchers.Count,
            AvailableShops = availableShops,
            Items = rows
        };

        return View("~/Areas/Admin/Views/Statistics/ProfitCalculator.cshtml", vm);
    }

    [HttpPost]
    [Authorize(Roles = "Shop,Admin")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateVariantPricing([FromBody] BulkVariantPriceUpdateRequest request, CancellationToken ct = default)
    {
        if (request?.Items == null || request.Items.Count == 0)
        {
            return BadRequest(new { success = false, message = "Không có dữ liệu giá cần lưu." });
        }

        var role = User.FindFirstValue(ClaimTypes.Role);
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        int.TryParse(userIdStr, out int currentUserId);
        bool isAdmin = string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase);

        var ids = request.Items.Select(x => x.VariantId).Distinct().ToList();
        var variants = await _context.ProductVariants
            .Include(v => v.Product)
            .Where(v => ids.Contains(v.Id) && !v.IsDeleted && v.Product != null && !v.Product.IsDeleted)
            .ToListAsync(ct);

        int updated = 0;
        foreach (var dto in request.Items)
        {
            if (dto.Price <= 0m || dto.CostPrice < 0m) continue;
            var entity = variants.FirstOrDefault(v => v.Id == dto.VariantId);
            if (entity == null || entity.Product == null) continue;
            if (!isAdmin && entity.Product.ShopId != currentUserId) continue;

            entity.CostPrice = Math.Round(dto.CostPrice, 0, MidpointRounding.AwayFromZero);
            entity.Price = Math.Round(dto.Price, 0, MidpointRounding.AwayFromZero);
            entity.UpdatedAt = DateTime.UtcNow;
            updated++;
        }

        if (updated > 0)
        {
            await _context.SaveChangesAsync(ct);
        }

        return Json(new
        {
            success = true,
            updatedCount = updated,
            message = $"Đã cập nhật thành công giá vốn & giá bán cho {updated} phân loại SKU!"
        });
    }

    [HttpGet]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> FinancialReport()
    {
        var report = await _statisticsService.GetFinancialReportAsync();
        return View("~/Areas/Admin/Views/Statistics/FinancialReport.cshtml", report);
    }
}
