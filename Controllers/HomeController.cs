using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using DOAN_BANHANG.Models;
using DOAN_BANHANG.ViewModels;
using Microsoft.EntityFrameworkCore;

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
        var nowUtc = DateTime.UtcNow;

        var topCategories = await _context.Categories
            .AsNoTracking()
            .Where(c => c.IsActive && c.ParentId == null)
            .OrderBy(c => c.Name)
            .Select(c => new CategoryLinkVm
            {
                CategoryId = c.Id,
                Name = c.Name,
                Slug = c.Slug,
                ProductCount = _context.Products.Count(p => p.Status == "Active" && p.CategoryId == c.Id)
            })
            .Take(10)
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
                Price = p.Variants
                    .Where(v => v.IsActive)
                    .Select(v => (decimal?)((v.SalePrice.HasValue
                            && (!v.SaleStartDate.HasValue || v.SaleStartDate <= nowUtc)
                            && (!v.SaleEndDate.HasValue || v.SaleEndDate >= nowUtc))
                        ? v.SalePrice.Value
                        : v.Price))
                    .Min() ?? 0m,
                OldPrice = p.Variants
                    .Where(v => v.IsActive
                        && v.SalePrice.HasValue
                        && (!v.SaleStartDate.HasValue || v.SaleStartDate <= nowUtc)
                        && (!v.SaleEndDate.HasValue || v.SaleEndDate >= nowUtc)
                        && v.SalePrice.Value < v.Price)
                    .Select(v => (decimal?)v.Price)
                    .Min()
            })
            .Take(12)
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
                PrimaryImageUrl = p.PrimaryImageUrl
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
                Price = p.Variants
                    .Where(v => v.IsActive)
                    .Select(v => (decimal?)((v.SalePrice.HasValue
                            && (!v.SaleStartDate.HasValue || v.SaleStartDate <= nowUtc)
                            && (!v.SaleEndDate.HasValue || v.SaleEndDate >= nowUtc))
                        ? v.SalePrice.Value
                        : v.Price))
                    .Min() ?? 0m,
                OldPrice = p.Variants
                    .Where(v => v.IsActive
                        && v.SalePrice.HasValue
                        && (!v.SaleStartDate.HasValue || v.SaleStartDate <= nowUtc)
                        && (!v.SaleEndDate.HasValue || v.SaleEndDate >= nowUtc)
                        && v.SalePrice.Value < v.Price)
                    .Select(v => (decimal?)v.Price)
                    .Min()
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
                PrimaryImageUrl = p.PrimaryImageUrl
            })
            .ToList();

        var vm = new HomeIndexVm
        {
            TopCategories = topCategories,
            LatestProducts = latestProducts,
            FeaturedProducts = featuredProducts
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

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
