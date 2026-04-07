using DOAN_BANHANG.Models;
using DOAN_BANHANG.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DOAN_BANHANG.Components;

public class SuggestedProductsViewComponent : ViewComponent
{
    private readonly DataContext _context;

    public SuggestedProductsViewComponent(DataContext context)
    {
        _context = context;
    }

    public async Task<IViewComponentResult> InvokeAsync(int count = 6, CancellationToken cancellationToken = default)
    {
        var take = count <= 0 ? 6 : Math.Clamp(count, 1, 12);

        var nowUtc = DateTime.UtcNow;

        var raw = await _context.Products
            .AsNoTracking()
            .Where(p => p.Status == "Active")
            .OrderByDescending(p => p.AverageRating)
            .ThenByDescending(p => p.ReviewCount)
            .ThenByDescending(p => p.CreatedAt)
            .Take(take)
            .Select(p => new
            {
                ProductId = p.Id,
                p.Name,
                p.Slug,
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
            .ToListAsync(cancellationToken);

        var items = raw
            .Select(p => new ProductCardVm
            {
                ProductId = p.ProductId,
                Name = p.Name,
                Slug = p.Slug,
                Price = p.Price,
                OldPrice = p.OldPrice.HasValue && p.OldPrice.Value > p.Price ? p.OldPrice : null,
                RatingAverage = p.RatingAverage,
                RatingCount = p.RatingCount,
                DiscountPercent = p.OldPrice.HasValue && p.OldPrice.Value > p.Price && p.OldPrice.Value > 0m
                    ? (int)Math.Round((p.OldPrice.Value - p.Price) * 100m / p.OldPrice.Value)
                    : 0,
                PrimaryImageUrl = p.PrimaryImageUrl
            })
            .ToList();

        return View(items);
    }
}
