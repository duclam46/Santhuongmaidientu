using DOAN_BANHANG.Models;
using DOAN_BANHANG.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DOAN_BANHANG.Helpers;
using Microsoft.AspNetCore.Http;
using System.Linq;

namespace DOAN_BANHANG.Controllers;

public class ProductsController : Controller
{
    private readonly DataContext _context;

    private static string ColorHex(string? key)
    {
        key = (key ?? string.Empty).Trim().ToLowerInvariant();
        return key switch
        {
            "green" => "#088178",
            "blue" => "#3366FF",
            "yellow" => "#FFC107",
            "red" => "#CB0233",
            "primary" => "#088178",
            "info" => "#3366FF",
            "info-darker" => "#04535c",
            "warning" => "#FFC107",
            "error-light" => "#FFCDD2",
            "error-dark" => "#CB0233",
            _ => "#dfe3e8"
        };
    }

    public ProductsController(DataContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<IActionResult> QuickView(int id, CancellationToken cancellationToken)
    {
        if (id <= 0) return NotFound();

        var nowLocal = DateTime.Now;

        var product = await _context.Products
            .AsNoTracking()
            .Where(p => p.Id == id && p.Status == "Active")
            .Include(p => p.Category)
            .Include(p => p.Brand)
            .Include(p => p.Images)
            .Include(p => p.Options).ThenInclude(o => o.Values)
            .Include(p => p.Variants).ThenInclude(v => v.OptionValue1)
            .Include(p => p.Variants).ThenInclude(v => v.OptionValue2)
            .FirstOrDefaultAsync(cancellationToken);

        if (product is null) return NotFound();

        var optGroups = product.Options.OrderBy(o => o.SortOrder).ThenBy(o => o.Id).ToList();
        var opt1Label = optGroups.Count > 0 ? optGroups[0].Name : "Màu sắc";
        var opt2Label = optGroups.Count > 1 ? optGroups[1].Name : "Kích cỡ";

        var activeVariants = product.Variants.Where(v => v.IsActive && !v.IsDeleted).ToList();

        var vm = new QuickViewVm
        {
            ProductId = product.Id,
            Name = product.Name,
            Slug = product.Slug,
            CategoryName = product.Category?.Name,
            BrandName = product.Brand?.Name,
            Option1Label = opt1Label,
            Option2Label = opt2Label,
            AverageRating = product.AverageRating,
            ReviewCount = product.ReviewCount,
            Description = product.Description,
            ImageUrls = product.Images
                .OrderByDescending(i => i.IsPrimary)
                .ThenBy(i => i.Id)
                .Select(i => i.ImageUrl)
                .ToList(),
            Variants = activeVariants
                .Select(v => new QuickViewVariantVm
                {
                    VariantId = v.Id,
                    Color = v.OptionValue1?.Value,
                    Size = v.OptionValue2?.Value,
                    Price = v.SalePrice.HasValue
                        && (!v.SaleStartDate.HasValue || v.SaleStartDate <= nowLocal)
                        && (!v.SaleEndDate.HasValue || v.SaleEndDate >= nowLocal)
                        ? v.SalePrice.Value
                        : v.Price,
                    StockQuantity = Math.Max(0, v.StockQuantity - v.ReservedStock)
                })
                .ToList(),
            Colors = activeVariants
                .Select(v => new
                {
                    Name = v.OptionValue1?.Value,
                    Code = (string?)null
                })
                .Where(c => !string.IsNullOrWhiteSpace(c.Name))
                .GroupBy(c => c.Name!)
                .Select(g => new QuickViewColorVm
                {
                    Key = g.Key,
                    Hex = string.IsNullOrWhiteSpace(g.First().Code) ? ColorHex(g.Key) : g.First().Code!
                })
                .ToList(),
            Sizes = activeVariants
                .Select(v => v.OptionValue2?.Value)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct()
                .Select(s => new QuickViewSizeVm { Label = s! })
                .ToList()
        };

        var defaultVariant = vm.Variants
            .Where(v => v.StockQuantity > 0)
            .OrderBy(v => v.Price)
            .FirstOrDefault()
            ?? vm.Variants
                .OrderBy(v => v.Price)
                .ThenByDescending(v => v.StockQuantity)
                .FirstOrDefault();

        vm.DefaultColor = defaultVariant?.Color;
        vm.DefaultSize = defaultVariant?.Size;
        vm.Price = defaultVariant?.Price
            ?? vm.Variants.OrderBy(v => v.Price).FirstOrDefault()?.Price
            ?? 0m;

        return PartialView("_QuickViewSidebarContent", vm);
    }

    private Dictionary<int, string> ParseSelectedAttributesFromQuery()
    {
        var result = new Dictionary<int, string>();
        if (Request?.Query == null) return result;
        foreach (var kv in Request.Query)
        {
            if (kv.Key.StartsWith("attr_", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(kv.Key["attr_".Length..], out var attrId) &&
                attrId > 0)
            {
                var val = kv.Value.ToString().Trim();
                if (!string.IsNullOrWhiteSpace(val))
                {
                    result[attrId] = val;
                }
            }
        }
        return result;
    }

    private async Task<(List<BrandFilterItemVm> Brands, List<DynamicAttributeFilterVm> DynamicAttributes)> BuildSidebarFiltersAsync(
        IQueryable<Product> baseQuery,
        List<int>? categoryScopeIds,
        int? selectedBrandId,
        Dictionary<int, string> selectedAttributes,
        CancellationToken cancellationToken)
    {
        var brandCounts = await baseQuery
            .Where(p => p.BrandId != null)
            .GroupBy(p => p.BrandId!.Value)
            .Select(g => new { BrandId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.BrandId, x => x.Count, cancellationToken);

        var allBrands = await _context.Brands
            .AsNoTracking()
            .Where(b => b.IsActive)
            .OrderBy(b => b.Name)
            .Select(b => new BrandFilterItemVm
            {
                BrandId = b.Id,
                Name = b.Name,
                LogoUrl = b.LogoUrl,
                ProductCount = brandCounts.ContainsKey(b.Id) ? brandCounts[b.Id] : 0,
                IsSelected = selectedBrandId.HasValue && selectedBrandId.Value == b.Id
            })
            .ToListAsync(cancellationToken);

        var dynamicAttrs = new List<DynamicAttributeFilterVm>();
        if (categoryScopeIds != null && categoryScopeIds.Count > 0)
        {
            var allCatMap = await _context.Categories.AsNoTracking().ToDictionaryAsync(c => c.Id, cancellationToken);
            var targetCatIds = new HashSet<int>(categoryScopeIds);
            foreach (var cid in categoryScopeIds)
            {
                int? cur = cid;
                int guard = 0;
                while (cur.HasValue && allCatMap.TryGetValue(cur.Value, out var catObj) && guard++ < 10)
                {
                    targetCatIds.Add(catObj.Id);
                    cur = catObj.ParentId;
                }
            }

            var catAttrs = await _context.CategoryAttributes
                .AsNoTracking()
                .Where(ca => ca.IsFilterable && targetCatIds.Contains(ca.CategoryId))
                .OrderBy(ca => ca.SortOrder)
                .ThenBy(ca => ca.Id)
                .ToListAsync(cancellationToken);

            var attrIds = catAttrs.Select(a => a.Id).ToList();
            if (attrIds.Count > 0)
            {
                var usedValues = await _context.ProductAttributeValues
                    .AsNoTracking()
                    .Where(av => attrIds.Contains(av.CategoryAttributeId) && !string.IsNullOrWhiteSpace(av.Value))
                    .Select(av => new { av.CategoryAttributeId, av.Value })
                    .Distinct()
                    .ToListAsync(cancellationToken);

                foreach (var ca in catAttrs)
                {
                    var vals = usedValues
                        .Where(v => v.CategoryAttributeId == ca.Id)
                        .Select(v => v.Value.Trim())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(v => v)
                        .ToList();

                    if (vals.Count > 0 || selectedAttributes.ContainsKey(ca.Id))
                    {
                        dynamicAttrs.Add(new DynamicAttributeFilterVm
                        {
                            AttributeId = ca.Id,
                            Name = ca.Name,
                            Unit = ca.Unit,
                            AvailableValues = vals,
                            SelectedValue = selectedAttributes.TryGetValue(ca.Id, out var selVal) ? selVal : null
                        });
                    }
                }
            }
        }

        return (allBrands, dynamicAttrs);
    }

    [HttpGet("search")]
    public async Task<IActionResult> Search(
        string? q,
        int? childCategoryId,
        int? brandId,
        int? rating,
        string? color,
        string? size,
        decimal? minPrice,
        decimal? maxPrice,
        int? page,
        string? sort,
        CancellationToken cancellationToken)
    {
        var nowLocal = DateTime.Now;
        var queryTerm = (q ?? string.Empty).Trim();

        var baseQuery = _context.Products
            .AsNoTracking()
            .Where(p => p.Status == "Active");

        if (!string.IsNullOrWhiteSpace(queryTerm))
        {
            baseQuery = baseQuery.Where(p => p.Name.Contains(queryTerm) || p.Description.Contains(queryTerm));
        }

        var rootCategories = await _context.Categories
            .AsNoTracking()
            .Where(c => c.IsActive && !c.IsDeleted && c.ParentId == null)
            .OrderBy(c => c.Name)
            .Select(c => new { c.Id, c.Name })
            .ToListAsync(cancellationToken);

        var validRootCategoryIds = rootCategories.Select(c => c.Id).ToHashSet();
        int? selectedCategoryId = (childCategoryId.HasValue && validRootCategoryIds.Contains(childCategoryId.Value))
            ? childCategoryId.Value
            : null;

        if (selectedCategoryId.HasValue)
        {
            var categoryScopeIds = await _context.Categories
                .AsNoTracking()
                .Where(c => c.IsActive && (
                    c.Id == selectedCategoryId.Value ||
                    c.ParentId == selectedCategoryId.Value ||
                    c.Parent!.ParentId == selectedCategoryId.Value ||
                    c.Parent!.Parent!.ParentId == selectedCategoryId.Value
                ))
                .Select(c => c.Id)
                .ToListAsync(cancellationToken);

            baseQuery = baseQuery.Where(p => categoryScopeIds.Contains(p.CategoryId));
        }

        var childCategoryCounts = new Dictionary<int, int>();
        foreach (var c in rootCategories)
        {
            var scopeIds = await _context.Categories
                .AsNoTracking()
                .Where(cat => cat.IsActive && (
                    cat.Id == c.Id ||
                    cat.ParentId == c.Id ||
                    cat.Parent!.ParentId == c.Id ||
                    cat.Parent!.Parent!.ParentId == c.Id
                ))
                .Select(cat => cat.Id)
                .ToListAsync(cancellationToken);

            var cnt = await _context.Products
                .AsNoTracking()
                .Where(p => p.Status == "Active" && (p.Name.Contains(queryTerm) || p.Description.Contains(queryTerm)))
                .Where(p => scopeIds.Contains(p.CategoryId))
                .CountAsync(cancellationToken);

            childCategoryCounts[c.Id] = cnt;
        }

        var effectivePricesQuery =
            from p in baseQuery.Select(x => x.Id)
            join v in _context.ProductVariants.AsNoTracking().Where(v => v.IsActive && !v.IsDeleted) on p equals v.ProductId into variants
            from v in variants.DefaultIfEmpty()
            group v by p into g
            select new
            {
                EffectivePrice = g.Min(x => x == null
                    ? (decimal?)null
                    : ((x.SalePrice.HasValue
                            && (!x.SaleStartDate.HasValue || x.SaleStartDate <= nowLocal)
                            && (!x.SaleEndDate.HasValue || x.SaleEndDate >= nowLocal))
                        ? x.SalePrice.Value
                        : x.Price))
            };

        var priceStats = await effectivePricesQuery
            .GroupBy(_ => 1)
            .Select(g => new { Min = g.Min(x => x.EffectivePrice), Max = g.Max(x => x.EffectivePrice) })
            .FirstOrDefaultAsync(cancellationToken);

        var priceMin = priceStats?.Min ?? 0m;
        var priceMax = priceStats?.Max ?? 0m;

        decimal? effectiveMinPrice = minPrice;
        decimal? effectiveMaxPrice = maxPrice;
        if (priceMax > 0m)
        {
            if (effectiveMinPrice.HasValue && effectiveMinPrice.Value > priceMax) effectiveMinPrice = null;
            if (effectiveMaxPrice.HasValue && effectiveMaxPrice.Value < priceMin) effectiveMaxPrice = null;
            if (effectiveMinPrice.HasValue && effectiveMinPrice.Value < 0m) effectiveMinPrice = null;
            if (effectiveMaxPrice.HasValue && effectiveMaxPrice.Value > priceMax) effectiveMaxPrice = priceMax;
            if (effectiveMinPrice.HasValue && effectiveMaxPrice.HasValue && effectiveMaxPrice.Value < effectiveMinPrice.Value)
            {
                (effectiveMinPrice, effectiveMaxPrice) = (effectiveMaxPrice, effectiveMinPrice);
            }
        }

        var query = baseQuery;

        var selectedAttributes = ParseSelectedAttributesFromQuery();
        if (brandId.HasValue && brandId.Value > 0)
        {
            query = query.Where(p => p.BrandId == brandId.Value);
        }
        foreach (var kv in selectedAttributes)
        {
            var aId = kv.Key;
            var aVal = kv.Value;
            query = query.Where(p => p.AttributeValues.Any(av => av.CategoryAttributeId == aId && av.Value == aVal));
        }

        int? selectedRating = null;
        if (rating.HasValue)
        {
            selectedRating = Math.Clamp(rating.Value, 1, 5);
            query = query.Where(p => p.AverageRating >= selectedRating.Value);
        }

        string? selectedColor = null;
        if (!string.IsNullOrWhiteSpace(color))
        {
            selectedColor = color.Trim().ToLowerInvariant();
            query = query.Where(p => p.Variants.Any(v =>
                v.IsActive && v.OptionValue1 != null && v.OptionValue1.Value.ToLower() == selectedColor));
        }

        string? selectedSize = null;
        if (!string.IsNullOrWhiteSpace(size))
        {
            selectedSize = size.Trim().ToUpperInvariant();
            query = query.Where(p => p.Variants.Any(v =>
                v.IsActive && v.OptionValue2 != null && v.OptionValue2.Value.ToUpper() == selectedSize));
        }

        if (effectiveMinPrice.HasValue)
        {
            query = query.Where(p =>
                p.Variants
                    .Where(v => v.IsActive)
                    .Select(v => (decimal?)((v.SalePrice.HasValue
                            && (!v.SaleStartDate.HasValue || v.SaleStartDate <= nowLocal)
                            && (!v.SaleEndDate.HasValue || v.SaleEndDate >= nowLocal))
                        ? v.SalePrice.Value
                        : v.Price))
                    .Min() >= effectiveMinPrice.Value);
        }

        if (effectiveMaxPrice.HasValue)
        {
            query = query.Where(p =>
                p.Variants
                    .Where(v => v.IsActive)
                    .Select(v => (decimal?)((v.SalePrice.HasValue
                            && (!v.SaleStartDate.HasValue || v.SaleStartDate <= nowLocal)
                            && (!v.SaleEndDate.HasValue || v.SaleEndDate >= nowLocal))
                        ? v.SalePrice.Value
                        : v.Price))
                    .Min() <= effectiveMaxPrice.Value);
        }

        query = (sort ?? string.Empty).ToLowerInvariant() switch
        {
            "name_desc" => query.OrderByDescending(p => p.Name),
            "price_asc" => query.OrderBy(p => p.Variants
                    .Where(v => v.IsActive)
                    .Select(v => (decimal?)((v.SalePrice.HasValue
                            && (!v.SaleStartDate.HasValue || v.SaleStartDate <= nowLocal)
                            && (!v.SaleEndDate.HasValue || v.SaleEndDate >= nowLocal))
                        ? v.SalePrice.Value
                        : v.Price))
                    .Min() ?? 0m)
                .ThenBy(p => p.Name),
            "price_desc" => query.OrderByDescending(p => p.Variants
                    .Where(v => v.IsActive)
                    .Select(v => (decimal?)((v.SalePrice.HasValue
                            && (!v.SaleStartDate.HasValue || v.SaleStartDate <= nowLocal)
                            && (!v.SaleEndDate.HasValue || v.SaleEndDate >= nowLocal))
                        ? v.SalePrice.Value
                        : v.Price))
                    .Min() ?? 0m)
                .ThenBy(p => p.Name),
            "rating_desc" => query.OrderByDescending(p => p.AverageRating).ThenByDescending(p => p.ReviewCount).ThenBy(p => p.Name),
            "rating_asc" => query.OrderBy(p => p.AverageRating).ThenBy(p => p.ReviewCount).ThenBy(p => p.Name),
            "newest" => query.OrderByDescending(p => p.CreatedAt),
            _ => query.OrderBy(p => p.Name)
        };

        const int pageSize = 12;
        var effectivePage = page.GetValueOrDefault(1);
        if (effectivePage < 1) effectivePage = 1;

        var totalItems = await query.CountAsync(cancellationToken);
        var totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);

        var productsRaw = await query
            .Skip((effectivePage - 1) * pageSize)
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
                    .Select(i => i.ImageUrl)
                    .FirstOrDefault(),
                Price = p.Variants
                    .Where(v => v.IsActive)
                    .Select(v => (decimal?)((v.SalePrice.HasValue
                            && (!v.SaleStartDate.HasValue || v.SaleStartDate <= nowLocal)
                            && (!v.SaleEndDate.HasValue || v.SaleEndDate >= nowLocal))
                        ? v.SalePrice.Value
                        : v.Price))
                    .Min() ?? 0m,
                OldPrice = p.Variants
                    .Where(v => v.IsActive
                        && v.SalePrice.HasValue
                        && (!v.SaleStartDate.HasValue || v.SaleStartDate <= nowLocal)
                        && (!v.SaleEndDate.HasValue || v.SaleEndDate >= nowLocal)
                        && v.SalePrice.Value < v.Price)
                    .Select(v => (decimal?)v.Price)
                    .Min()
            })
            .ToListAsync(cancellationToken);

        var products = productsRaw
            .Select(p => new ProductCardVm
            {
                ProductId = p.ProductId,
                Name = p.Name,
                Slug = p.Slug,
                Price = p.Price,
                DefaultVariantId = p.DefaultVariantId,
                OldPrice = p.OldPrice.HasValue && p.OldPrice.Value > p.Price ? p.OldPrice : null,
                RatingAverage = p.RatingAverage,
                RatingCount = p.RatingCount,
                DiscountPercent = p.OldPrice.HasValue && p.OldPrice.Value > p.Price && p.OldPrice.Value > 0m
                    ? (int)Math.Round((p.OldPrice.Value - p.Price) * 100m / p.OldPrice.Value)
                    : 0,
                PrimaryImageUrl = p.PrimaryImageUrl
            })
            .ToList();

        var (brandsFilter, dynamicAttrsFilter) = await BuildSidebarFiltersAsync(
            baseQuery,
            selectedCategoryId.HasValue ? new List<int> { selectedCategoryId.Value } : null,
            brandId,
            selectedAttributes,
            cancellationToken);

        var vm = new CategoryProductsVm
        {
            Page = effectivePage,
            PageSize = pageSize,
            TotalItems = totalItems,
            CategoryName = string.IsNullOrWhiteSpace(queryTerm) ? "Tất cả sản phẩm" : $"Kết quả tìm kiếm cho: \"{queryTerm}\"",
            CategorySlug = null,
            SearchTerm = queryTerm,
            Sort = sort,
            SelectedChildCategoryId = selectedCategoryId,
            SelectedBrandId = brandId,
            Brands = brandsFilter,
            DynamicAttributes = dynamicAttrsFilter,
            SelectedAttributes = selectedAttributes,
            SelectedMinPrice = effectiveMinPrice,
            SelectedMaxPrice = effectiveMaxPrice,
            SelectedRating = selectedRating,
            SelectedColor = selectedColor,
            SelectedSize = selectedSize,
            PriceMin = priceMin,
            PriceMax = priceMax,
            ChildCategories = rootCategories
                .Select(c => new CategoryFilterVm
                {
                    CategoryId = c.Id,
                    Name = c.Name,
                    ProductCount = childCategoryCounts.TryGetValue(c.Id, out var cnt) ? cnt : 0,
                    IsSelected = selectedCategoryId.HasValue && c.Id == selectedCategoryId.Value
                })
                .Where(c => c.ProductCount > 0)
                .ToList(),
            Products = products
        };

        return View("Index", vm);
    }

