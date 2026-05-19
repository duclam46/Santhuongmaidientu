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
    public async Task<IActionResult> Index(int? page, int? categoryId, int? minRating, int? maxRating, CancellationToken cancellationToken)
    {
        const int pageSize = 12;
        var currentPage = page.GetValueOrDefault(1);
        if (currentPage < 1) currentPage = 1;

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

        var rootCategories = await _context.Categories
            .AsNoTracking()
            .Where(c => c.IsActive && !c.IsDeleted && c.ParentId == null)
            .OrderBy(c => c.Name)
            .Select(c => new { c.Id, c.Name })
            .ToListAsync(cancellationToken);

        var validRootCategoryIds = rootCategories.Select(c => c.Id).ToHashSet();
        int? selectedCategoryId = (categoryId.HasValue && validRootCategoryIds.Contains(categoryId.Value))
            ? categoryId.Value
            : null;

        var rootCategoryShopCounts = await (
                from p in _context.Products.AsNoTracking()
                join c in _context.Categories.AsNoTracking() on p.CategoryId equals c.Id
                where p.Status == "Active" && !p.IsDeleted
                      && c.IsActive && !c.IsDeleted
                select new
                {
                    RootCategoryId = c.ParentId ?? c.Id,
                    p.ShopId
                })
            .Distinct()
            .GroupBy(x => x.RootCategoryId)
            .Select(g => new { RootCategoryId = g.Key, ShopCount = g.Count() })
            .ToDictionaryAsync(x => x.RootCategoryId, x => x.ShopCount, cancellationToken);

        var categoryFilters = rootCategories
            .Select(c => new ShopCategoryFilterItemVm
            {
                CategoryId = c.Id,
                Name = c.Name,
                ShopCount = rootCategoryShopCounts.TryGetValue(c.Id, out var count) ? count : 0,
                IsSelected = selectedCategoryId == c.Id
            })
            .Where(x => x.ShopCount > 0)
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

        if (selectedCategoryId.HasValue)
        {
            var selectedChildrenIds = await _context.Categories
                .AsNoTracking()
                .Where(c => c.IsActive && !c.IsDeleted && c.ParentId == selectedCategoryId.Value)
                .Select(c => c.Id)
                .ToListAsync(cancellationToken);

            var categoryScopeIds = new List<int> { selectedCategoryId.Value };
            if (selectedChildrenIds.Count != 0)
            {
                categoryScopeIds.AddRange(selectedChildrenIds);
            }

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
            .OrderBy(a => a.Profile!.ShopName)
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
                ChatResponseRate = null
            },
            Page = currentPage,
            PageSize = pageSize,
            TotalItems = productCount,
            Products = products
        };

        return View(vm);
    }
}
