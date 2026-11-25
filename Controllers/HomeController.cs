using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using DOAN_BANHANG.Models;
using DOAN_BANHANG.ViewModels;
using Microsoft.EntityFrameworkCore;
using DOAN_BANHANG.Helpers;

namespace DOAN_BANHANG.Controllers;

public class HomeController : Controller
{
    private readonly DataContext _context;

    public HomeController(DataContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var nowLocal = DateTime.Now;

        var topCategories = await _context.Categories
            .AsNoTracking()
            .Where(c => c.IsActive && c.ParentId == null)
            .OrderBy(c => c.SortOrder)
            .Select(c => new CategoryLinkVm
            {
                CategoryId = c.Id,
                Name = c.Name,
                Slug = c.Slug,
                IconUrl = c.IconUrl,
                ProductCount = _context.Products.Count(p => p.Status == "Active" && 
                    (p.CategoryId == c.Id || 
                     (p.Category != null && p.Category.ParentId == c.Id) || 
                     (p.Category != null && p.Category.Parent != null && p.Category.Parent.ParentId == c.Id) || 
                     (p.Category != null && p.Category.Parent != null && p.Category.Parent.Parent != null && p.Category.Parent.Parent.ParentId == c.Id)))
            })
            .Take(12)
            .ToListAsync(cancellationToken);

        var latestRaw = await _context.Products
            .AsNoTracking()
            .Where(p => p.Status == "Active")
            .OrderByDescending(p => p.CreatedAt)
            .ThenByDescending(p => p.Id)
            .Select(p => new
            {
                ProductId = p.Id,
                Name = p.Name,
                Slug = p.Slug,
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
                ImageUrls = p.Images
                    .OrderByDescending(i => i.IsPrimary)
                    .Select(i => i.ImageUrl)
                    .ToList(),
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
                    .Min(),
                Sizes = p.Variants
                    .Where(v => v.IsActive && !v.IsDeleted && v.OptionValue2Id != null && v.OptionValue2 != null)
                    .Select(v => v.OptionValue2!.Value)
                    .Distinct()
                    .ToList(),
                Colors = p.Variants
                    .Where(v => v.IsActive && !v.IsDeleted && v.OptionValue1Id != null && v.OptionValue1 != null)
                    .Select(v => v.OptionValue1!.Value)
                    .Distinct()
                    .ToList()
            })
            .Take(8)
            .ToListAsync(cancellationToken);

        var latestProducts = latestRaw
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
                PrimaryImageUrl = ImageUrlHelper.NormalizeStaticFileUrl(p.PrimaryImageUrl),
                ImageUrls = p.ImageUrls
                    .Select(url => ImageUrlHelper.NormalizeStaticFileUrl(url))
                    .Where(url => !string.IsNullOrEmpty(url))
                    .ToList(),
                Sizes = p.Sizes,
                Colors = p.Colors.Select(c => new ColorVm { Name = c, Code = null }).ToList()
            })
            .ToList();

        // Top Sellers: ordered by review count descending.
        var topSellersRaw = await _context.Products
            .AsNoTracking()
            .Where(p => p.Status == "Active")
            .OrderByDescending(p => p.ReviewCount)
            .ThenByDescending(p => p.AverageRating)
            .Select(p => new
            {
                ProductId = p.Id,
                Name = p.Name,
                Slug = p.Slug,
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
                ImageUrls = p.Images
                    .OrderByDescending(i => i.IsPrimary)
                    .Select(i => i.ImageUrl)
                    .ToList(),
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
                    .Min(),
                Sizes = p.Variants
                    .Where(v => v.IsActive && !v.IsDeleted && v.OptionValue2Id != null && v.OptionValue2 != null)
                    .Select(v => v.OptionValue2!.Value)
                    .Distinct()
                    .ToList(),
                Colors = p.Variants
                    .Where(v => v.IsActive && !v.IsDeleted && v.OptionValue1Id != null && v.OptionValue1 != null)
                    .Select(v => v.OptionValue1!.Value)
                    .Distinct()
                    .ToList()
            })
            .Take(8)
            .ToListAsync(cancellationToken);

        var topSellers = topSellersRaw
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
                PrimaryImageUrl = ImageUrlHelper.NormalizeStaticFileUrl(p.PrimaryImageUrl),
                ImageUrls = p.ImageUrls
                    .Select(url => ImageUrlHelper.NormalizeStaticFileUrl(url))
                    .Where(url => !string.IsNullOrEmpty(url))
                    .ToList(),
                Sizes = p.Sizes,
                Colors = p.Colors.Select(c => new ColorVm { Name = c, Code = null }).ToList()
            })
            .ToList();

        // Featured: highest rating then most reviewed.
        var featuredRaw = await _context.Products
            .AsNoTracking()
            .Where(p => p.Status == "Active")
            .OrderByDescending(p => p.AverageRating)
            .ThenByDescending(p => p.ReviewCount)
            .ThenBy(p => p.Name)
            .Select(p => new
            {
                ProductId = p.Id,
                Name = p.Name,
                Slug = p.Slug,
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
                ImageUrls = p.Images
                    .OrderByDescending(i => i.IsPrimary)
                    .Select(i => i.ImageUrl)
                    .ToList(),
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
                    .Min(),
                Sizes = p.Variants
                    .Where(v => v.IsActive && !v.IsDeleted && v.OptionValue2Id != null && v.OptionValue2 != null)
                    .Select(v => v.OptionValue2!.Value)
                    .Distinct()
                    .ToList(),
                Colors = p.Variants
                    .Where(v => v.IsActive && !v.IsDeleted && v.OptionValue1Id != null && v.OptionValue1 != null)
                    .Select(v => v.OptionValue1!.Value)
                    .Distinct()
                    .ToList()
            })
            .Take(8)
            .ToListAsync(cancellationToken);

        var featuredProducts = featuredRaw
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
                PrimaryImageUrl = ImageUrlHelper.NormalizeStaticFileUrl(p.PrimaryImageUrl),
                ImageUrls = p.ImageUrls
                    .Select(url => ImageUrlHelper.NormalizeStaticFileUrl(url))
                    .Where(url => !string.IsNullOrEmpty(url))
                    .ToList(),
                Sizes = p.Sizes,
                Colors = p.Colors.Select(c => new ColorVm { Name = c, Code = null }).ToList()
            })
            .ToList();

        var dealRaw = await _context.Products
            .AsNoTracking()
            .Where(p => p.Status == "Active")
            .Select(p => new
            {
                ProductId = p.Id,
                Name = p.Name,
                Slug = p.Slug,
                CategoryId = p.CategoryId,
                ParentId = p.Category != null ? p.Category.ParentId : null,
                GrandParentId = p.Category != null && p.Category.Parent != null ? p.Category.Parent.ParentId : null,
                GreatGrandParentId = p.Category != null && p.Category.Parent != null && p.Category.Parent.Parent != null ? p.Category.Parent.Parent.ParentId : null,
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
                    .Min(),
                Colors = p.Variants
                    .Where(v => v.IsActive && !v.IsDeleted && v.OptionValue1Id != null && v.OptionValue1 != null)
                    .Select(v => v.OptionValue1!.Value)
                    .Distinct()
                    .ToList()
            })
            .Where(p => p.OldPrice != null && p.OldPrice > p.Price)
            .ToListAsync(cancellationToken);

        var dealCards = dealRaw.Select(p => new ProductCardVm
        {
            ProductId = p.ProductId,
            Name = p.Name,
            Slug = p.Slug,
            Price = p.Price,
            DefaultVariantId = p.DefaultVariantId,
            OldPrice = p.OldPrice,
            RatingAverage = p.RatingAverage,
            RatingCount = p.RatingCount,
            DiscountPercent = p.OldPrice.HasValue && p.OldPrice.Value > 0m
                ? (int)Math.Round((p.OldPrice.Value - p.Price) * 100m / p.OldPrice.Value)
                : 0,
            PrimaryImageUrl = ImageUrlHelper.NormalizeStaticFileUrl(p.PrimaryImageUrl),
            Colors = p.Colors.Select(c => new ColorVm { Name = c, Code = null }).ToList()
        }).ToList();

        var categoryDeals = new Dictionary<string, List<ProductCardVm>>();
        foreach (var rootCat in topCategories)
        {
            if (string.IsNullOrWhiteSpace(rootCat.Slug)) continue;

            var rootDeals = dealRaw
                .Where(p => p.CategoryId == rootCat.CategoryId || 
                            (p.ParentId.HasValue && p.ParentId.Value == rootCat.CategoryId) || 
                            (p.GrandParentId.HasValue && p.GrandParentId.Value == rootCat.CategoryId) || 
                            (p.GreatGrandParentId.HasValue && p.GreatGrandParentId.Value == rootCat.CategoryId))
                .Select(p => dealCards.First(dc => dc.ProductId == p.ProductId))
                .ToList();

            categoryDeals[rootCat.Slug] = rootDeals;
        }

        var dynamicBanners = await _context.HomeBanners
            .AsNoTracking()
            .Where(b => b.IsActive)
            .OrderBy(b => b.SortOrder)
            .ThenByDescending(b => b.Id)
            .Take(6)
            .ToListAsync(cancellationToken);

        var activeCampaigns = await _context.PlatformCampaigns
            .AsNoTracking()
            .Where(c => c.IsActive && c.EndDate >= DateTime.UtcNow)
            .OrderBy(c => c.StartDate)
            .Take(3)
            .ToListAsync(cancellationToken);

        int viewerId = 0;
        if (User?.Identity?.IsAuthenticated == true)
        {
            var rawId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            int.TryParse(rawId, out viewerId);
        }

        var savedVoucherIds = viewerId > 0
            ? (await _context.SavedVouchers
                .AsNoTracking()
                .Where(s => s.AccountId == viewerId)
                .Select(s => s.VoucherId)
                .ToListAsync(cancellationToken)).ToHashSet()
            : new HashSet<int>();

        var hotRawVouchers = await _context.Vouchers
            .AsNoTracking()
            .Where(v => v.IsActive
                     && v.StartDate <= nowLocal
                     && v.EndDate >= nowLocal
                     && v.TargetAudience != "PrivateChatOnly"
                     && (v.UsageLimit == 0 || v.UsedCount < v.UsageLimit))
            .OrderByDescending(v => v.DiscountType == "FreeShipping")
            .ThenByDescending(v => v.ShopId == null)
            .ThenByDescending(v => v.DiscountValue)
            .Take(4)
            .ToListAsync(cancellationToken);

        var hotVouchers = hotRawVouchers.Select(v => new StorefrontVoucherVm
        {
            Id = v.Id,
            Code = v.Code,
            DiscountType = v.DiscountType,
            DiscountValue = v.DiscountValue,
            MinOrderValue = v.MinOrderValue,
            MaxDiscountValue = v.MaxDiscountValue,
            EndDate = v.EndDate,
            IsShopVoucher = v.ShopId.HasValue,
            SponsorBadge = v.DiscountType == "FreeShipping" ? "Freeship" : (v.ShopId.HasValue ? "Voucher Shop" : "Voucher Sàn"),
            ScopeBadge = v.ScopeType == "MallOnly" ? "Sellzy Mall" : (v.ShopId.HasValue ? "Gian hàng" : "Toàn Sàn"),
            IsSaved = savedVoucherIds.Contains(v.Id)
        }).ToList();

        var vm = new HomeIndexVm
        {
            TopCategories = topCategories,
            LatestProducts = latestProducts,
            FeaturedProducts = featuredProducts,
            TopSellers = topSellers,
            MostWanted = featuredProducts,
            CategoryDeals = categoryDeals,
            DynamicBanners = dynamicBanners,
            ActiveCampaigns = activeCampaigns,
            HotVouchers = hotVouchers
        };

        return View(vm);
    }

    public IActionResult Privacy()
    {
        return View();
    }

    public IActionResult About()
    {
        return View("about");
    }

    public IActionResult Contact()
    {
        return View("contact");
    }

    public async Task<IActionResult> FlashSale(CancellationToken cancellationToken)
    {
        var nowLocal = DateTime.Now;

        // FlashSale is represented by ProductVariant.SalePrice + SaleStartDate/SaleEndDate.
        // Show only currently active sales.
        var raw = await _context.ProductVariants
            .AsNoTracking()
            .Where(v => v.IsActive && !v.IsDeleted)
            .Where(v => v.SalePrice.HasValue
                && v.SaleStartDate.HasValue
                && v.SaleEndDate.HasValue
                && v.SaleStartDate.Value <= nowLocal
                && v.SaleEndDate.Value >= nowLocal
                && v.SalePrice.Value < v.Price)
            .Where(v => v.Product != null && v.Product.Status == "Active" && !v.Product.IsDeleted)
            .Select(v => new
            {
                VariantId = v.Id,
                ProductId = v.ProductId,
                ProductName = v.Product!.Name,
                ProductSlug = v.Product!.Slug,
                CategoryId = v.Product!.CategoryId,
                CategoryName = v.Product!.Category!.Name,
                CategorySlug = v.Product!.Category!.Slug,
                RatingAverage = v.Product!.AverageRating,
                RatingCount = v.Product!.ReviewCount,
                OldPrice = v.Price,
                SalePrice = v.SalePrice!.Value,
                PrimaryImageUrl = v.Product!.Images
                    .OrderByDescending(i => i.IsPrimary)
                    .ThenBy(i => i.SortOrder)
                    .Select(i => i.ImageUrl)
                    .FirstOrDefault(),
            })
            .OrderByDescending(x => x.ProductId)
            .Take(500)
            .ToListAsync(cancellationToken);

        // Choose one variant per product to represent the sale card (lowest sale price).
        var cards = raw
            .GroupBy(x => x.ProductId)
            .Select(g => g.OrderBy(x => x.SalePrice).First())
            .Select(x => new
            {
                x.CategoryId,
                x.CategoryName,
                x.CategorySlug,
                Card = new ProductCardVm
                {
                    ProductId = x.ProductId,
                    DefaultVariantId = x.VariantId,
                    Name = x.ProductName,
                    Slug = x.ProductSlug,
                    Price = x.SalePrice,
                    OldPrice = x.OldPrice,
                    RatingAverage = x.RatingAverage,
                    RatingCount = x.RatingCount,
                    DiscountPercent = x.OldPrice > 0m && x.OldPrice > x.SalePrice
                        ? (int)Math.Round((x.OldPrice - x.SalePrice) * 100m / x.OldPrice)
                        : 0,
                    PrimaryImageUrl = ImageUrlHelper.NormalizeStaticFileUrl(x.PrimaryImageUrl)
                }
            })
            .Where(x => x.Card.DiscountPercent > 0)
            .ToList();

        List<ProductCardVm> UpTo10(IEnumerable<ProductCardVm> items)
            => items.Where(i => i.DiscountPercent is > 0 and <= 10)
                .OrderByDescending(i => i.DiscountPercent)
                .ThenBy(i => i.Price)
                .ToList();

        List<ProductCardVm> Above10(IEnumerable<ProductCardVm> items)
            => items.Where(i => i.DiscountPercent > 10)
                .OrderByDescending(i => i.DiscountPercent)
                .ThenBy(i => i.Price)
                .ToList();

        var tabs = new List<FlashSaleTabVm>();

        var allItems = cards.Select(x => x.Card).ToList();
        tabs.Add(new FlashSaleTabVm
        {
            Key = "all",
            Label = "Tất cả",
            UpTo10 = UpTo10(allItems),
            Above10 = Above10(allItems)
        });

        var categoryGroups = cards
            .GroupBy(x => new { x.CategoryId, x.CategoryName, x.CategorySlug })
            .OrderBy(g => g.Key.CategoryName)
            .ToList();

        foreach (var g in categoryGroups)
        {
            var keyBase = string.IsNullOrWhiteSpace(g.Key.CategorySlug) ? $"cat-{g.Key.CategoryId}" : g.Key.CategorySlug.Trim();
            var key = $"{keyBase}-{g.Key.CategoryId}";

            var catItems = g.Select(x => x.Card).ToList();
            tabs.Add(new FlashSaleTabVm
            {
                Key = key,
                Label = g.Key.CategoryName,
                UpTo10 = UpTo10(catItems),
                Above10 = Above10(catItems)
            });
        }

        var vm = new FlashSalePageVm { Tabs = tabs };
        return View(vm);
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}