    [HttpGet("api/products/search-suggestions")]
    public async Task<IActionResult> SearchSuggestions(string? q, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(q))
        {
            return Json(new List<object>());
        }

        var searchTerm = q.Trim().ToLower();

        var products = await _context.Products
            .AsNoTracking()
            .Where(p => p.Status == "Active" && (p.Name.ToLower().Contains(searchTerm) || p.Description.ToLower().Contains(searchTerm)))
            .OrderByDescending(p => p.AverageRating)
            .Take(5)
            .Select(p => new
            {
                id = p.Id,
                name = p.Name,
                slug = p.Slug,
                imageUrl = p.Images.OrderByDescending(i => i.IsPrimary).Select(i => i.ImageUrl).FirstOrDefault(),
                price = p.Variants
                    .Where(v => v.IsActive)
                    .Select(v => (decimal?)((v.SalePrice.HasValue
                            && (!v.SaleStartDate.HasValue || v.SaleStartDate <= DateTime.Now)
                            && (!v.SaleEndDate.HasValue || v.SaleEndDate >= DateTime.Now))
                        ? v.SalePrice.Value
                        : v.Price))
                    .Min() ?? 0m
            })
            .ToListAsync(cancellationToken);

        return Json(products);
    }

    [HttpGet("api/search/live-suggest")]
    public async Task<IActionResult> LiveSuggest(string? q, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(q))
        {
            return Json(new { shops = Array.Empty<object>(), products = Array.Empty<object>() });
        }

        var term = q.Trim().ToLower();
        var now = DateTime.Now;

        var rawShops = await _context.Accounts
            .AsNoTracking()
            .Where(a => a.Status == "Active" && !a.IsDeleted && a.Profile != null && !string.IsNullOrWhiteSpace(a.Profile.ShopSlug))
            .Where(a => a.Profile!.ShopName != null && a.Profile.ShopName.ToLower().Contains(term))
            .OrderByDescending(a => a.Profile!.IsMall)
            .Take(3)
            .Select(a => new
            {
                name = a.Profile!.ShopName ?? a.Email,
                slug = a.Profile!.ShopSlug!,
                logoUrl = a.Profile!.ShopLogoUrl,
                isMall = a.Profile!.IsMall,
                productCount = _context.Products.Count(p => p.ShopId == a.Id && p.Status == "Active" && !p.IsDeleted)
            })
            .ToListAsync(cancellationToken);

        var shops = rawShops.Select(s => new
        {
            s.name,
            s.slug,
            logoUrl = ImageUrlHelper.NormalizeStaticFileUrl(s.logoUrl) ?? "/assets/images/vendors-list/vendor-profile.png",
            s.isMall,
            s.productCount
        }).ToList();

        var rawProducts = await _context.Products
            .AsNoTracking()
            .Where(p => p.Status == "Active" && !p.IsDeleted
                && (p.Name.ToLower().Contains(term)
                    || (p.Brand != null && p.Brand.Name.ToLower().Contains(term))
                    || (p.Category != null && p.Category.Name.ToLower().Contains(term))))
            .OrderByDescending(p => p.AverageRating)
            .ThenByDescending(p => p.ReviewCount)
            .Take(6)
            .Select(p => new
            {
                id = p.Id,
                name = p.Name,
                slug = p.Slug,
                rating = p.AverageRating,
                imageUrl = p.Images.OrderByDescending(i => i.IsPrimary).ThenBy(i => i.SortOrder).Select(i => i.ImageUrl).FirstOrDefault(),
                price = p.Variants
                    .Where(v => v.IsActive && !v.IsDeleted)
                    .Select(v => (decimal?)((v.SalePrice.HasValue
                            && (!v.SaleStartDate.HasValue || v.SaleStartDate <= now)
                            && (!v.SaleEndDate.HasValue || v.SaleEndDate >= now))
                        ? v.SalePrice.Value
                        : v.Price))
                    .Min() ?? 0m,
                oldPrice = p.Variants
                    .Where(v => v.IsActive && !v.IsDeleted && v.SalePrice.HasValue && v.SalePrice.Value < v.Price
                            && (!v.SaleStartDate.HasValue || v.SaleStartDate <= now)
                            && (!v.SaleEndDate.HasValue || v.SaleEndDate >= now))
                    .Select(v => (decimal?)v.Price)
                    .Min()
            })
            .ToListAsync(cancellationToken);

        var products = rawProducts.Select(p => new
        {
            p.id,
            p.name,
            p.slug,
            rating = p.rating > 0 ? p.rating.ToString("0.0") : "5.0",
            imageUrl = ImageUrlHelper.NormalizeStaticFileUrl(p.imageUrl) ?? "/assets/images/vitamin-c.png",
            p.price,
            p.oldPrice
        }).ToList();

        return Json(new { shops, products });
    }

    [HttpGet("api/products/get-by-ids")]
    public async Task<IActionResult> GetProductsByIds([FromQuery] List<int> ids, CancellationToken cancellationToken)
    {
        if (ids == null || !ids.Any())
        {
            return Json(new List<object>());
        }

        var products = await _context.Products
            .AsNoTracking()
            .Where(p => p.Status == "Active" && !p.IsDeleted && ids.Contains(p.Id))
            .Select(p => new
            {
                id = p.Id,
                name = p.Name,
                slug = p.Slug,
                imageUrl = p.Images.OrderByDescending(i => i.IsPrimary).Select(i => i.ImageUrl).FirstOrDefault(),
                minPrice = (int)(p.Variants
                    .Where(v => v.IsActive && !v.IsDeleted)
                    .Select(v => (decimal?)((v.SalePrice.HasValue
                            && (!v.SaleStartDate.HasValue || v.SaleStartDate <= DateTime.Now)
                            && (!v.SaleEndDate.HasValue || v.SaleEndDate >= DateTime.Now))
                        ? v.SalePrice.Value
                        : v.Price))
                    .Min() ?? 0m),
                maxPrice = (int)(p.Variants
                    .Where(v => v.IsActive && !v.IsDeleted)
                    .Select(v => (decimal?)((v.SalePrice.HasValue
                            && (!v.SaleStartDate.HasValue || v.SaleStartDate <= DateTime.Now)
                            && (!v.SaleEndDate.HasValue || v.SaleEndDate >= DateTime.Now))
                        ? v.SalePrice.Value
                        : v.Price))
                    .Max() ?? 0m),
                rating = (double)p.AverageRating
            })
            .ToListAsync(cancellationToken);

        var orderedResult = ids
            .Select(id => products.FirstOrDefault(p => p.id == id))
            .Where(p => p != null)
            .Select(p => new
            {
                p!.id,
                p.name,
                p.slug,
                imageUrl = ImageUrlHelper.NormalizeStaticFileUrl(p.imageUrl),
                p.minPrice,
                p.maxPrice,
                p.rating
            })
            .ToList();

        return Json(orderedResult);
    }



    [HttpGet]
    public async Task<IActionResult> Index(
        string slug,
        int? childCategoryId,
        int? brandId,
        int? rating,
        string? color,
        string? size,
        decimal? minPrice,
        decimal? maxPrice,
        int? page,
        string? sort,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            return NotFound();
        }

        var nowLocal = DateTime.Now;

        var category = await _context.Categories
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.IsActive && c.Slug == slug, cancellationToken);

        if (category is null)
        {
            return NotFound();
        }

        var childCategories = await _context.Categories
            .AsNoTracking()
            .Where(c => c.IsActive && c.ParentId == category.Id)
            .OrderBy(c => c.Name)
            .Select(c => new { CategoryId = c.Id, c.Name })
            .ToListAsync(cancellationToken);

        var childCategoryIds = childCategories.Select(c => c.CategoryId).ToList();

        int? effectiveChildCategoryId = null;
        if (childCategoryId.HasValue && childCategoryIds.Contains(childCategoryId.Value))
        {
            effectiveChildCategoryId = childCategoryId.Value;
        }

        var activeScopeRootId = effectiveChildCategoryId ?? category.Id;

        var categoryScopeIds = await _context.Categories
            .AsNoTracking()
            .Where(c => c.IsActive && (
                c.Id == activeScopeRootId ||
                c.ParentId == activeScopeRootId ||
                c.Parent!.ParentId == activeScopeRootId ||
                c.Parent!.Parent!.ParentId == activeScopeRootId
            ))
            .Select(c => c.Id)
            .ToListAsync(cancellationToken);

        var baseQuery = _context.Products
            .AsNoTracking()
            .Where(p => p.Status == "Active")
            .Where(p => categoryScopeIds.Contains(p.CategoryId));

        var childCategoryCounts = new Dictionary<int, int>();
        if (childCategoryIds.Count > 0)
        {
            foreach (var cid in childCategoryIds)
            {
                var cnt = await _context.Products
                    .AsNoTracking()
                    .Where(p => p.Status == "Active" && (
                        p.CategoryId == cid ||
                        p.Category!.ParentId == cid ||
                        p.Category!.Parent!.ParentId == cid ||
                        p.Category!.Parent!.Parent!.ParentId == cid
                    ))
                    .CountAsync(cancellationToken);
                childCategoryCounts[cid] = cnt;
            }
        }

        // SQL Server doesn't allow aggregating over an expression that contains an aggregate/subquery.
        // Compute the effective per-product price in a derived query (left-join variants, group by product),
        // then compute Min/Max over that derived scalar.
        var effectivePricesQuery =
            from p in baseQuery.Select(x => x.Id)
            join v in _context.ProductVariants.AsNoTracking().Where(v => v.IsActive && !v.IsDeleted) on p equals v.ProductId into variants
            from v in variants.DefaultIfEmpty()
            group v by p into g
            select new
            {
                EffectivePrice = g.Min(x => x == null
                    ? (decimal?)null
                    : ((x.SalePrice.HasValue
                            && (!x.SaleStartDate.HasValue || x.SaleStartDate <= nowLocal)
                            && (!x.SaleEndDate.HasValue || x.SaleEndDate >= nowLocal))
                        ? x.SalePrice.Value
                        : x.Price))
            };

        var priceStats = await effectivePricesQuery
            .GroupBy(_ => 1)
            .Select(g => new { Min = g.Min(x => x.EffectivePrice), Max = g.Max(x => x.EffectivePrice) })
            .FirstOrDefaultAsync(cancellationToken);

        var priceMin = priceStats?.Min ?? 0m;
        var priceMax = priceStats?.Max ?? 0m;

        // Guard against invalid/buggy price-range submissions.
        decimal? effectiveMinPrice = minPrice;
        decimal? effectiveMaxPrice = maxPrice;
        if (priceMax > 0m)
        {
            if (effectiveMinPrice.HasValue && effectiveMinPrice.Value > priceMax)
            {
                effectiveMinPrice = null;
            }

            if (effectiveMaxPrice.HasValue && effectiveMaxPrice.Value < priceMin)
            {
                effectiveMaxPrice = null;
            }

            if (effectiveMinPrice.HasValue && effectiveMinPrice.Value < 0m)
            {
                effectiveMinPrice = null;
            }

            if (effectiveMaxPrice.HasValue && effectiveMaxPrice.Value > priceMax)
            {
                effectiveMaxPrice = priceMax;
            }

            if (effectiveMinPrice.HasValue && effectiveMaxPrice.HasValue && effectiveMaxPrice.Value < effectiveMinPrice.Value)
            {
                (effectiveMinPrice, effectiveMaxPrice) = (effectiveMaxPrice, effectiveMinPrice);
            }
        }

        var query = baseQuery;

        var selectedAttributes = ParseSelectedAttributesFromQuery();
        if (brandId.HasValue && brandId.Value > 0)
        {
            query = query.Where(p => p.BrandId == brandId.Value);
        }
        foreach (var kv in selectedAttributes)
        {
            var aId = kv.Key;
            var aVal = kv.Value;
            query = query.Where(p => p.AttributeValues.Any(av => av.CategoryAttributeId == aId && av.Value == aVal));
        }

        int? selectedRating = null;
        if (rating.HasValue)
        {
            selectedRating = Math.Clamp(rating.Value, 1, 5);
            query = query.Where(p => p.AverageRating >= selectedRating.Value);
        }

        string? selectedColor = null;
        if (!string.IsNullOrWhiteSpace(color))
        {
            selectedColor = color.Trim().ToLowerInvariant();
            if (selectedColor.Length > 50) selectedColor = selectedColor[..50];
            query = query.Where(p => p.Variants.Any(v =>
                v.IsActive && !v.IsDeleted && v.OptionValue1 != null && v.OptionValue1.Value.ToLower() == selectedColor));
        }

        string? selectedSize = null;
        if (!string.IsNullOrWhiteSpace(size))
        {
            selectedSize = size.Trim().ToUpperInvariant();
            if (selectedSize.Length > 50) selectedSize = selectedSize[..50];
            query = query.Where(p => p.Variants.Any(v =>
                v.IsActive && !v.IsDeleted && v.OptionValue2 != null && v.OptionValue2.Value.ToUpper() == selectedSize));
        }

        if (effectiveMinPrice.HasValue)
        {
            query = query.Where(p =>
                p.Variants
                    .Where(v => v.IsActive && !v.IsDeleted)
                    .Select(v => (decimal?)((v.SalePrice.HasValue
                            && (!v.SaleStartDate.HasValue || v.SaleStartDate <= nowLocal)
                            && (!v.SaleEndDate.HasValue || v.SaleEndDate >= nowLocal))
                        ? v.SalePrice.Value
                        : v.Price))
                    .Min() >= effectiveMinPrice.Value);
        }

        if (effectiveMaxPrice.HasValue)
        {
            query = query.Where(p =>
                p.Variants
                    .Where(v => v.IsActive && !v.IsDeleted)
                    .Select(v => (decimal?)((v.SalePrice.HasValue
                            && (!v.SaleStartDate.HasValue || v.SaleStartDate <= nowLocal)
                            && (!v.SaleEndDate.HasValue || v.SaleEndDate >= nowLocal))
                        ? v.SalePrice.Value
                        : v.Price))
                    .Min() <= effectiveMaxPrice.Value);
        }

        // Redirect to normalized querystring if we changed bounds.
        if (effectiveMinPrice != minPrice || effectiveMaxPrice != maxPrice)
        {
            return RedirectToRoute("category", new
            {
                slug,
                childCategoryId = effectiveChildCategoryId,
                rating = selectedRating,
                color = selectedColor,
                size = selectedSize,
                minPrice = effectiveMinPrice,
                maxPrice = effectiveMaxPrice,
                page,
                sort
            });
        }

        query = (sort ?? string.Empty).ToLowerInvariant() switch
        {
            "name_desc" => query.OrderByDescending(p => p.Name),
            "price_asc" => query.OrderBy(p => p.Variants
                    .Where(v => v.IsActive && !v.IsDeleted)
                    .Select(v => (decimal?)((v.SalePrice.HasValue
                            && (!v.SaleStartDate.HasValue || v.SaleStartDate <= nowLocal)
                            && (!v.SaleEndDate.HasValue || v.SaleEndDate >= nowLocal))
                        ? v.SalePrice.Value
                        : v.Price))
                    .Min() ?? 0m)
                .ThenBy(p => p.Name),
            "price_desc" => query.OrderByDescending(p => p.Variants
                    .Where(v => v.IsActive && !v.IsDeleted)
                    .Select(v => (decimal?)((v.SalePrice.HasValue
                            && (!v.SaleStartDate.HasValue || v.SaleStartDate <= nowLocal)
                            && (!v.SaleEndDate.HasValue || v.SaleEndDate >= nowLocal))
                        ? v.SalePrice.Value
                        : v.Price))
                    .Min() ?? 0m)
                .ThenBy(p => p.Name),
            "rating_desc" => query.OrderByDescending(p => p.AverageRating).ThenByDescending(p => p.ReviewCount).ThenBy(p => p.Name),
            "rating_asc" => query.OrderBy(p => p.AverageRating).ThenBy(p => p.ReviewCount).ThenBy(p => p.Name),
            "newest" => query.OrderByDescending(p => p.CreatedAt),
            _ => query.OrderBy(p => p.Name)
        };

        const int pageSize = 12;
        var effectivePage = page.GetValueOrDefault(1);
        if (effectivePage < 1) effectivePage = 1;

        var totalItems = await query.CountAsync(cancellationToken);
        var totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);

        if (totalPages > 0 && effectivePage > totalPages)
        {
            return RedirectToRoute("category", new
            {
                slug,
                childCategoryId = effectiveChildCategoryId,
                rating = selectedRating,
                color = selectedColor,
                size = selectedSize,
                minPrice = effectiveMinPrice,
                maxPrice = effectiveMaxPrice,
                page = totalPages,
                sort
            });
        }

        var productsRaw = await query
            .Skip((effectivePage - 1) * pageSize)
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
                    .Select(i => i.ImageUrl)
                    .FirstOrDefault(),
                Price = p.Variants
                    .Where(v => v.IsActive && !v.IsDeleted)
                    .Select(v => (decimal?)((v.SalePrice.HasValue
                            && (!v.SaleStartDate.HasValue || v.SaleStartDate <= nowLocal)
                            && (!v.SaleEndDate.HasValue || v.SaleEndDate >= nowLocal))
                        ? v.SalePrice.Value
                        : v.Price))
                    .Min() ?? 0m,
                OldPrice = p.Variants
                    .Where(v => v.IsActive && !v.IsDeleted
                        && v.SalePrice.HasValue
                        && (!v.SaleStartDate.HasValue || v.SaleStartDate <= nowLocal)
                        && (!v.SaleEndDate.HasValue || v.SaleEndDate >= nowLocal)
                        && v.SalePrice.Value < v.Price)
                    .Select(v => (decimal?)v.Price)
                    .Min()
            })
            .ToListAsync(cancellationToken);

        var products = productsRaw
            .Select(p => new ProductCardVm
            {
                ProductId = p.ProductId,
                Name = p.Name,
                Slug = p.Slug,
                Price = p.Price,
                DefaultVariantId = p.DefaultVariantId,
                OldPrice = p.OldPrice.HasValue && p.OldPrice.Value > p.Price ? p.OldPrice : null,
                RatingAverage = p.RatingAverage,
                RatingCount = p.RatingCount,
                DiscountPercent = p.OldPrice.HasValue && p.OldPrice.Value > p.Price && p.OldPrice.Value > 0m
                    ? (int)Math.Round((p.OldPrice.Value - p.Price) * 100m / p.OldPrice.Value)
                    : 0,
                PrimaryImageUrl = p.PrimaryImageUrl
            })
            .ToList();

        var (brandsFilter, dynamicAttrsFilter) = await BuildSidebarFiltersAsync(
            baseQuery,
            categoryScopeIds,
            brandId,
            selectedAttributes,
            cancellationToken);

        var vm = new CategoryProductsVm
        {
            Page = effectivePage,
            PageSize = pageSize,
            TotalItems = totalItems,
            CategoryName = category.Name,
            CategorySlug = category.Slug,
            Sort = sort,
            SelectedChildCategoryId = effectiveChildCategoryId,
            SelectedBrandId = brandId,
            Brands = brandsFilter,
            DynamicAttributes = dynamicAttrsFilter,
            SelectedAttributes = selectedAttributes,
            SelectedMinPrice = effectiveMinPrice,
            SelectedMaxPrice = effectiveMaxPrice,
            SelectedRating = selectedRating,
            SelectedColor = selectedColor,
            SelectedSize = selectedSize,
            PriceMin = priceMin,
            PriceMax = priceMax,
            ChildCategories = childCategories
                .Select(c => new CategoryFilterVm
                {
                    CategoryId = c.CategoryId,
                    Name = c.Name,
                    ProductCount = childCategoryCounts.TryGetValue(c.CategoryId, out var cnt) ? cnt : 0,
                    IsSelected = effectiveChildCategoryId.HasValue && c.CategoryId == effectiveChildCategoryId.Value
                })
                .ToList(),
            Products = products
        };

        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> Details(string slug, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            return NotFound();
        }

        var now = DateTime.Now;

        var product = await _context.Products
            .AsNoTracking()
            .Include(p => p.Category)
            .Include(p => p.Brand)
            .Include(p => p.Images)
            .Include(p => p.Options)
                .ThenInclude(o => o.Values)
            .Include(p => p.AttributeValues)
                .ThenInclude(av => av.CategoryAttribute)
            .Include(p => p.Variants)
                .ThenInclude(v => v.Images)
            .Include(p => p.Variants)
                .ThenInclude(v => v.OptionValue1)
            .Include(p => p.Variants)
                .ThenInclude(v => v.OptionValue2)
            .Include(p => p.ShopAccount)
                .ThenInclude(a => a!.Profile)
            .FirstOrDefaultAsync(p => p.Status == "Active" && p.Slug == slug, cancellationToken);

        if (product is null)
        {
            return NotFound();
        }

        var primaryCategory = product.Category;

        var orderedImages = (product.Images ?? new List<ProductImage>())
            .OrderByDescending(i => i.IsPrimary)
            .ToList();

        var primaryCategoryId = primaryCategory?.Id;
        var parentCategoryId = primaryCategory?.ParentId;

        IQueryable<Product> relatedBaseQuery = _context.Products
            .AsNoTracking()
            .Where(p => p.Status == "Active" && !p.IsDeleted)
            .Where(p => p.Id != product.Id);

        IQueryable<ProductCardVm> projectCard(IQueryable<Product> q)
        {
            return q
                .OrderByDescending(p => p.AverageRating)
                .ThenByDescending(p => p.ReviewCount)
                .ThenBy(p => p.Name)
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
                    OldPrice = x.OldPrice.HasValue && x.OldPrice.Value > x.Price ? x.OldPrice : null,
                    RatingAverage = x.RatingAverage,
                    RatingCount = x.RatingCount,
                    DiscountPercent = x.OldPrice.HasValue && x.OldPrice.Value > x.Price && x.OldPrice.Value > 0m
                        ? (int)Math.Round((x.OldPrice.Value - x.Price) * 100m / x.OldPrice.Value)
                        : 0,
                    PrimaryImageUrl = x.PrimaryImageUrl
                });
        }

        var relatedProducts = new List<ProductCardVm>(8);
        var selectedIds = new HashSet<int>();

        async Task AppendAsync(IQueryable<Product> q)
        {
            if (relatedProducts.Count >= 8) return;
            var remaining = 8 - relatedProducts.Count;
            var batch = await projectCard(q)
                .Where(x => !selectedIds.Contains(x.ProductId))
                .Take(remaining)
                .ToListAsync(cancellationToken);
            foreach (var it in batch)
            {
                if (selectedIds.Add(it.ProductId)) relatedProducts.Add(it);
            }
        }

        // 1) Same category
        if (primaryCategoryId.HasValue)
        {
            await AppendAsync(relatedBaseQuery.Where(p => p.CategoryId == primaryCategoryId.Value));
        }

        // 2) Parent category (siblings)
        if (relatedProducts.Count < 8 && parentCategoryId.HasValue)
        {
            var siblingCategoryIds = await _context.Categories
                .AsNoTracking()
                .Where(c => c.IsActive && !c.IsDeleted && c.ParentId == parentCategoryId.Value)
                .Select(c => c.Id)
                .ToListAsync(cancellationToken);

            if (siblingCategoryIds.Count > 0)
            {
                await AppendAsync(relatedBaseQuery.Where(p => siblingCategoryIds.Contains(p.CategoryId)));
            }
        }

        // 3) Fallback
        if (relatedProducts.Count < 8)
        {
            await AppendAsync(relatedBaseQuery);
        }

        var sizeGuideEntities = await _context.SizeGuides
            .AsNoTracking()
            .Include(g => g.Details)
            .Where(g => g.ProductId == product.Id)
            .OrderBy(g => g.SortOrder)
            .ToListAsync(cancellationToken);

        var sizeGuides = sizeGuideEntities.Select(g => new ProductSizeGuideVm
        {
            SizeLabel = g.SizeLabel,
            HeightRange = g.HeightRange,
            WeightRange = g.WeightRange,
            ShoulderWidthCm = g.Details.FirstOrDefault(d => d.Name == "ShoulderWidth") is { } dS && decimal.TryParse(dS.Value, out var vS) ? vS : null,
            BustCm = g.Details.FirstOrDefault(d => d.Name == "Bust") is { } dB && decimal.TryParse(dB.Value, out var vB) ? vB : null,
            WaistCm = g.Details.FirstOrDefault(d => d.Name == "Waist") is { } dW && decimal.TryParse(dW.Value, out var vW) ? vW : null,
            HipCm = g.Details.FirstOrDefault(d => d.Name == "Hip") is { } dH && decimal.TryParse(dH.Value, out var vH) ? vH : null,
            LengthCm = g.Details.FirstOrDefault(d => d.Name == "Length") is { } dL && decimal.TryParse(dL.Value, out var vL) ? vL : null,
            WidthCm = g.Details.FirstOrDefault(d => d.Name == "Width") is { } dWd && decimal.TryParse(dWd.Value, out var vWd) ? vWd : null,
            HeightCm = g.Details.FirstOrDefault(d => d.Name == "Height") is { } dHt && decimal.TryParse(dHt.Value, out var vHt) ? vHt : null,
            ShoeLengthCm = g.Details.FirstOrDefault(d => d.Name == "ShoeLength") is { } dSl && decimal.TryParse(dSl.Value, out var vSl) ? vSl : null,
            Note = g.Note
        }).ToList();

        var reviewEntities = await _context.ProductReviews
            .AsNoTracking()
            .Include(r => r.Account)
                .ThenInclude(a => a!.Profile)
            .Where(r => r.ProductId == product.Id && !r.IsHidden)
            .OrderByDescending(r => r.CreatedAt)
            .ThenByDescending(r => r.Id)
            .Take(50)
            .ToListAsync(cancellationToken);

        static List<string> ParseReviewMedia(string? rawJson)
        {
            if (string.IsNullOrWhiteSpace(rawJson)) return new List<string>();
            try
            {
                var parsed = System.Text.Json.JsonSerializer.Deserialize<List<string>>(rawJson);
                return parsed?.Where(u => !string.IsNullOrWhiteSpace(u)).ToList() ?? new List<string>();
            }
            catch
            {
                return new List<string>();
            }
        }

        var reviewVms = reviewEntities
            .Select(r => new ProductReviewVm
            {
                Rating = r.Rating,
                Comment = r.Comment,
                CreatedAt = r.CreatedAt,
                AuthorName = r.Account?.Profile?.FullName
                    ?? r.Account?.Email
                    ?? "Khách hàng",
                AvatarUrl = r.Account?.Profile?.AvatarUrl,
                SellerReply = r.SellerReply,
                RepliedAt = r.RepliedAt,
                ShopName = product.ShopAccount?.Profile?.ShopName ?? "Gian hàng",
                VariantName = r.VariantName,
                MediaUrls = ParseReviewMedia(r.MediaUrlsJson)
            })
            .ToList();

        var defaultVariant = (product.Variants ?? new List<ProductVariant>())
            .Where(v => v.IsActive && !v.IsDeleted)
            .OrderByDescending(v => v.IsDefault)
            .ThenBy(v => v.Id)
            .FirstOrDefault();

        bool IsOnSale(ProductVariant v)
        {
            if (!v.SalePrice.HasValue || v.SalePrice.Value <= 0 || v.SalePrice.Value >= v.Price)
                return false;

            // Đối với sản phẩm có phân loại biến thể, chỉ tính là đang Sale khi có lịch Flash Sale hợp lệ (có ngày bắt đầu & kết thúc)
            bool isMultiTierVariant = v.OptionValue1Id.HasValue || v.OptionValue2Id.HasValue;
            if (isMultiTierVariant && (!v.SaleStartDate.HasValue || !v.SaleEndDate.HasValue))
                return false;

            return (!v.SaleStartDate.HasValue || v.SaleStartDate <= now)
                && (!v.SaleEndDate.HasValue || v.SaleEndDate >= now);
        }

        decimal EffectivePrice(ProductVariant v)
        {
            return IsOnSale(v) ? v.SalePrice!.Value : v.Price;
        }

        var variantVms = (product.Variants ?? new List<ProductVariant>())
            .Where(v => v.IsActive && !v.IsDeleted)
            .OrderByDescending(v => v.IsDefault)
            .ThenBy(v => v.Id)
            .Select(v =>
            {
                var isOnSale = IsOnSale(v);
                int? saleSecondsLeft = null;
                if (isOnSale && v.SaleEndDate.HasValue)
                {
                    var secs = (int)Math.Floor((v.SaleEndDate.Value - now).TotalSeconds);
                    saleSecondsLeft = Math.Max(0, secs);
                }

                return new ProductVariantVm
                {
                    VariantId = v.Id,
                    Sku = v.Sku,
                    ColorId = v.OptionValue1Id,
                    SizeId = v.OptionValue2Id,
                    SizeLabel = v.OptionValue2?.Value,
                    Color = v.OptionValue1?.Value,
                    ColorHex = null,
                    OptionValue1Id = v.OptionValue1Id,
                    OptionValue2Id = v.OptionValue2Id,
                    Option1Value = v.OptionValue1?.Value,
                    Option2Value = v.OptionValue2?.Value,
                    ImageUrl = v.Images.OrderByDescending(i => i.IsPrimary).ThenBy(i => i.SortOrder).Select(i => i.ImageUrl).FirstOrDefault()
                               ?? v.OptionValue1?.ImageUrl
                               ?? v.OptionValue2?.ImageUrl,
                    WeightGram = v.WeightGram > 0 ? v.WeightGram : 300,
                    BasePrice = v.Price,
                    SalePrice = v.SalePrice,
                    SaleStartDate = v.SaleStartDate,
                    SaleEndDate = v.SaleEndDate,
                    IsOnSale = isOnSale,
                    SaleSecondsLeft = saleSecondsLeft,
                    Price = EffectivePrice(v),
                    StockQuantity = Math.Max(0, v.StockQuantity - v.ReservedStock)
                };
            })
            .ToList();

        var allCatDict = await _context.Categories.AsNoTracking().ToDictionaryAsync(c => c.Id, cancellationToken);
        var catPolicy = CommissionHelper.ResolveCategoryPolicy(product.CategoryId, allCatDict);

        var activeOpt1IdSet = variantVms
            .Where(v => v.OptionValue1Id.HasValue)
            .Select(v => v.OptionValue1Id!.Value)
            .ToHashSet();
        var activeOpt2IdSet = variantVms
            .Where(v => v.OptionValue2Id.HasValue)
            .Select(v => v.OptionValue2Id!.Value)
            .ToHashSet();

        var optionGroups = new List<ProductOptionGroupVm>();
        var sortedOpts = (product.Options ?? new List<ProductOption>())
            .OrderBy(o => o.SortOrder)
            .ThenBy(o => o.Id)
            .ToList();

        if (sortedOpts.Count > 0)
        {
            for (int i = 0; i < Math.Min(2, sortedOpts.Count); i++)
            {
                var opt = sortedOpts[i];
                var activeSetForTier = (i == 0) ? activeOpt1IdSet : activeOpt2IdSet;
                var vals = opt.Values
                    .Where(val => activeSetForTier.Contains(val.Id))
                    .OrderBy(val => val.Id)
                    .Select(val => new ProductOptionValueVm
                    {
                        Id = val.Id,
                        Value = val.Value,
                        ImageUrl = val.ImageUrl
                    })
                    .ToList();
                if (vals.Count > 0)
                {
                    optionGroups.Add(new ProductOptionGroupVm
                    {
                        Tier = i + 1,
                        Name = opt.Name,
                        Values = vals
                    });
                }
            }
        }
        else
        {
            // Fallback to legacy Color / Size
            var legacyColors = variantVms
                .Where(v => v.ColorId.HasValue && !string.IsNullOrWhiteSpace(v.Color))
                .GroupBy(v => v.ColorId!.Value)
                .Select(g => new ProductOptionValueVm
                {
                    Id = g.Key,
                    Value = g.First().Color!,
                    ColorHex = g.First().ColorHex
                })
                .ToList();
            if (legacyColors.Count > 0)
            {
                optionGroups.Add(new ProductOptionGroupVm { Tier = 1, Name = "Màu sắc", Values = legacyColors });
            }

            var legacySizes = variantVms
                .Where(v => v.SizeId.HasValue && !string.IsNullOrWhiteSpace(v.SizeLabel))
                .GroupBy(v => v.SizeId!.Value)
                .Select(g => new ProductOptionValueVm
                {
                    Id = g.Key,
                    Value = g.First().SizeLabel!
                })
                .ToList();
            if (legacySizes.Count > 0)
            {
                optionGroups.Add(new ProductOptionGroupVm { Tier = 2, Name = "Kích cỡ", Values = legacySizes });
            }
        }

        var specAttributes = (product.AttributeValues ?? new List<ProductAttributeValue>())
            .Where(av => av.CategoryAttribute != null && !string.IsNullOrWhiteSpace(av.Value))
            .OrderBy(av => av.CategoryAttribute!.SortOrder)
            .ThenBy(av => av.CategoryAttributeId)
            .Select(av => new ProductSpecItemVm
            {
                Name = av.CategoryAttribute!.Name,
                Value = av.Value,
                Unit = av.CategoryAttribute.Unit
            })
            .ToList();

        var activePrices = variantVms.Where(v => v.Price > 0).Select(v => v.Price).ToList();
        decimal minVarPrice = activePrices.Count > 0 ? activePrices.Min() : 0m;
        decimal maxVarPrice = activePrices.Count > 0 ? activePrices.Max() : 0m;

        // Bundle products from same shop for "Mua kèm Deal Sốc / Thường được mua cùng"
        var bundleProducts = await projectCard(
                _context.Products
                    .AsNoTracking()
                    .Where(p => p.Status == "Active" && !p.IsDeleted && p.ShopId == product.ShopId && p.Id != product.Id))
            .Where(x => x.DefaultVariantId > 0 && x.Price > 0)
            .Take(3)
            .ToListAsync(cancellationToken);

        // Applicable Shop & Platform vouchers for this product
        int ResolveRootCat(int catId)
        {
            var cur = catId;
            var guard = 0;
            while (guard++ < 10 && allCatDict.TryGetValue(cur, out var node) && node.ParentId.HasValue && allCatDict.ContainsKey(node.ParentId.Value))
            {
                cur = node.ParentId.Value;
            }
            return cur;
        }
        int prodRootCatId = ResolveRootCat(product.CategoryId);
        bool isProductShopMall = product.ShopAccount?.Profile?.IsMall ?? false;

        var rawActiveVouchers = await _context.Vouchers
            .AsNoTracking()
            .Where(v => v.IsActive && v.StartDate <= now && v.EndDate >= now && (v.UsageLimit == 0 || v.UsedCount < v.UsageLimit))
            .Where(v => v.ShopId == null || v.ShopId == product.ShopId)
            .Where(v => v.TargetAudience != "PrivateChatOnly")
            .OrderByDescending(v => v.ShopId != null)
            .ThenByDescending(v => v.DiscountValue)
            .ToListAsync(cancellationToken);

        static HashSet<int> ParseCsvIds(string? csv)
        {
            var set = new HashSet<int>();
            if (string.IsNullOrWhiteSpace(csv)) return set;
            foreach (var p in csv.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (int.TryParse(p, out var id) && id > 0) set.Add(id);
            }
            return set;
        }

        int viewerAccountId = 0;
        if (User?.Identity?.IsAuthenticated == true)
        {
            var rawViewerId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            int.TryParse(rawViewerId, out viewerAccountId);
        }

        var savedVoucherIdSet = viewerAccountId > 0
            ? (await _context.SavedVouchers
                .AsNoTracking()
                .Where(sv => sv.AccountId == viewerAccountId)
                .Select(sv => sv.VoucherId)
                .ToListAsync(cancellationToken)).ToHashSet()
            : new HashSet<int>();

        var availableVouchers = new List<StorefrontVoucherVm>();
        foreach (var v in rawActiveVouchers)
        {
            var scope = string.IsNullOrWhiteSpace(v.ScopeType) ? "All" : v.ScopeType.Trim();
            bool matches = true;
            string scopeBadge = v.ShopId.HasValue ? "Toàn Shop" : "Toàn Sàn";

            if (string.Equals(scope, "MallOnly", StringComparison.OrdinalIgnoreCase))
            {
                matches = isProductShopMall;
                scopeBadge = "Sellzy Mall";
            }
            else if (string.Equals(scope, "ByCategory", StringComparison.OrdinalIgnoreCase))
            {
                var catIds = ParseCsvIds(v.TargetCategoryIds);
                matches = catIds.Contains(product.CategoryId) || catIds.Contains(prodRootCatId);
                scopeBadge = "Theo ngành hàng";
            }
            else if (string.Equals(scope, "SpecificProducts", StringComparison.OrdinalIgnoreCase))
            {
                var prodIds = ParseCsvIds(v.TargetProductIds);
                matches = prodIds.Contains(product.Id);
                scopeBadge = "Sản phẩm chọn lọc";
            }

            if (!matches) continue;

            availableVouchers.Add(new StorefrontVoucherVm
            {
                Id = v.Id,
                Code = v.Code,
                DiscountType = v.DiscountType,
                DiscountValue = v.DiscountValue,
                MinOrderValue = v.MinOrderValue,
                MaxDiscountValue = v.MaxDiscountValue,
                EndDate = v.EndDate,
                IsShopVoucher = v.ShopId.HasValue,
                SponsorBadge = v.ShopId.HasValue ? "Voucher Shop" : "Voucher Sàn",
                ScopeBadge = scopeBadge,
                IsSaved = savedVoucherIdSet.Contains(v.Id)
            });

            if (availableVouchers.Count >= 6) break;
        }

        // Track & load Recently Viewed Products
        var recentIds = HttpContext.Session.GetJson<List<int>>("recently_viewed_ids") ?? new List<int>();
        var previousRecentIds = recentIds.Where(id => id > 0 && id != product.Id).Distinct().Take(8).ToList();
        var recentlyViewedProducts = new List<ProductCardVm>();
        if (previousRecentIds.Count > 0)
        {
            var rawRecent = await projectCard(
                    _context.Products
                        .AsNoTracking()
                        .Where(p => p.Status == "Active" && !p.IsDeleted && previousRecentIds.Contains(p.Id)))
                .ToListAsync(cancellationToken);

            recentlyViewedProducts = previousRecentIds
                .Select(id => rawRecent.FirstOrDefault(x => x.ProductId == id))
                .Where(x => x != null)
                .Select(x => x!)
                .ToList();
        }

        var updatedRecentIds = new List<int> { product.Id };
        updatedRecentIds.AddRange(recentIds.Where(id => id > 0 && id != product.Id));
        if (updatedRecentIds.Count > 12) updatedRecentIds = updatedRecentIds.Take(12).ToList();
        HttpContext.Session.SetJson("recently_viewed_ids", updatedRecentIds);

        var vm = new ProductDetailsVm
        {
            ProductId = product.Id,
            ShopId = product.ShopId,
            Name = product.Name,
            Slug = product.Slug,
            Price = defaultVariant != null
                ? EffectivePrice(defaultVariant)
                : (product.Variants ?? new List<ProductVariant>()).Where(v => v.IsActive && !v.IsDeleted).OrderBy(EffectivePrice).FirstOrDefault() is { } fallback
                    ? EffectivePrice(fallback)
                    : 0m,
            MinVariantPrice = minVarPrice,
            MaxVariantPrice = maxVarPrice,
            AverageRating = product.AverageRating,
            ReviewCount = product.ReviewCount,
            StockQuantity = defaultVariant == null ? 0 : Math.Max(0, defaultVariant.StockQuantity - defaultVariant.ReservedStock),
            SelectedVariantId = defaultVariant?.Id,
            Variants = variantVms,
            CreatedAt = product.CreatedAt,
            Description = product.Description,
            PrimaryCategoryName = primaryCategory?.Name,
            PrimaryCategorySlug = primaryCategory?.Slug,
            BrandName = product.Brand?.Name,
            BrandSlug = product.Brand?.Slug,
            BrandLogoUrl = product.Brand?.LogoUrl,
            WarrantyMonths = product.WarrantyMonths,
            WarrantyType = product.WarrantyType,
            IsReturnable = product.IsReturnable,
            IsDigital = product.IsDigital,
            ManufacturingDate = product.ManufacturingDate,
            ExpiryDate = product.ExpiryDate,
            ShippingCondition = product.ShippingCondition,
            HasSizeGuide = catPolicy.HasSizeGuide,
            OptionGroups = optionGroups,
            Attributes = specAttributes,
            Images = orderedImages
                .Select(i => new ProductDetailsImageVm
                {
                    Url = i.ImageUrl,
                    Alt = product.Name,
                    IsPrimary = i.IsPrimary,
                    SortOrder = i.SortOrder
                })
                .ToList(),
            SizeGuides = sizeGuides,
            Reviews = reviewVms,
            RelatedProducts = relatedProducts,
            RecentlyViewedProducts = recentlyViewedProducts,
            BundleProducts = bundleProducts,
            AvailableVouchers = availableVouchers,
            SavedVoucherIds = savedVoucherIdSet
        };

        // Shop summary (for seller/shop card)
        var shopProfile = product.ShopAccount?.Profile;
        if (shopProfile is null)
        {
            shopProfile = await _context.UserProfiles
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.AccountId == product.ShopId, cancellationToken);
        }

        if (shopProfile is not null)
        {
            var shopAddress = await _context.UserAddresses
                .AsNoTracking()
                .Where(a => a.AccountId == product.ShopId && a.AddressType == AddressType.Shop)
                .OrderByDescending(a => a.IsDefault)
                .ThenByDescending(a => a.UpdatedAt)
                .ThenByDescending(a => a.Id)
                .FirstOrDefaultAsync(cancellationToken);

            var shopProductQuery = _context.Products
                .AsNoTracking()
                .Where(p => p.Status == "Active" && !p.IsDeleted)
                .Where(p => p.ShopId == product.ShopId);

            var shopProductCount = await shopProductQuery.CountAsync(cancellationToken);
            var shopRatingAvg = await shopProductQuery
                .Select(p => (decimal?)p.AverageRating)
                .AverageAsync(cancellationToken) ?? 0m;

            var location = !string.IsNullOrWhiteSpace(shopAddress?.ProvinceName)
                ? $"{shopAddress!.DistrictName}, {shopAddress.ProvinceName}"
                : shopProfile.Address;

            var shopFollowerCount = await _context.ShopFollowers
                .AsNoTracking()
                .CountAsync(f => f.ShopId == product.ShopId, cancellationToken);

            var isViewerFollowingShop = viewerAccountId > 0 && await _context.ShopFollowers
                .AsNoTracking()
                .AnyAsync(f => f.FollowerAccountId == viewerAccountId && f.ShopId == product.ShopId, cancellationToken);

            vm.Shop = new ShopSummaryVm
            {
                ShopId = product.ShopId,
                Name = shopProfile.ShopName
                    ?? shopProfile.FullName
                    ?? product.ShopAccount?.Email
                    ?? "Shop",
                Slug = shopProfile.ShopSlug,
                LogoUrl = shopProfile.ShopLogoUrl,
                Location = location,
                LastActiveText = null,
                RatingAverage = Math.Round(shopRatingAvg, 1),
                ProductCount = shopProductCount,
                ChatResponseRate = 98,
                IsMall = shopProfile.IsMall,
                ShopTier = shopProfile.IsMall ? "Mall" : "Standard",
                FollowerCount = shopFollowerCount,
                IsFollowing = isViewerFollowingShop
            };
        }

        return View("Product_details", vm);
    }
    [HttpGet("compare")]
    public async Task<IActionResult> Compare(List<int> ids, CancellationToken cancellationToken)
    {
        var compareIds = HttpContext.Session.GetJson<List<int>>("compare_ids") ?? new List<int>();
        
        // If IDs are passed via query, update session
        if (ids != null && ids.Count > 0)
        {
            compareIds = ids.Distinct().ToList();
            HttpContext.Session.SetJson("compare_ids", compareIds);
        }

        if (compareIds.Count == 0)
        {
            return View(new ProductCompareVm());
        }

        var nowLocal = DateTime.Now;

        var productsRaw = await _context.Products
            .AsNoTracking()
            .Where(p => compareIds.Contains(p.Id) && p.Status == "Active")
            .Include(p => p.Category)
            .Include(p => p.Brand)
            .Include(p => p.Images)
            .Include(p => p.AttributeValues).ThenInclude(av => av.CategoryAttribute)
            .Include(p => p.Variants).ThenInclude(v => v.OptionValue1)
            .Include(p => p.Variants).ThenInclude(v => v.OptionValue2)
            .Include(p => p.ShopAccount).ThenInclude(a => a!.Profile)
            .ToListAsync(cancellationToken);

        // Sort products in the same order as compareIds
        var sortedProducts = compareIds
            .Select(id => productsRaw.FirstOrDefault(p => p.Id == id))
            .Where(p => p != null)
            .ToList();

        var vm = new ProductCompareVm
        {
            Products = sortedProducts.Select(p =>
            {
                var variants = p!.Variants.Where(v => v.IsActive && !v.IsDeleted).ToList();
                var minPriceVariant = variants
                    .OrderBy(v => (v.SalePrice.HasValue
                            && (!v.SaleStartDate.HasValue || v.SaleStartDate <= nowLocal)
                            && (!v.SaleEndDate.HasValue || v.SaleEndDate >= nowLocal))
                        ? v.SalePrice.Value
                        : v.Price)
                    .FirstOrDefault();

                var specsDict = new Dictionary<string, string>();
                foreach (var av in (p.AttributeValues ?? new List<ProductAttributeValue>()).Where(x => x.CategoryAttribute != null && !string.IsNullOrWhiteSpace(x.Value)))
                {
                    var unit = string.IsNullOrWhiteSpace(av.CategoryAttribute!.Unit) ? "" : $" {av.CategoryAttribute.Unit}";
                    specsDict[av.CategoryAttribute.Name] = $"{av.Value}{unit}";
                }

                return new ProductCompareItemVm
                {
                    ProductId = p.Id,
                    Name = p.Name,
                    Slug = p.Slug,
                    ImageUrl = p.Images.OrderByDescending(i => i.IsPrimary).Select(i => i.ImageUrl).FirstOrDefault(),
                    Price = minPriceVariant != null 
                        ? ((minPriceVariant.SalePrice.HasValue
                            && (!minPriceVariant.SaleStartDate.HasValue || minPriceVariant.SaleStartDate <= nowLocal)
                            && (!minPriceVariant.SaleEndDate.HasValue || minPriceVariant.SaleEndDate >= nowLocal))
                            ? minPriceVariant.SalePrice.Value
                            : minPriceVariant.Price)
                        : 0m,
                    OldPrice = minPriceVariant?.SalePrice.HasValue == true ? minPriceVariant.Price : null,
                    Rating = p.AverageRating,
                    ReviewCount = p.ReviewCount,
                    Description = p.ShortDescription ?? (p.Description != null && p.Description.Length > 200 ? p.Description.Substring(0, 200) + "..." : p.Description),
                    CategoryName = p.Category?.Name,
                    BrandName = p.Brand?.Name,
                    WarrantyMonths = p.WarrantyMonths,
                    WarrantyType = p.WarrantyType,
                    IsReturnable = p.IsReturnable,
                    Specs = specsDict,
                    ShopName = p.ShopAccount?.Profile?.ShopName ?? p.ShopAccount?.Email ?? "Shop",
                    Colors = variants.Select(v => v.OptionValue1?.Value).Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!).Distinct().ToList(),
                    Sizes = variants.Select(v => v.OptionValue2?.Value).Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!).Distinct().ToList(),
                    InStock = variants.Any(v => (v.StockQuantity - v.ReservedStock) > 0)
                };
            }).ToList()
        };

        return View(vm);
    }

    [HttpPost("compare/add")]
    public IActionResult AddToCompare(int productId)
    {
        var compareIds = HttpContext.Session.GetJson<List<int>>("compare_ids") ?? new List<int>();
        if (!compareIds.Contains(productId))
        {
            // Limit comparison to 4 products for UI stability
            if (compareIds.Count >= 4)
            {
                return Json(new { success = false, message = "Bạn chỉ có thể so sánh tối đa 4 sản phẩm.", count = compareIds.Count });
            }
            compareIds.Add(productId);
            HttpContext.Session.SetJson("compare_ids", compareIds);
            return Json(new { success = true, message = "Đã thêm sản phẩm vào danh sách so sánh.", count = compareIds.Count });
        }
        return Json(new { success = false, message = "Sản phẩm đã có trong danh sách so sánh.", count = compareIds.Count });
    }

    [HttpPost("compare/remove")]
    public IActionResult RemoveFromCompare(int productId)
    {
        var compareIds = HttpContext.Session.GetJson<List<int>>("compare_ids") ?? new List<int>();
        if (compareIds.Remove(productId))
        {
            HttpContext.Session.SetJson("compare_ids", compareIds);
            return Json(new { success = true, message = "Đã xóa sản phẩm khỏi danh sách so sánh.", count = compareIds.Count });
        }
        return Json(new { success = false, message = "Sản phẩm không có trong danh sách so sánh.", count = compareIds.Count });
    }

    [HttpPost("compare/clear")]
    public IActionResult ClearCompare()
    {
        HttpContext.Session.Remove("compare_ids");
        return Json(new { success = true, message = "Đã xóa tất cả sản phẩm trong danh sách so sánh.", count = 0 });
    }

    [HttpGet("compare/count")]
    public IActionResult GetCompareCount()
    {
        var compareIds = HttpContext.Session.GetJson<List<int>>("compare_ids") ?? new List<int>();
        return Json(new { count = compareIds.Count });
    }

    [HttpGet("compare/list")]
    public async Task<IActionResult> GetCompareList()
    {
        var compareIds = HttpContext.Session.GetJson<List<int>>("compare_ids") ?? new List<int>();
        if (compareIds.Count == 0) return Json(new List<object>());

        var products = await _context.Products
            .AsNoTracking()
            .Where(p => compareIds.Contains(p.Id))
            .Include(p => p.Images)
            .Select(p => new
            {
                id = p.Id,
                name = p.Name,
                slug = p.Slug,
                imageUrl = p.Images.OrderByDescending(i => i.IsPrimary).Select(i => i.ImageUrl).FirstOrDefault()
            })
            .ToListAsync();

        // Maintain session order
        var sortedProducts = compareIds
            .Select(id => products.FirstOrDefault(p => p.id == id))
            .Where(p => p != null)
            .ToList();

        return Json(sortedProducts);
    }
}
