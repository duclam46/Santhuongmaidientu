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
    public async Task<IActionResult> Details(string slug, CancellationToken cancellationToken)
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

        var products = await shopProductQuery
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
            Products = products
        };

        return View(vm);
    }
}
