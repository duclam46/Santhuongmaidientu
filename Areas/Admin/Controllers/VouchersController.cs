using System.Security.Claims;
using DOAN_BANHANG.Helpers;
using DOAN_BANHANG.Models;
using DOAN_BANHANG.ViewModels;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DOAN_BANHANG.Areas.Admin.Controllers;

[Area("Admin")]
[Route("Admin/[controller]/[action]")]
[Authorize(Roles = "Admin,Shop")]
public class VouchersController : Controller
{
    private readonly DataContext _db;

    public VouchersController(DataContext db)
    {
        _db = db;
    }

    private string GenerateRandomVoucherCode()
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        var random = new Random();
        return new string(Enumerable.Repeat(chars, 8)
            .Select(s => s[random.Next(s.Length)]).ToArray());
    }

    private static List<int> ParseIdList(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return new List<int>();
        return csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.TryParse(s, out var id) ? id : 0)
            .Where(id => id > 0)
            .Distinct()
            .ToList();
    }

    private static string? SerializeIdList(IEnumerable<int>? ids)
    {
        if (ids == null) return null;
        var clean = ids.Where(id => id > 0).Distinct().ToList();
        return clean.Count > 0 ? string.Join(",", clean) : null;
    }

    private async Task<(Account? Account, bool IsAdmin)> GetCurrentUserAsync()
    {
        if (!(User?.Identity?.IsAuthenticated ?? false)) return (null, false);

        var idRaw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(idRaw, out var accountId) || accountId <= 0) return (null, false);

        var account = await _db.Accounts
            .AsNoTracking()
            .Include(a => a.Profile)
            .FirstOrDefaultAsync(a => a.Id == accountId && !a.IsDeleted);

        if (account is null) return (null, false);

        var isAdmin = account.Role == "Admin";
        return (account, isAdmin);
    }

    private async Task PopulateScopeOptionsAsync(VoucherAdminUpsertVm vm, Account account, bool isAdmin, CancellationToken ct = default)
    {
        vm.IsAdmin = isAdmin;
        vm.ShopName = isAdmin
            ? "Hệ thống Toàn sàn Sellzy"
            : (account.Profile?.ShopName ?? account.Profile?.FullName ?? "Gian hàng của bạn");

        var allCategories = await _db.Categories
            .AsNoTracking()
            .Where(c => !c.IsDeleted && c.IsActive)
            .ToDictionaryAsync(c => c.Id, ct);

        int ResolveRootId(int catId)
        {
            var cur = catId;
            var guard = 0;
            while (guard++ < 10 && allCategories.TryGetValue(cur, out var node) && node.ParentId.HasValue && allCategories.ContainsKey(node.ParentId.Value))
            {
                cur = node.ParentId.Value;
            }
            return cur;
        }

        if (isAdmin)
        {
            var prodCatIds = await _db.Products
                .AsNoTracking()
                .Where(p => !p.IsDeleted && p.Status == "Active")
                .Select(p => p.CategoryId)
                .ToListAsync(ct);

            var countByRoot = prodCatIds
                .GroupBy(ResolveRootId)
                .ToDictionary(g => g.Key, g => g.Count());

            vm.AvailableCategories = allCategories.Values
                .Where(c => c.ParentId == null)
                .OrderBy(c => c.SortOrder)
                .Select(c => new VoucherScopeCategoryOptionVm
                {
                    CategoryId = c.Id,
                    CategoryName = c.Name,
                    CommissionRatePercent = c.CommissionRate ?? 10m,
                    ProductCount = countByRoot.TryGetValue(c.Id, out var cnt) ? cnt : 0
                })
                .ToList();

            vm.ShopAvgCommissionPercent = vm.AvailableCategories.Any()
                ? Math.Round(vm.AvailableCategories.Average(c => c.CommissionRatePercent), 1)
                : 10m;
            vm.ShopAvgNetMarginPercent = vm.ShopAvgCommissionPercent;
        }
        else
        {
            var now = DateTime.Now;
            var isMall = account.Profile?.IsMall ?? false;
            decimal taxAndMktPct = (isMall ? 0m : 1.5m) + 6m;

            var shopProducts = await _db.Products
                .AsNoTracking()
                .Include(p => p.Images)
                .Include(p => p.Variants)
                .Include(p => p.Category)
                .Where(p => p.ShopId == account.Id && !p.IsDeleted)
                .OrderByDescending(p => p.Id)
                .ToListAsync(ct);

            var shopProdIds = shopProducts.Select(p => p.Id).ToList();
            var soldCountByProd = await _db.OrderItems
                .AsNoTracking()
                .Where(oi => oi.Variant != null && shopProdIds.Contains(oi.Variant.ProductId))
                .GroupBy(oi => oi.Variant!.ProductId)
                .Select(g => new { ProductId = g.Key, Units = g.Sum(x => x.Quantity) })
                .ToDictionaryAsync(x => x.ProductId, x => x.Units, ct);

            var margins = new List<decimal>();
            var commRates = new List<decimal>();

            foreach (var p in shopProducts)
            {
                var variants = p.Variants.Where(v => !v.IsDeleted).ToList();
                int stock = variants.Sum(v => Math.Max(0, v.StockQuantity));
                var repVar = variants.OrderBy(v => v.SalePrice ?? v.Price).FirstOrDefault();
                decimal sellPrice = repVar != null ? (repVar.SalePrice ?? repVar.Price) : 0m;
                decimal costPrice = repVar != null && repVar.CostPrice > 0
                    ? repVar.CostPrice
                    : Math.Round(sellPrice * 0.52m, 0);

                bool isInFlashSale = variants.Any(v =>
                    v.SalePrice.HasValue && v.SalePrice.Value < v.Price &&
                    (!v.SaleStartDate.HasValue || v.SaleStartDate.Value <= now) &&
                    (!v.SaleEndDate.HasValue || v.SaleEndDate.Value >= now));

                var policy = CommissionHelper.ResolveCategoryPolicy(p.CategoryId, allCategories, 8m);
                commRates.Add(policy.CommissionRatePercent);

                decimal unitComm = sellPrice > 0
                    ? CommissionHelper.CalculateItemCommission(sellPrice, 1, policy.CommissionRatePercent, policy.MaxCommissionAmount, isMall)
                    : 0m;
                decimal estNet = sellPrice - costPrice - unitComm - (sellPrice * (taxAndMktPct / 100m));
                decimal netMarginPct = sellPrice > 0 ? Math.Round(estNet * 100m / sellPrice, 1) : 22m;
                margins.Add(netMarginPct);

                int soldCount = soldCountByProd.TryGetValue(p.Id, out var sc) ? sc : 0;

                string bcgTag;
                string bcgClass;
                if (isInFlashSale)
                {
                    bcgTag = "⚡ Đang chạy Flash Sale";
                    bcgClass = "bg-danger-subtle text-danger-emphasis border-danger-subtle";
                }
                else if (soldCount >= 5 && netMarginPct >= 20m)
                {
                    bcgTag = "🌟 Ngôi sao Chủ lực";
                    bcgClass = "bg-success-subtle text-success-emphasis border-success-subtle";
                }
                else if (netMarginPct >= 22m)
                {
                    bcgTag = "🚀 Tiềm năng Kích cầu";
                    bcgClass = "bg-info-subtle text-info-emphasis border-info-subtle";
                }
                else
                {
                    bcgTag = "📦 Cần tối ưu / Xả tồn";
                    bcgClass = "bg-warning-subtle text-warning-emphasis border-warning-subtle";
                }

                var imgUrl = ImageUrlHelper.NormalizeStaticFileUrl(
                    p.Images.FirstOrDefault(i => i.IsPrimary)?.ImageUrl
                    ?? p.Images.FirstOrDefault()?.ImageUrl) ?? "";

                vm.AvailableProducts.Add(new VoucherScopeProductOptionVm
                {
                    ProductId = p.Id,
                    ProductName = p.Name,
                    CategoryName = p.Category?.Name ?? "Khác",
                    ImageUrl = imgUrl,
                    SellingPrice = sellPrice,
                    CostPrice = costPrice,
                    EstimatedNetMarginPercent = netMarginPct,
                    CurrentStock = stock,
                    UnitsSold = soldCount,
                    IsInFlashSale = isInFlashSale,
                    BcgTag = bcgTag,
                    BcgBadgeClass = bcgClass
                });
            }

            vm.AvailableCategories = shopProducts
                .GroupBy(p => ResolveRootId(p.CategoryId))
                .Select(g =>
                {
                    allCategories.TryGetValue(g.Key, out var rootCat);
                    return new VoucherScopeCategoryOptionVm
                    {
                        CategoryId = g.Key,
                        CategoryName = rootCat?.Name ?? "Danh mục chung",
                        CommissionRatePercent = rootCat?.CommissionRate ?? 8m,
                        ProductCount = g.Count()
                    };
                })
                .OrderByDescending(c => c.ProductCount)
                .ToList();

            vm.ShopAvgNetMarginPercent = margins.Any() ? Math.Round(margins.Average(), 1) : 26m;
            vm.ShopAvgCommissionPercent = commRates.Any() ? Math.Round(commRates.Average(), 1) : 8m;
        }
    }

    private static string BuildScopeDisplay(Voucher v, Dictionary<int, string> categoryNames, int shopProductCount)
    {
        var scope = (v.ScopeType ?? "All").Trim();
        if (scope == "MallOnly")
            return "👑 Đặc quyền Gian hàng Sellzy Mall";

        if (scope == "ByCategory")
        {
            var ids = ParseIdList(v.TargetCategoryIds);
            if (ids.Count == 0) return "Theo ngành hàng";
            var names = ids.Select(id => categoryNames.TryGetValue(id, out var n) ? n : $"#{id}").Take(2).ToList();
            return ids.Count > 2
                ? $"Ngành: {string.Join(", ", names)} (+{ids.Count - 2})"
                : $"Ngành: {string.Join(", ", names)}";
        }

        if (scope == "SpecificProducts")
        {
            var ids = ParseIdList(v.TargetProductIds);
            return $"📦 {ids.Count} sản phẩm chỉ định";
        }

        return v.ShopId == null ? "🌐 Toàn sàn Sellzy" : "🏪 Toàn bộ sản phẩm Shop";
    }

    private static string BuildAudienceDisplay(string? audience)
    {
        return (audience ?? "Public").Trim() switch
        {
            "NewBuyerOnly" => "🆕 Chỉ Khách hàng mới",
            "PrivateChatOnly" => "💬 Mã ẩn (Gửi riêng qua Chat)",
            _ => "👥 Công khai mọi khách"
        };
    }

    [HttpGet]
    [HttpGet("/Admin/Vouchers")]
    public async Task<IActionResult> Index(VoucherAdminQueryVm query, CancellationToken cancellationToken)
    {
        var (account, isAdmin) = await GetCurrentUserAsync();
        if (account == null)
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Index", "Home", new { area = "Admin" });
        }

        var q = _db.Vouchers
            .Include(v => v.Shop)
                .ThenInclude(s => s!.Profile)
            .AsNoTracking();

        if (isAdmin)
        {
            q = q.Where(v => v.ShopId == null);
        }
        else if (account.Role == "Shop")
        {
            q = q.Where(v => v.ShopId == account.Id);
        }

        var now = DateTime.Now;
        var allCount = await q.CountAsync(cancellationToken);
        var activeCount = await q.CountAsync(v => v.IsActive && v.StartDate <= now && v.EndDate >= now && (v.UsageLimit == 0 || v.UsedCount < v.UsageLimit), cancellationToken);
        var inactiveCount = await q.CountAsync(v => !v.IsActive, cancellationToken);
        var expiredCount = await q.CountAsync(v => v.EndDate < now || (v.UsageLimit > 0 && v.UsedCount >= v.UsageLimit), cancellationToken);
        var totalUsedCount = await q.SumAsync(v => (int?)v.UsedCount, cancellationToken) ?? 0;

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            q = q.Where(v => v.Code.Contains(term));
        }

        if (!string.IsNullOrWhiteSpace(query.Status) && query.Status != "All")
        {
            if (query.Status == "Active")
                q = q.Where(v => v.IsActive && v.StartDate <= now && v.EndDate >= now && (v.UsageLimit == 0 || v.UsedCount < v.UsageLimit));
            else if (query.Status == "Inactive")
                q = q.Where(v => !v.IsActive);
            else if (query.Status == "Expired")
                q = q.Where(v => v.EndDate < now || (v.UsageLimit > 0 && v.UsedCount >= v.UsageLimit));
        }

        var totalItems = await q.CountAsync(cancellationToken);

        var page = query.Page <= 0 ? 1 : query.Page;
        var pageSize = query.PageSize <= 0 ? 10 : Math.Min(query.PageSize, 50);
        var skip = (page - 1) * pageSize;

        var rawItems = await q
            .OrderByDescending(v => v.CreatedAt)
            .Skip(skip)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var catNames = await _db.Categories
            .AsNoTracking()
            .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);

        var items = rawItems.Select(v => new VoucherAdminListItemVm
        {
            Id = v.Id,
            Code = v.Code,
            DiscountType = v.DiscountType,
            DiscountValue = v.DiscountValue,
            MinOrderValue = v.MinOrderValue,
            MaxDiscountValue = v.MaxDiscountValue,
            UsageLimit = v.UsageLimit,
            UsedCount = v.UsedCount,
            StartDate = v.StartDate,
            EndDate = v.EndDate,
            IsActive = v.IsActive,
            CreatedAt = v.CreatedAt,
            ShopId = v.ShopId,
            ShopName = v.Shop != null && v.Shop.Profile != null ? v.Shop.Profile.ShopName : null,
            ScopeType = v.ScopeType ?? "All",
            ScopeDisplayText = BuildScopeDisplay(v, catNames, 0),
            TargetAudience = v.TargetAudience ?? "Public",
            AudienceDisplayText = BuildAudienceDisplay(v.TargetAudience),
            MaxUsagePerUser = v.MaxUsagePerUser,
            ExcludeFlashSaleItems = v.ExcludeFlashSaleItems,
            FundingSourceText = v.ShopId == null
                ? "Quỹ Phí chiết khấu Sàn (Admin tài trợ)"
                : "Trừ vào Tiền hàng Quyết toán Ví Shop"
        }).ToList();

        var vm = new VoucherAdminListVm
        {
            Query = query,
            Items = items,
            TotalItems = totalItems,
            AllCount = allCount,
            ActiveCount = activeCount,
            InactiveCount = inactiveCount,
            ExpiredCount = expiredCount,
            TotalUsedCount = totalUsedCount,
            IsAdmin = isAdmin
        };

        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> Create(
        [FromQuery] string? discountType = null,
        [FromQuery] decimal? discountValue = null,
        [FromQuery] decimal? minOrderValue = null,
        [FromQuery] decimal? maxDiscountValue = null,
        [FromQuery] int? durationDays = null,
        [FromQuery] string? prefix = null,
        [FromQuery] string? scopeType = null,
        CancellationToken ct = default)
    {
        var (account, isAdmin) = await GetCurrentUserAsync();
        if (account == null) return RedirectToAction("Index", "Home", new { area = "Admin" });

        var cleanPrefix = !string.IsNullOrWhiteSpace(prefix)
            ? new string(prefix.Trim().ToUpperInvariant().Where(char.IsLetterOrDigit).Take(4).ToArray())
            : (isAdmin ? "SELLZY" : "SHOP");
        var randomSuffix = GenerateRandomVoucherCode();
        var suggestedCode = $"{cleanPrefix}{randomSuffix[..Math.Min(4, randomSuffix.Length)]}";

        var days = durationDays.HasValue && durationDays.Value > 0 ? Math.Min(60, durationDays.Value) : 7;

        var vm = new VoucherAdminUpsertVm
        {
            Code = suggestedCode,
            DiscountType = !string.IsNullOrWhiteSpace(discountType) ? discountType : "Percent",
            DiscountValue = discountValue ?? (isAdmin ? 10m : 8m),
            MinOrderValue = minOrderValue ?? 0m,
            MaxDiscountValue = maxDiscountValue,
            StartDate = DateTime.Now,
            EndDate = DateTime.Now.AddDays(days),
            UsageLimit = 100,
            MaxUsagePerUser = 1,
            ScopeType = !string.IsNullOrWhiteSpace(scopeType) ? scopeType : "All",
            TargetAudience = "Public",
            ExcludeFlashSaleItems = !isAdmin,
            IsActive = true
        };

        await PopulateScopeOptionsAsync(vm, account, isAdmin, ct);
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(VoucherAdminUpsertVm model, CancellationToken cancellationToken)
    {
        var (account, isAdmin) = await GetCurrentUserAsync();
        if (account == null) return RedirectToAction("Index", "Home", new { area = "Admin" });

        if (model.StartDate >= model.EndDate)
        {
            ModelState.AddModelError(nameof(model.EndDate), "Ngày kết thúc phải lớn hơn ngày bắt đầu.");
        }

        if (model.DiscountType == "Percent" && model.DiscountValue > 100)
        {
            ModelState.AddModelError(nameof(model.DiscountValue), "Khuyến mãi phần trăm không vượt quá 100%.");
        }

        var normScope = (model.ScopeType ?? "All").Trim();
        if (normScope == "ByCategory" && (model.SelectedCategoryIds == null || !model.SelectedCategoryIds.Any()))
        {
            ModelState.AddModelError(nameof(model.ScopeType), "Vui lòng tích chọn ít nhất 1 ngành hàng áp dụng cho mã giảm giá này.");
        }
        if (!isAdmin && normScope == "SpecificProducts" && (model.SelectedProductIds == null || !model.SelectedProductIds.Any()))
        {
            ModelState.AddModelError(nameof(model.ScopeType), "Vui lòng tích chọn ít nhất 1 sản phẩm của Shop để áp dụng mã giảm giá này.");
        }

        if (!ModelState.IsValid)
        {
            await PopulateScopeOptionsAsync(model, account, isAdmin, cancellationToken);
            return View(model);
        }

        var code = model.Code.Trim().ToUpper();
        var exists = await _db.Vouchers.AnyAsync(v => v.Code == code, cancellationToken);
        if (exists)
        {
            ModelState.AddModelError(nameof(model.Code), "Mã Voucher đã tồn tại.");
            await PopulateScopeOptionsAsync(model, account, isAdmin, cancellationToken);
            return View(model);
        }

        var voucher = new Voucher
        {
            ShopId = isAdmin ? null : account.Id,
            Code = code,
            DiscountType = model.DiscountType,
            DiscountValue = model.DiscountValue,
            MinOrderValue = model.MinOrderValue,
            MaxDiscountValue = model.MaxDiscountValue,
            UsageLimit = model.UsageLimit,
            MaxUsagePerUser = Math.Max(0, model.MaxUsagePerUser),
            ScopeType = normScope,
            TargetCategoryIds = normScope == "ByCategory" ? SerializeIdList(model.SelectedCategoryIds) : null,
            TargetProductIds = (!isAdmin && normScope == "SpecificProducts") ? SerializeIdList(model.SelectedProductIds) : null,
            TargetAudience = string.IsNullOrWhiteSpace(model.TargetAudience) ? "Public" : model.TargetAudience.Trim(),
            ExcludeFlashSaleItems = model.ExcludeFlashSaleItems,
            UsedCount = 0,
            StartDate = model.StartDate!.Value,
            EndDate = model.EndDate!.Value,
            IsActive = model.IsActive,
            CreatedAt = DateTime.UtcNow
        };

        _db.Vouchers.Add(voucher);
        await _db.SaveChangesAsync(cancellationToken);

        // Notify shop followers when Shop launches an active public voucher
        if (!isAdmin && voucher.IsActive && voucher.TargetAudience != "PrivateChatOnly")
        {
            try
            {
                var followerIds = await _db.ShopFollowers
                    .AsNoTracking()
                    .Where(f => f.ShopId == account.Id)
                    .Select(f => f.FollowerAccountId)
                    .ToListAsync(cancellationToken);

                if (followerIds.Count > 0)
                {
                    var shopName = account.Profile?.ShopName ?? account.Profile?.FullName ?? "Gian hàng bạn theo dõi";
                    var shopUrl = !string.IsNullOrWhiteSpace(account.Profile?.ShopSlug)
                        ? $"/shop/{account.Profile.ShopSlug}"
                        : "/vouchers";
                    var discDesc = voucher.DiscountType == "FreeShipping"
                        ? $"Freeship {voucher.DiscountValue:N0}₫"
                        : (voucher.DiscountType == "Percent" ? $"Giảm {voucher.DiscountValue:0.##}%" : $"Giảm {voucher.DiscountValue:N0}₫");

                    foreach (var fid in followerIds)
                    {
                        _db.SystemNotifications.Add(new SystemNotification
                        {
                            Title = $"🎟️ {shopName} vừa tung mã mới: {voucher.Code}",
                            Content = $"Ưu đãi {discDesc} cho đơn từ {voucher.MinOrderValue:N0}₫ tại {shopUrl}. Bấm để lưu mã và mua sắm ngay!",
                            TargetType = "Account",
                            TargetAccountId = fid,
                            SentByAdminId = account.Id,
                            CreatedAt = DateTime.UtcNow
                        });
                    }
                    await _db.SaveChangesAsync(cancellationToken);
                }
            }
            catch
            {
                // Non-blocking notification fan-out
            }
        }

        TempData["AdminToast"] = isAdmin
            ? $"Đã tạo Voucher Hệ thống [{code}] thành công (Nguồn tài trợ: Quỹ Phí chiết khấu sàn)!"
            : $"Đã tạo Voucher Gian hàng [{code}] thành công (Khấu trừ tự động vào doanh thu quyết toán đơn hàng)!";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        var (account, isAdmin) = await GetCurrentUserAsync();
        if (account == null) return RedirectToAction("Index", "Home", new { area = "Admin" });

        var voucher = await _db.Vouchers.FirstOrDefaultAsync(v => v.Id == id, cancellationToken);
        if (voucher == null) return NotFound();

        if (!isAdmin && voucher.ShopId != account.Id) return Forbid();

        var vm = new VoucherAdminUpsertVm
        {
            Id = voucher.Id,
            Code = voucher.Code,
            DiscountType = voucher.DiscountType,
            DiscountValue = voucher.DiscountValue,
            MinOrderValue = voucher.MinOrderValue,
            MaxDiscountValue = voucher.MaxDiscountValue,
            UsageLimit = voucher.UsageLimit,
            MaxUsagePerUser = voucher.MaxUsagePerUser,
            ScopeType = string.IsNullOrWhiteSpace(voucher.ScopeType) ? "All" : voucher.ScopeType,
            SelectedCategoryIds = ParseIdList(voucher.TargetCategoryIds),
            SelectedProductIds = ParseIdList(voucher.TargetProductIds),
            TargetAudience = string.IsNullOrWhiteSpace(voucher.TargetAudience) ? "Public" : voucher.TargetAudience,
            ExcludeFlashSaleItems = voucher.ExcludeFlashSaleItems,
            StartDate = voucher.StartDate,
            EndDate = voucher.EndDate,
            IsActive = voucher.IsActive
        };

        await PopulateScopeOptionsAsync(vm, account, isAdmin, cancellationToken);
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(VoucherAdminUpsertVm model, CancellationToken cancellationToken)
    {
        var (account, isAdmin) = await GetCurrentUserAsync();
        if (account == null) return RedirectToAction("Index", "Home", new { area = "Admin" });

        if (model.StartDate >= model.EndDate)
        {
            ModelState.AddModelError(nameof(model.EndDate), "Ngày kết thúc phải lớn hơn ngày bắt đầu.");
        }

        if (model.DiscountType == "Percent" && model.DiscountValue > 100)
        {
            ModelState.AddModelError(nameof(model.DiscountValue), "Khuyến mãi phần trăm không vượt quá 100%.");
        }

        var normScope = (model.ScopeType ?? "All").Trim();
        if (normScope == "ByCategory" && (model.SelectedCategoryIds == null || !model.SelectedCategoryIds.Any()))
        {
            ModelState.AddModelError(nameof(model.ScopeType), "Vui lòng tích chọn ít nhất 1 ngành hàng áp dụng.");
        }
        if (!isAdmin && normScope == "SpecificProducts" && (model.SelectedProductIds == null || !model.SelectedProductIds.Any()))
        {
            ModelState.AddModelError(nameof(model.ScopeType), "Vui lòng tích chọn ít nhất 1 sản phẩm áp dụng.");
        }

        if (!ModelState.IsValid)
        {
            await PopulateScopeOptionsAsync(model, account, isAdmin, cancellationToken);
            return View(model);
        }

        var voucher = await _db.Vouchers.FirstOrDefaultAsync(v => v.Id == model.Id, cancellationToken);
        if (voucher == null) return NotFound();

        if (!isAdmin && voucher.ShopId != account.Id) return Forbid();

        var code = model.Code.Trim().ToUpper();
        if (code != voucher.Code)
        {
            var exists = await _db.Vouchers.AnyAsync(v => v.Code == code, cancellationToken);
            if (exists)
            {
                ModelState.AddModelError(nameof(model.Code), "Mã Voucher đã tồn tại.");
                await PopulateScopeOptionsAsync(model, account, isAdmin, cancellationToken);
                return View(model);
            }
        }

        if (model.UsageLimit > 0 && model.UsageLimit < voucher.UsedCount)
        {
            ModelState.AddModelError(nameof(model.UsageLimit), $"Số lượng giới hạn không được nhỏ hơn số lượng đã sử dụng ({voucher.UsedCount}).");
            await PopulateScopeOptionsAsync(model, account, isAdmin, cancellationToken);
            return View(model);
        }

        voucher.Code = code;
        voucher.DiscountType = model.DiscountType;
        voucher.DiscountValue = model.DiscountValue;
        voucher.MinOrderValue = model.MinOrderValue;
        voucher.MaxDiscountValue = model.MaxDiscountValue;
        voucher.UsageLimit = model.UsageLimit;
        voucher.MaxUsagePerUser = Math.Max(0, model.MaxUsagePerUser);
        voucher.ScopeType = normScope;
        voucher.TargetCategoryIds = normScope == "ByCategory" ? SerializeIdList(model.SelectedCategoryIds) : null;
        voucher.TargetProductIds = (!isAdmin && normScope == "SpecificProducts") ? SerializeIdList(model.SelectedProductIds) : null;
        voucher.TargetAudience = string.IsNullOrWhiteSpace(model.TargetAudience) ? "Public" : model.TargetAudience.Trim();
        voucher.ExcludeFlashSaleItems = model.ExcludeFlashSaleItems;
        voucher.StartDate = model.StartDate!.Value;
        voucher.EndDate = model.EndDate!.Value;
        voucher.IsActive = model.IsActive;

        await _db.SaveChangesAsync(cancellationToken);

        TempData["AdminToast"] = $"Cập nhật Voucher [{code}] thành công!";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var (account, isAdmin) = await GetCurrentUserAsync();
        if (account == null) return Unauthorized();

        var voucher = await _db.Vouchers.FirstOrDefaultAsync(v => v.Id == id, cancellationToken);
        if (voucher == null) return NotFound();

        if (!isAdmin && voucher.ShopId != account.Id) return Forbid();

        if (voucher.UsedCount > 0)
        {
            voucher.IsActive = false;
            TempData["AdminToast"] = "Voucher đã được sử dụng nên chỉ chuyển sang trạng thái ngưng kích hoạt.";
        }
        else
        {
            _db.Vouchers.Remove(voucher);
            TempData["AdminToast"] = "Đã xóa Voucher thành công!";
        }

        await _db.SaveChangesAsync(cancellationToken);
        return RedirectToAction(nameof(Index));
    }
}
