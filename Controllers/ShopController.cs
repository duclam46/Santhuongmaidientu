using DOAN_BANHANG.Models;
using DOAN_BANHANG.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DOAN_BANHANG.Controllers;

public class ShopController : Controller
{
    private readonly DataContext _context;

    public ShopController(DataContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<IActionResult> Index(int? page, int? categoryId, int? minRating, int? maxRating, string? tier, CancellationToken cancellationToken)
    {
        const int pageSize = 12;
        var currentPage = page.GetValueOrDefault(1);
        if (currentPage < 1) currentPage = 1;

        string? selectedTier = null;
        if (string.Equals(tier?.Trim(), "Mall", StringComparison.OrdinalIgnoreCase))
        {
            selectedTier = "Mall";
        }
        else if (string.Equals(tier?.Trim(), "Standard", StringComparison.OrdinalIgnoreCase))
        {
            selectedTier = "Standard";
        }

        int? selectedMinRating = null;
        if (minRating.HasValue)
        {
            selectedMinRating = Math.Clamp(minRating.Value, 1, 5);
        }

        int? selectedMaxRating = null;
        if (maxRating.HasValue)
        {
            selectedMaxRating = Math.Clamp(maxRating.Value, 1, 5);
        }

        if (selectedMinRating.HasValue && selectedMaxRating.HasValue && selectedMinRating.Value > selectedMaxRating.Value)
        {
            (selectedMinRating, selectedMaxRating) = (selectedMaxRating, selectedMinRating);
        }

        var allActiveCategories = await _context.Categories
            .AsNoTracking()
            .Where(c => c.IsActive && !c.IsDeleted)
            .Select(c => new { c.Id, c.Name, c.ParentId })
            .ToListAsync(cancellationToken);

        var parentById = allActiveCategories.ToDictionary(c => c.Id, c => c.ParentId);
        int GetRootCategoryId(int catId)
        {
            var current = catId;
            var guard = 0;
            while (parentById.TryGetValue(current, out var parentId) && parentId.HasValue && guard++ < 10)
            {
                current = parentId.Value;
            }
            return current;
        }

        var rootCategories = allActiveCategories
            .Where(c => c.ParentId == null)
            .OrderBy(c => c.Name)
            .Select(c => new { c.Id, c.Name })
            .ToList();

        var validRootCategoryIds = rootCategories.Select(c => c.Id).ToHashSet();
        int? selectedCategoryId = (categoryId.HasValue && validRootCategoryIds.Contains(categoryId.Value))
            ? categoryId.Value
            : null;

        var activeProductPairs = await _context.Products
            .AsNoTracking()
            .Where(p => p.Status == "Active" && !p.IsDeleted)
            .Select(p => new { p.CategoryId, p.ShopId })
            .Distinct()
            .ToListAsync(cancellationToken);

        var rootCategoryShopCounts = activeProductPairs
            .Where(x => parentById.ContainsKey(x.CategoryId))
            .Select(x => new { RootCategoryId = GetRootCategoryId(x.CategoryId), x.ShopId })
            .Distinct()
            .GroupBy(x => x.RootCategoryId)
            .ToDictionary(g => g.Key, g => g.Count());

        var categoryFilters = rootCategories
            .Select(c => new ShopCategoryFilterItemVm
            {
                CategoryId = c.Id,
                Name = c.Name,
                ShopCount = rootCategoryShopCounts.TryGetValue(c.Id, out var count) ? count : 0,
                IsSelected = selectedCategoryId == c.Id
            })
            .ToList();

        string? selectedCategoryName = null;
        if (selectedCategoryId.HasValue)
        {
            selectedCategoryName = rootCategories
                .Where(c => c.Id == selectedCategoryId.Value)
                .Select(c => c.Name)
                .FirstOrDefault();
        }

        var shopsQuery = _context.Accounts
            .AsNoTracking()
            .Where(a => a.Status == "Active" && !a.IsDeleted)
            .Where(a => a.Profile != null && !string.IsNullOrWhiteSpace(a.Profile.ShopSlug));

        if (selectedTier == "Mall")
        {
            shopsQuery = shopsQuery.Where(a => a.Profile!.IsMall);
        }
        else if (selectedTier == "Standard")
        {
            shopsQuery = shopsQuery.Where(a => !a.Profile!.IsMall);
        }

        if (selectedCategoryId.HasValue)
        {
            var categoryScopeIds = allActiveCategories
                .Where(c => GetRootCategoryId(c.Id) == selectedCategoryId.Value)
                .Select(c => c.Id)
                .ToList();

            shopsQuery = shopsQuery.Where(a => _context.Products
                .AsNoTracking()
                .Any(p => p.Status == "Active"
                    && !p.IsDeleted
                    && p.ShopId == a.Id
                    && categoryScopeIds.Contains(p.CategoryId)));
        }

        if (selectedMinRating.HasValue || selectedMaxRating.HasValue)
        {
            var effectiveMin = selectedMinRating ?? 0;
            var effectiveMax = selectedMaxRating ?? 5;

            shopsQuery = shopsQuery.Where(a => (
                    _context.Products
                        .AsNoTracking()
                        .Where(p => p.Status == "Active" && !p.IsDeleted && p.ShopId == a.Id)
                        .Select(p => (decimal?)p.AverageRating)
                        .Average() ?? 0m)
                >= effectiveMin
                && (
                    _context.Products
                        .AsNoTracking()
                        .Where(p => p.Status == "Active" && !p.IsDeleted && p.ShopId == a.Id)
                        .Select(p => (decimal?)p.AverageRating)
                        .Average() ?? 0m)
                <= effectiveMax);
        }

        var totalItems = await shopsQuery.CountAsync(cancellationToken);
        var totalPages = totalItems == 0 ? 0 : (int)Math.Ceiling(totalItems / (double)pageSize);

        if (totalPages > 0 && currentPage > totalPages)
        {
            return RedirectToAction(nameof(Index), new
            {
                page = totalPages,
                categoryId = selectedCategoryId,
                minRating = selectedMinRating,
                maxRating = selectedMaxRating
            });
        }

        var shops = await shopsQuery
            .OrderByDescending(a => a.Profile!.IsMall)
            .ThenBy(a => a.Profile!.ShopName)
            .ThenBy(a => a.Profile!.FullName)
            .ThenBy(a => a.Id)
            .Select(a => new ShopSummaryVm
            {
                ShopId = a.Id,
                Name = a.Profile!.ShopName
                    ?? a.Profile!.FullName
                    ?? a.Email,
                Slug = a.Profile!.ShopSlug,
                LogoUrl = a.Profile!.ShopLogoUrl,
                Location = a.Profile!.Address,
                LastActiveText = null,
                IsMall = a.Profile!.IsMall,
                ShopTier = a.Profile!.IsMall ? "Mall" : "Standard",
                RatingAverage = _context.Products
                    .AsNoTracking()
                    .Where(p => p.Status == "Active" && !p.IsDeleted && p.ShopId == a.Id)
                    .Select(p => (decimal?)p.AverageRating)
                    .Average() ?? 0m,
                ProductCount = _context.Products
                    .AsNoTracking()
                    .Count(p => p.Status == "Active" && !p.IsDeleted && p.ShopId == a.Id),
                ChatResponseRate = null
            })
            .Skip((currentPage - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var vm = new ShopListVm
        {
            Page = currentPage,
            PageSize = pageSize,
            TotalItems = totalItems,
            TotalPages = totalPages,
            SelectedCategoryId = selectedCategoryId,
            SelectedCategoryName = selectedCategoryName,
            SelectedMinRating = selectedMinRating,
            SelectedMaxRating = selectedMaxRating,
            SelectedTier = selectedTier,
            Categories = categoryFilters,
            Shops = shops
        };

        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> Details(string slug, int? page, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            return NotFound();
        }

        var shopAccount = await _context.Accounts
            .AsNoTracking()
            .Include(a => a.Profile)
            .FirstOrDefaultAsync(a => a.Status == "Active"
                && !a.IsDeleted
                && a.Profile != null
                && a.Profile.ShopSlug == slug, cancellationToken);

        if (shopAccount?.Profile is null)
        {
            return NotFound();
        }

        var shopAddress = await _context.UserAddresses
            .AsNoTracking()
            .Where(a => a.AccountId == shopAccount.Id && a.AddressType == AddressType.Shop)
            .OrderByDescending(a => a.IsDefault)
            .ThenByDescending(a => a.UpdatedAt)
            .ThenByDescending(a => a.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var shopProductQuery = _context.Products
            .AsNoTracking()
            .Where(p => p.Status == "Active" && !p.IsDeleted)
            .Where(p => p.ShopId == shopAccount.Id);

        var productCount = await shopProductQuery.CountAsync(cancellationToken);
        var ratingAvg = await shopProductQuery
            .Select(p => (decimal?)p.AverageRating)
            .AverageAsync(cancellationToken) ?? 0m;

        var now = DateTime.Now;

        const int pageSize = 12;
        var currentPage = page.GetValueOrDefault(1);
        if (currentPage < 1) currentPage = 1;

        var totalPages = productCount == 0 ? 0 : (int)Math.Ceiling(productCount / (double)pageSize);
        if (totalPages > 0 && currentPage > totalPages)
        {
            return RedirectToRoute("shop_details", new { slug, page = totalPages });
        }

        var products = await shopProductQuery
            .OrderByDescending(p => p.AverageRating)
            .ThenByDescending(p => p.ReviewCount)
            .ThenBy(p => p.Name)
            .Skip((currentPage - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new
            {
                ProductId = p.Id,
                p.Name,
                p.Slug,
                DefaultVariantId = p.Variants
                    .Where(v => v.IsActive && !v.IsDeleted)
                    .OrderByDescending(v => v.IsDefault)
                    .ThenBy(v => v.Price)
                    .Select(v => (int?)v.Id)
                    .FirstOrDefault() ?? 0,
                RatingAverage = p.AverageRating,
                RatingCount = p.ReviewCount,
                PrimaryImageUrl = p.Images
                    .OrderByDescending(i => i.IsPrimary)
                    .ThenBy(i => i.SortOrder)
                    .ThenBy(i => i.Id)
                    .Select(i => i.ImageUrl)
                    .FirstOrDefault(),
                Price = p.Variants
                    .Where(v => v.IsActive && !v.IsDeleted)
                    .Select(v => (decimal?)((v.SalePrice.HasValue
                            && (!v.SaleStartDate.HasValue || v.SaleStartDate <= now)
                            && (!v.SaleEndDate.HasValue || v.SaleEndDate >= now))
                        ? v.SalePrice.Value
                        : v.Price))
                    .Min() ?? 0m,
                OldPrice = p.Variants
                    .Where(v => v.IsActive
                        && !v.IsDeleted
                        && v.SalePrice.HasValue
                        && (!v.SaleStartDate.HasValue || v.SaleStartDate <= now)
                        && (!v.SaleEndDate.HasValue || v.SaleEndDate >= now)
                        && v.SalePrice.Value < v.Price)
                    .Select(v => (decimal?)v.Price)
                    .Min()
            })
            .Select(x => new ProductCardVm
            {
                ProductId = x.ProductId,
                DefaultVariantId = x.DefaultVariantId,
                Name = x.Name,
                Slug = x.Slug,
                Price = x.Price,
                OldPrice = x.OldPrice,
                RatingAverage = x.RatingAverage,
                RatingCount = x.RatingCount,
                PrimaryImageUrl = x.PrimaryImageUrl,
                DiscountPercent = (x.OldPrice.HasValue && x.OldPrice.Value > 0m && x.Price < x.OldPrice.Value)
                    ? (int)Math.Round((double)((x.OldPrice.Value - x.Price) / x.OldPrice.Value * 100m))
                    : 0
            })
            .ToListAsync(cancellationToken);

        var location = !string.IsNullOrWhiteSpace(shopAddress?.ProvinceName)
            ? $"{shopAddress!.DistrictName}, {shopAddress.ProvinceName}"
            : shopAccount.Profile.Address;

        var rawShopVouchers = await _context.Vouchers
            .AsNoTracking()
            .Where(v => v.IsActive && v.StartDate <= now && v.EndDate >= now && (v.UsageLimit == 0 || v.UsedCount < v.UsageLimit))
            .Where(v => v.ShopId == shopAccount.Id || (v.ShopId == null && (v.ScopeType == "All" || (v.ScopeType == "MallOnly" && shopAccount.Profile.IsMall))))
            .Where(v => v.TargetAudience != "PrivateChatOnly")
            .OrderByDescending(v => v.ShopId != null)
            .ThenByDescending(v => v.DiscountValue)
            .Take(8)
            .ToListAsync(cancellationToken);

        int viewerId = 0;
        if (User?.Identity?.IsAuthenticated == true)
        {
            var rawId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            int.TryParse(rawId, out viewerId);
        }

        var savedSet = viewerId > 0
            ? (await _context.SavedVouchers
                .AsNoTracking()
                .Where(s => s.AccountId == viewerId)
                .Select(s => s.VoucherId)
                .ToListAsync(cancellationToken)).ToHashSet()
            : new HashSet<int>();

        var followerCount = await _context.ShopFollowers
            .AsNoTracking()
            .CountAsync(f => f.ShopId == shopAccount.Id, cancellationToken);

        var isFollowing = viewerId > 0 && await _context.ShopFollowers
            .AsNoTracking()
            .AnyAsync(f => f.FollowerAccountId == viewerId && f.ShopId == shopAccount.Id, cancellationToken);

        var shopVouchers = rawShopVouchers.Select(v => new StorefrontVoucherVm
        {
            Id = v.Id,
            Code = v.Code,
            DiscountType = v.DiscountType,
            DiscountValue = v.DiscountValue,
            MinOrderValue = v.MinOrderValue,
            MaxDiscountValue = v.MaxDiscountValue,
            EndDate = v.EndDate,
            IsShopVoucher = v.ShopId.HasValue,
            SponsorBadge = v.ShopId.HasValue ? "Voucher Gian hàng" : "Voucher Toàn Sàn",
            ScopeBadge = (v.ScopeType ?? "All") switch
            {
                "MallOnly" => "Độc quyền Mall",
                "ByCategory" => "Theo danh mục",
                "SpecificProducts" => "Sản phẩm chọn lọc",
                _ => v.ShopId.HasValue ? "Toàn Gian hàng" : "Toàn Sàn Sellzy"
            },
            IsSaved = savedSet.Contains(v.Id)
        }).ToList();

        var vm = new ShopDetailsVm
        {
            Shop = new ShopSummaryVm
            {
                ShopId = shopAccount.Id,
                Name = shopAccount.Profile.ShopName
                    ?? shopAccount.Profile.FullName
                    ?? shopAccount.Email,
                Slug = shopAccount.Profile.ShopSlug,
                LogoUrl = shopAccount.Profile.ShopLogoUrl,
                Location = location,
                LastActiveText = null,
                RatingAverage = Math.Round(ratingAvg, 1),
                ProductCount = productCount,
                ChatResponseRate = 99,
                IsMall = shopAccount.Profile.IsMall,
                ShopTier = shopAccount.Profile.IsMall ? "Mall" : "Standard",
                FollowerCount = followerCount,
                IsFollowing = isFollowing
            },
            Page = currentPage,
            PageSize = pageSize,
            TotalItems = productCount,
            Products = products,
            ShopVouchers = shopVouchers
        };

        return View(vm);
    }

    public sealed record ToggleFollowRequest(int ShopId);

    [HttpPost("/shop/toggle-follow")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> ToggleFollow([FromBody] ToggleFollowRequest? req, CancellationToken cancellationToken)
    {
        if (User?.Identity?.IsAuthenticated != true)
            return Unauthorized(new { ok = false, message = "Vui lòng đăng nhập để theo dõi Gian hàng." });

        var rawId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(rawId, out var viewerId) || viewerId <= 0)
            return Unauthorized(new { ok = false, message = "Phiên đăng nhập không hợp lệ." });

        if (req == null || req.ShopId <= 0)
            return BadRequest(new { ok = false, message = "Gian hàng không hợp lệ." });

        if (req.ShopId == viewerId)
            return BadRequest(new { ok = false, message = "Bạn không thể tự theo dõi gian hàng của chính mình." });

        var existing = await _context.ShopFollowers
            .FirstOrDefaultAsync(f => f.FollowerAccountId == viewerId && f.ShopId == req.ShopId, cancellationToken);

        bool isNowFollowing;
        if (existing != null)
        {
            _context.ShopFollowers.Remove(existing);
            isNowFollowing = false;
        }
        else
        {
            _context.ShopFollowers.Add(new ShopFollower
            {
                FollowerAccountId = viewerId,
                ShopId = req.ShopId,
                CreatedAt = DateTime.UtcNow
            });
            isNowFollowing = true;
        }

        await _context.SaveChangesAsync(cancellationToken);
        var count = await _context.ShopFollowers
            .AsNoTracking()
            .CountAsync(f => f.ShopId == req.ShopId, cancellationToken);

        return Ok(new { ok = true, isFollowing = isNowFollowing, followerCount = count });
    }
}
