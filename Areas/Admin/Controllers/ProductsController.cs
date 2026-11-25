using System.Security.Claims;
using DOAN_BANHANG.Helpers;
using DOAN_BANHANG.Models;
using DOAN_BANHANG.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

namespace DOAN_BANHANG.Areas.Admin.Controllers;

[Area("Admin")]
[Route("Admin/[controller]/[action]")]
[Authorize(Roles = "Admin,Shop")]
public class ProductsController : Controller
{
    private readonly DataContext _db;
    private readonly IWebHostEnvironment _env;

    public ProductsController(DataContext db, IWebHostEnvironment env)
    {
        _db = db;
        _env = env;
    }

    [HttpGet]
    public async Task<IActionResult> ChildCategories(int parentId, CancellationToken cancellationToken)
    {
        if (parentId <= 0) return Json(new object[] { });

        var children = await _db.Categories
            .AsNoTracking()
            .Where(c => c.ParentId == parentId && !c.IsDeleted)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .Select(c => new ProductAdminSelectOptionVm { Value = c.Id, Label = c.Name })
            .ToListAsync(cancellationToken);

        return Json(children);
    }

    [HttpGet]
    public async Task<IActionResult> AllCategories(CancellationToken cancellationToken)
    {
        var all = await _db.Categories
            .AsNoTracking()
            .Where(c => !c.IsDeleted)
            .Select(c => new
            {
                c.Id,
                c.Name,
                c.ParentId,
                c.SortOrder,
                c.HasSizeGuide,
                c.IsReturnableDefault,
                c.CommissionRate,
                c.MaxCommissionAmount
            })
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .ToListAsync(cancellationToken);
        return Json(all);
    }

    /// <summary>
    /// AJAX endpoint: Trả về cấu hình của Danh mục (HasSizeGuide, IsReturnableDefault, Chiết khấu kế thừa, và danh sách CategoryAttributes EAV)
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetCategoryConfig(int categoryId, int? productId, CancellationToken cancellationToken)
    {
        if (categoryId <= 0)
        {
            return Json(new
            {
                hasSizeGuide = false,
                isReturnableDefault = true,
                commissionRate = 10m,
                maxCommissionAmount = (decimal?)null,
                attributes = Array.Empty<object>()
            });
        }

        var allCats = await _db.Categories
            .AsNoTracking()
            .Where(c => !c.IsDeleted)
            .ToListAsync(cancellationToken);

        var catMap = allCats.ToDictionary(c => c.Id, c => c);
        var defaultComm = await CommissionHelper.GetDefaultCommissionRatePercentAsync(_db, cancellationToken);
        var policy = CommissionHelper.ResolveCategoryPolicy(categoryId, catMap, defaultComm);

        // Collect ancestor chain IDs (from root down to selected category) so child categories inherit parent attributes
        var chainIds = new List<int>();
        int? curId = categoryId;
        int guard = 0;
        while (curId.HasValue && catMap.TryGetValue(curId.Value, out var node) && guard++ < 10)
        {
            chainIds.Insert(0, node.Id);
            curId = node.ParentId;
        }

        var attrs = await _db.CategoryAttributes
            .AsNoTracking()
            .Where(a => chainIds.Contains(a.CategoryId))
            .OrderBy(a => a.SortOrder)
            .ThenBy(a => a.Id)
            .ToListAsync(cancellationToken);

        var existingValMap = new Dictionary<int, string>();
        if (productId.HasValue && productId.Value > 0)
        {
            existingValMap = await _db.ProductAttributeValues
                .AsNoTracking()
                .Where(v => v.ProductId == productId.Value)
                .ToDictionaryAsync(v => v.CategoryAttributeId, v => v.Value, cancellationToken);
        }

        var attrDtos = attrs.Select(a => new
        {
            categoryAttributeId = a.Id,
            attributeName = a.Name,
            name = a.Name,
            unit = a.Unit,
            isFilterable = a.IsFilterable,
            value = existingValMap.TryGetValue(a.Id, out var val) ? val : ""
        }).ToList();

        return Json(new
        {
            hasSizeGuide = policy.HasSizeGuide,
            isReturnableDefault = policy.IsReturnableDefault,
            commissionRate = policy.CommissionRatePercent,
            maxCommissionAmount = policy.MaxCommissionAmount,
            inheritedFrom = policy.InheritedFromCategoryName,
            attributes = attrDtos
        });
    }

    private bool TryGetSignedInAccount(out int accountId, out string? role)
    {
        role = null;
        accountId = 0;
        if (!(User?.Identity?.IsAuthenticated ?? false)) return false;
        var idRaw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(idRaw, out accountId) || accountId <= 0) return false;
        role = User.FindFirstValue(ClaimTypes.Role);
        return true;
    }

    private string GenerateSlug(string title)
    {
        var slug = title.ToLower().Trim();
        slug = Regex.Replace(slug, @"[àáạảãâầấậẩẫăằắặẳẵ]", "a");
        slug = Regex.Replace(slug, @"[èéẹẻẽêềếệểễ]", "e");
        slug = Regex.Replace(slug, @"[ìíịỉĩ]", "i");
        slug = Regex.Replace(slug, @"[òóọỏõôồốộổỗơờớợởỡ]", "o");
        slug = Regex.Replace(slug, @"[ùúụủũưừứựửữ]", "u");
        slug = Regex.Replace(slug, @"[ỳýỵỷỹ]", "y");
        slug = Regex.Replace(slug, @"[đ]", "d");
        slug = Regex.Replace(slug, @"[^a-z0-9\s-]", "");
        slug = Regex.Replace(slug, @"[\s-]+", "-").Trim('-');
        return string.IsNullOrWhiteSpace(slug) ? "san-pham" : slug;
    }

    private IQueryable<Product> ScopedProducts(int accountId, string? role)
    {
        var q = _db.Products.Where(p => !p.IsDeleted);
        if (string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase)) return q;
        if (string.Equals(role, "Shop", StringComparison.OrdinalIgnoreCase)) return q.Where(p => p.ShopId == accountId);
        return q.Where(_ => false);
    }

    [HttpGet]
    [Route("/Admin/Products")]
    [Route("Index")]
    public async Task<IActionResult> Index([FromQuery] ProductAdminListQueryVm query, CancellationToken cancellationToken)
    {
        if (!TryGetSignedInAccount(out var accountId, out var role)) return Challenge();

        var baseQuery = ScopedProducts(accountId, role).AsNoTracking();

        // Global KPI summary across all scoped products
        var allProductStats = await baseQuery
            .Select(p => new
            {
                p.Status,
                VariantCount = p.Variants.Count(v => !v.IsDeleted),
                TotalAvailable = p.Variants.Where(v => !v.IsDeleted && v.IsActive).Sum(v => v.StockQuantity > v.ReservedStock ? v.StockQuantity - v.ReservedStock : 0),
                HasLowStock = p.Variants.Any(v => !v.IsDeleted && v.IsActive && (v.StockQuantity - v.ReservedStock) > 0 && (v.StockQuantity - v.ReservedStock) < 10)
            })
            .ToListAsync(cancellationToken);

        var allProductsCount = allProductStats.Count;
        var activeProductsCount = allProductStats.Count(x => string.Equals(x.Status, "Active", StringComparison.OrdinalIgnoreCase));
        var hiddenProductsCount = allProductStats.Count(x => string.Equals(x.Status, "Hidden", StringComparison.OrdinalIgnoreCase));
        var draftProductsCount = allProductStats.Count(x => string.Equals(x.Status, "Draft", StringComparison.OrdinalIgnoreCase));
        var outOfStockProductsCount = allProductStats.Count(x => x.TotalAvailable <= 0);
        var lowStockProductsCount = allProductStats.Count(x => x.TotalAvailable > 0 && (x.HasLowStock || x.TotalAvailable < 10));
        var totalAvailableStockAll = allProductStats.Sum(x => x.TotalAvailable);
        var totalVariantsAll = allProductStats.Sum(x => x.VariantCount);

        var q = baseQuery;

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            q = q.Where(p => p.Name.Contains(term)
                             || p.Id.ToString() == term
                             || p.Variants.Any(v => !v.IsDeleted && v.Sku != null && v.Sku.Contains(term)));
        }

        if (query.CategoryId.HasValue)
        {
            q = q.Where(p => p.CategoryId == query.CategoryId.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            q = q.Where(p => p.Status == query.Status);
        }

        if (!string.IsNullOrWhiteSpace(query.StockFilter))
        {
            if (string.Equals(query.StockFilter, "OutOfStock", StringComparison.OrdinalIgnoreCase))
            {
                q = q.Where(p => p.Variants.Where(v => !v.IsDeleted && v.IsActive).Sum(v => v.StockQuantity > v.ReservedStock ? v.StockQuantity - v.ReservedStock : 0) <= 0);
            }
            else if (string.Equals(query.StockFilter, "LowStock", StringComparison.OrdinalIgnoreCase))
            {
                q = q.Where(p => p.Variants.Where(v => !v.IsDeleted && v.IsActive).Sum(v => v.StockQuantity > v.ReservedStock ? v.StockQuantity - v.ReservedStock : 0) > 0
                                 && (p.Variants.Any(v => !v.IsDeleted && v.IsActive && (v.StockQuantity - v.ReservedStock) > 0 && (v.StockQuantity - v.ReservedStock) < 10)
                                     || p.Variants.Where(v => !v.IsDeleted && v.IsActive).Sum(v => v.StockQuantity > v.ReservedStock ? v.StockQuantity - v.ReservedStock : 0) < 10));
            }
            else if (string.Equals(query.StockFilter, "InStock", StringComparison.OrdinalIgnoreCase))
            {
                q = q.Where(p => p.Variants.Where(v => !v.IsDeleted && v.IsActive).Sum(v => v.StockQuantity > v.ReservedStock ? v.StockQuantity - v.ReservedStock : 0) >= 10);
            }
        }

        var totalItems = await q.CountAsync(cancellationToken);
        var page = query.Page <= 0 ? 1 : query.Page;
        var pageSize = query.PageSize <= 0 ? 10 : query.PageSize;

        IOrderedQueryable<Product> orderedQuery = (query.SortBy ?? "newest").ToLowerInvariant() switch
        {
            "stock_asc" => q.OrderBy(p => p.Variants.Where(v => !v.IsDeleted && v.IsActive).Sum(v => v.StockQuantity > v.ReservedStock ? v.StockQuantity - v.ReservedStock : 0)).ThenByDescending(p => p.CreatedAt),
            "stock_desc" => q.OrderByDescending(p => p.Variants.Where(v => !v.IsDeleted && v.IsActive).Sum(v => v.StockQuantity > v.ReservedStock ? v.StockQuantity - v.ReservedStock : 0)).ThenByDescending(p => p.CreatedAt),
            "price_asc" => q.OrderBy(p => p.Variants.Where(v => !v.IsDeleted && v.IsActive).Select(v => (decimal?)v.Price).Min() ?? 0m).ThenByDescending(p => p.CreatedAt),
            "price_desc" => q.OrderByDescending(p => p.Variants.Where(v => !v.IsDeleted && v.IsActive).Select(v => (decimal?)v.Price).Max() ?? 0m).ThenByDescending(p => p.CreatedAt),
            _ => q.OrderByDescending(p => p.CreatedAt)
        };

        var pageProducts = await orderedQuery
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Include(p => p.Category)
            .Include(p => p.Brand)
            .Include(p => p.Images)
            .Include(p => p.Variants).ThenInclude(v => v.OptionValue1)
            .Include(p => p.Variants).ThenInclude(v => v.OptionValue2)
            .Include(p => p.Variants).ThenInclude(v => v.Images)
            .ToListAsync(cancellationToken);

        var pageVariantIds = pageProducts
            .SelectMany(p => p.Variants.Where(v => !v.IsDeleted))
            .Select(v => v.Id)
            .Distinct()
            .ToList();

        var soldByVariant = pageVariantIds.Count > 0
            ? await _db.OrderItems
                .AsNoTracking()
                .Where(oi => pageVariantIds.Contains(oi.VariantId)
                             && oi.Order != null
                             && oi.Order.OrderStatus != "Cancelled"
                             && oi.Order.OrderStatus != "Rejected"
                             && oi.Order.OrderStatus != "Returned")
                .GroupBy(oi => oi.VariantId)
                .Select(g => new { VariantId = g.Key, Qty = g.Sum(x => x.Quantity) })
                .ToDictionaryAsync(x => x.VariantId, x => x.Qty, cancellationToken)
            : new Dictionary<int, int>();

        var products = pageProducts.Select(p =>
        {
            var primaryImg = p.Images.Where(i => i.IsPrimary).Select(i => i.ImageUrl).FirstOrDefault()
                             ?? p.Images.OrderBy(i => i.SortOrder).Select(i => i.ImageUrl).FirstOrDefault();

            var variantVms = p.Variants
                .Where(v => !v.IsDeleted)
                .OrderByDescending(v => v.IsDefault)
                .ThenBy(v => v.Id)
                .Select(v =>
                {
                    var optParts = new List<string>();
                    var o1 = v.OptionValue1?.Value;
                    var o2 = v.OptionValue2?.Value;
                    if (!string.IsNullOrWhiteSpace(o1)) optParts.Add(o1);
                    if (!string.IsNullOrWhiteSpace(o2)) optParts.Add(o2);
                    var label = optParts.Count > 0 ? string.Join(" / ", optParts) : (string.IsNullOrWhiteSpace(v.Sku) ? "Mặc định" : v.Sku);

                    var vImg = v.Images?.OrderByDescending(i => i.IsPrimary).ThenBy(i => i.SortOrder).Select(i => i.ImageUrl).FirstOrDefault()
                               ?? v.OptionValue1?.ImageUrl
                               ?? primaryImg;

                    return new ProductAdminListVariantVm
                    {
                        Id = v.Id,
                        Sku = v.Sku ?? "",
                        Label = label,
                        Price = v.Price,
                        CostPrice = v.CostPrice,
                        StockQuantity = v.StockQuantity,
                        ReservedStock = v.ReservedStock,
                        SoldQuantity = soldByVariant.TryGetValue(v.Id, out var sq) ? sq : 0,
                        IsActive = v.IsActive,
                        ImageUrl = vImg
                    };
                })
                .ToList();

            var activeVariants = variantVms.Where(v => v.IsActive).ToList();
            var pricePool = activeVariants.Count > 0 ? activeVariants : variantVms;

            return new ProductAdminListItemVm
            {
                Id = p.Id,
                Name = p.Name,
                Slug = p.Slug,
                CategoryName = p.Category?.Name,
                BrandName = p.Brand?.Name,
                Status = p.Status,
                CreatedAt = p.CreatedAt,
                VariantCount = variantVms.Count,
                PrimaryImageUrl = primaryImg,
                MinPrice = pricePool.Count > 0 ? pricePool.Min(v => v.Price) : 0m,
                MaxPrice = pricePool.Count > 0 ? pricePool.Max(v => v.Price) : 0m,
                TotalStock = activeVariants.Sum(v => v.StockQuantity),
                ReservedStock = activeVariants.Sum(v => v.ReservedStock),
                LowStockVariantCount = activeVariants.Count(v => v.AvailableStock > 0 && v.AvailableStock < 10),
                OutOfStockVariantCount = activeVariants.Count(v => v.AvailableStock <= 0),
                SoldQuantity = variantVms.Sum(v => v.SoldQuantity),
                Variants = variantVms
            };
        }).ToList();

        var categories = await _db.Categories
            .AsNoTracking()
            .Where(c => !c.IsDeleted)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .Select(c => new ProductAdminSelectOptionVm { Value = c.Id, Label = c.Name })
            .ToListAsync(cancellationToken);

        var vm = new ProductAdminListVm
        {
            Query = new ProductAdminListQueryVm
            {
                Search = query.Search,
                CategoryId = query.CategoryId,
                Status = query.Status,
                StockFilter = query.StockFilter,
                SortBy = query.SortBy,
                Page = page,
                PageSize = pageSize
            },
            Products = products,
            TotalItems = totalItems,
            AllProductsCount = allProductsCount,
            ActiveProductsCount = activeProductsCount,
            HiddenProductsCount = hiddenProductsCount,
            DraftProductsCount = draftProductsCount,
            LowStockProductsCount = lowStockProductsCount,
            OutOfStockProductsCount = outOfStockProductsCount,
            TotalAvailableStockAll = totalAvailableStockAll,
            TotalVariantsAll = totalVariantsAll,
            CategoryOptions = categories
        };

        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id, int? selectedVariantId = null, CancellationToken cancellationToken = default)
    {
        if (!TryGetSignedInAccount(out var accountId, out var role)) return Challenge();

        var product = await ScopedProducts(accountId, role)
            .AsNoTracking()
            .Include(p => p.Category)
            .Include(p => p.Brand)
            .Include(p => p.Images)
            .Include(p => p.Options)
            .Include(p => p.AttributeValues).ThenInclude(av => av.CategoryAttribute)
            .Include(p => p.Variants).ThenInclude(v => v.Images)
            .Include(p => p.Variants).ThenInclude(v => v.OptionValue1).ThenInclude(ov => ov!.ProductOption)
            .Include(p => p.Variants).ThenInclude(v => v.OptionValue2).ThenInclude(ov => ov!.ProductOption)
            .Include(p => p.SizeGuides).ThenInclude(g => g.Details)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (product == null) return NotFound();

        var activeDbVariants = product.Variants.Where(v => !v.IsDeleted).OrderByDescending(v => v.IsDefault).ThenBy(v => v.Id).ToList();
        var activeVarIds = activeDbVariants.Select(v => v.Id).ToList();
        var soldByVariant = await _db.OrderItems
            .AsNoTracking()
            .Where(oi => activeVarIds.Contains(oi.VariantId)
                && oi.Order != null
                && oi.Order.OrderStatus != "Cancelled"
                && oi.Order.OrderStatus != "Rejected")
            .GroupBy(oi => oi.VariantId)
            .Select(g => new { VariantId = g.Key, Units = g.Sum(x => x.Quantity) })
            .ToDictionaryAsync(x => x.VariantId, x => x.Units, cancellationToken);

        var sortedOpts = product.Options.OrderBy(o => o.SortOrder).ThenBy(o => o.Id).ToList();

        var vm = new ProductAdminDetailsVm
        {
            ProductId = product.Id,
            Name = product.Name,
            Slug = product.Slug,
            CategoryName = product.Category?.Name,
            BrandName = product.Brand?.Name,
            WarrantyMonths = product.WarrantyMonths,
            WarrantyType = product.WarrantyType,
            IsReturnable = product.IsReturnable,
            IsDigital = product.IsDigital,
            DigitalContent = product.DigitalContent,
            ManufacturingDate = product.ManufacturingDate,
            ExpiryDate = product.ExpiryDate,
            ShippingCondition = product.ShippingCondition,
            Status = product.Status ?? "Active",
            Description = product.Description,
            AverageRating = product.AverageRating,
            ReviewCount = product.ReviewCount,
            CreatedAt = product.CreatedAt,
            UpdatedAt = product.UpdatedAt,
            SelectedVariantId = selectedVariantId,
            Option1Name = sortedOpts.ElementAtOrDefault(0)?.Name ?? "Phân loại 1",
            Option2Name = sortedOpts.ElementAtOrDefault(1)?.Name ?? "Phân loại 2",
            TotalStockQuantity = activeDbVariants.Sum(v => v.StockQuantity),
            TotalReservedStock = activeDbVariants.Sum(v => v.ReservedStock),
            TotalSoldQuantity = soldByVariant.Values.Sum(),
            Images = product.Images.Select(i => new ProductAdminDetailsImageVm
            {
                Url = i.ImageUrl,
                Alt = product.Name,
                IsPrimary = i.IsPrimary,
                SortOrder = i.SortOrder
            }).ToList(),
            Attributes = product.AttributeValues
                .Where(av => av.CategoryAttribute != null && !string.IsNullOrWhiteSpace(av.Value))
                .OrderBy(av => av.CategoryAttribute!.SortOrder)
                .Select(av => new ProductAdminAttributeInputVm
                {
                    CategoryAttributeId = av.CategoryAttributeId,
                    AttributeName = av.CategoryAttribute!.Name,
                    Unit = av.CategoryAttribute.Unit,
                    Value = av.Value
                }).ToList(),
            Variants = activeDbVariants.Select(v => new ProductAdminDetailsVariantVm
            {
                VariantId = v.Id,
                Sku = v.Sku,
                ColorId = v.OptionValue1Id,
                ColorName = v.OptionValue1?.Value,
                ColorHex = null,
                SizeId = v.OptionValue2Id,
                SizeName = v.OptionValue2?.Value,
                Option1Value = v.OptionValue1?.Value,
                Option2Value = v.OptionValue2?.Value,
                VariantLabel = v.ShortVariantLabel,
                ImageUrl = v.Images.OrderByDescending(i => i.IsPrimary).ThenBy(i => i.SortOrder).Select(i => i.ImageUrl).FirstOrDefault()
                           ?? v.OptionValue1?.ImageUrl,
                WeightGram = v.WeightGram,
                LengthCm = v.LengthCm,
                WidthCm = v.WidthCm,
                HeightCm = v.HeightCm,
                Price = v.Price,
                CostPrice = v.CostPrice,
                SalePrice = v.SalePrice,
                SaleStartDate = v.SaleStartDate,
                SaleEndDate = v.SaleEndDate,
                StockQuantity = v.StockQuantity,
                ReservedStock = v.ReservedStock,
                SoldQuantity = soldByVariant.TryGetValue(v.Id, out var sq) ? sq : 0,
                IsActive = v.IsActive,
                IsDefault = v.IsDefault
            }).ToList(),
            SizeGuides = product.SizeGuides.OrderBy(g => g.SortOrder).Select(g => new ProductAdminSizeGuideRowVm
            {
                SizeLabel = g.SizeLabel,
                HeightRange = g.HeightRange,
                WeightRange = g.WeightRange,
                ShoulderWidth = g.Details.FirstOrDefault(d => d.Name == "ShoulderWidth")?.Value,
                Bust = g.Details.FirstOrDefault(d => d.Name == "Bust")?.Value,
                Waist = g.Details.FirstOrDefault(d => d.Name == "Waist")?.Value,
                Hip = g.Details.FirstOrDefault(d => d.Name == "Hip")?.Value,
                ClothingLength = g.Details.FirstOrDefault(d => d.Name == "Length")?.Value,
                ClothingWidth = g.Details.FirstOrDefault(d => d.Name == "Width")?.Value,
                ShoeLength = g.Details.FirstOrDefault(d => d.Name == "ShoeLength")?.Value,
                Note = g.Note
            }).ToList(),
            RecentReviews = await _db.ProductReviews
                .AsNoTracking()
                .Where(r => r.ProductId == id)
                .OrderByDescending(r => r.CreatedAt)
                .Take(5)
                .Select(r => new ProductAdminReviewItemVm
                {
                    ReviewId = r.Id,
                    AuthorName = r.Account != null ? (r.Account.Email ?? "Anonymous") : "Anonymous",
                    Rating = r.Rating,
                    Comment = r.Comment,
                    CreatedAt = r.CreatedAt
                }).ToListAsync(cancellationToken)
        };

        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> Create(CancellationToken cancellationToken)
    {
        var vm = new ProductAdminUpsertVm
        {
            IsReturnable = true,
            WarrantyMonths = 0,
            SingleWeightGram = 300,
            SingleLengthCm = 10,
            SingleWidthCm = 10,
            SingleHeightCm = 10
        };
        await PopulateUpsertVmAsync(vm, cancellationToken);
        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        if (!TryGetSignedInAccount(out var accountId, out var role)) return Challenge();

        var product = await ScopedProducts(accountId, role)
            .Include(p => p.Images)
            .Include(p => p.Options).ThenInclude(o => o.Values)
            .Include(p => p.AttributeValues).ThenInclude(av => av.CategoryAttribute)
            .Include(p => p.Variants).ThenInclude(v => v.Images)
            .Include(p => p.Variants).ThenInclude(v => v.OptionValue1).ThenInclude(ov => ov!.ProductOption)
            .Include(p => p.Variants).ThenInclude(v => v.OptionValue2).ThenInclude(ov => ov!.ProductOption)
            .Include(p => p.SizeGuides).ThenInclude(g => g.Details)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (product == null) return NotFound();

        var orderedOptions = product.Options.OrderBy(o => o.SortOrder).ThenBy(o => o.Id).ToList();
        var opt1 = orderedOptions.ElementAtOrDefault(0);
        var opt2 = orderedOptions.ElementAtOrDefault(1);

        var activeVariants = product.Variants.Where(v => !v.IsDeleted).ToList();
        var defaultVar = activeVariants.OrderBy(v => v.Price > 0 ? v.Price : decimal.MaxValue).FirstOrDefault()
                         ?? activeVariants.FirstOrDefault(v => v.IsDefault)
                         ?? activeVariants.FirstOrDefault();

        var hasMultiVariants = activeVariants.Count > 1
            || (activeVariants.Count == 1 && (activeVariants[0].OptionValue1Id != null || activeVariants[0].OptionValue2Id != null));

        var vm = new ProductAdminUpsertVm
        {
            Id = product.Id,
            Name = product.Name,
            Slug = product.Slug,
            CategoryId = product.CategoryId,
            BrandId = product.BrandId,
            WarrantyMonths = product.WarrantyMonths,
            WarrantyType = product.WarrantyType,
            IsReturnable = product.IsReturnable,
            IsDigital = product.IsDigital,
            DigitalContent = product.DigitalContent,
            ManufacturingDate = product.ManufacturingDate,
            ExpiryDate = product.ExpiryDate,
            ShippingCondition = product.ShippingCondition,
            Description = product.Description,
            Status = product.Status,
            Price = defaultVar?.Price ?? 0m,
            SingleCostPrice = defaultVar?.CostPrice ?? 0m,
            SalePrice = defaultVar?.SalePrice,
            SingleStockQuantity = defaultVar?.StockQuantity ?? 0,
            SingleWeightGram = defaultVar?.WeightGram ?? 300,
            SingleLengthCm = defaultVar?.LengthCm ?? 10,
            SingleWidthCm = defaultVar?.WidthCm ?? 10,
            SingleHeightCm = defaultVar?.HeightCm ?? 10,
            Option1Name = opt1?.Name,
            Option2Name = opt2?.Name,
            HasSizeVariants = hasMultiVariants,
            Variants = activeVariants
                .OrderBy(v => v.Id)
                .Select(v =>
                {
                    var varImg = v.Images.OrderByDescending(i => i.IsPrimary).ThenBy(i => i.SortOrder).Select(i => i.ImageUrl).FirstOrDefault()
                                 ?? v.OptionValue1?.ImageUrl;
                    return new ProductAdminVariantRowVm
                    {
                        VariantId = v.Id,
                        Sku = v.Sku,
                        ColorId = v.OptionValue1Id,
                        ColorName = v.OptionValue1?.Value,
                        ColorHex = null,
                        SizeId = v.OptionValue2Id,
                        SizeName = v.OptionValue2?.Value,
                        OptionValue1Id = v.OptionValue1Id,
                        Option1Value = v.OptionValue1?.Value,
                        Option1ImageUrl = varImg,
                        ImageUrl = varImg,
                        OptionValue2Id = v.OptionValue2Id,
                        Option2Value = v.OptionValue2?.Value,
                        VariantLabel = v.ShortVariantLabel,
                        WeightGram = v.WeightGram > 0 ? v.WeightGram : 300,
                        LengthCm = v.LengthCm > 0 ? v.LengthCm : 10,
                        WidthCm = v.WidthCm > 0 ? v.WidthCm : 10,
                        HeightCm = v.HeightCm > 0 ? v.HeightCm : 10,
                        Price = v.Price,
                        CostPrice = v.CostPrice,
                        SalePrice = v.SalePrice,
                        SaleStartDate = v.SaleStartDate,
                        SaleEndDate = v.SaleEndDate,
                        StockQuantity = v.StockQuantity,
                        IsActive = v.IsActive,
                        IsActiveStr = v.IsActive ? "true" : null
                    };
                }).ToList(),
            AttributeValues = product.AttributeValues.Select(av => new ProductAdminAttributeInputVm
            {
                CategoryAttributeId = av.CategoryAttributeId,
                AttributeName = av.CategoryAttribute?.Name ?? "",
                Unit = av.CategoryAttribute?.Unit,
                Value = av.Value
            }).ToList(),
            SizeGuide = product.SizeGuides.OrderBy(g => g.SortOrder).Select(g => new ProductAdminSizeGuideRowVm
            {
                SizeLabel = g.SizeLabel,
                HeightRange = g.HeightRange,
                WeightRange = g.WeightRange,
                ShoulderWidth = g.Details.FirstOrDefault(d => d.Name == "ShoulderWidth")?.Value,
                Bust = g.Details.FirstOrDefault(d => d.Name == "Bust")?.Value,
                Waist = g.Details.FirstOrDefault(d => d.Name == "Waist")?.Value,
                Hip = g.Details.FirstOrDefault(d => d.Name == "Hip")?.Value,
                ClothingLength = g.Details.FirstOrDefault(d => d.Name == "Length")?.Value,
                ClothingWidth = g.Details.FirstOrDefault(d => d.Name == "Width")?.Value,
                ShoeLength = g.Details.FirstOrDefault(d => d.Name == "ShoeLength")?.Value,
                Note = g.Note
            }).ToList(),
            Images = product.Images.OrderBy(i => i.SortOrder).Select(i => new ProductAdminImageRowVm
            {
                ImageId = i.Id,
                ImageUrl = i.ImageUrl,
                IsPrimary = i.IsPrimary,
                SortOrder = i.SortOrder
            }).ToList()
        };

        await PopulateUpsertVmAsync(vm, cancellationToken);
        return View(vm);
    }

    [HttpPost]
    public async Task<IActionResult> UploadVariantImage(Microsoft.AspNetCore.Http.IFormFile? file, int? productId, CancellationToken cancellationToken)
    {
        if (!TryGetSignedInAccount(out _, out _)) return Unauthorized(new { ok = false, message = "Chưa đăng nhập." });
        if (file == null || file.Length == 0)
            return BadRequest(new { ok = false, message = "Vui lòng chọn file ảnh hợp lệ." });

        var ext = Path.GetExtension(file.FileName)?.ToLowerInvariant() ?? ".jpg";
        var allowed = new[] { ".jpg", ".jpeg", ".png", ".webp", ".gif" };
        if (!allowed.Contains(ext))
            return BadRequest(new { ok = false, message = "Chỉ hỗ trợ định dạng ảnh (.jpg, .png, .webp, .gif)." });

        string folderName = productId.HasValue && productId.Value > 0 ? productId.Value.ToString() : "variants";
        string uploadDir = Path.Combine(_env.WebRootPath, "uploads", "products", folderName);
        if (!Directory.Exists(uploadDir)) Directory.CreateDirectory(uploadDir);

        string fileName = $"var_{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid().ToString("N")[..8]}{ext}";
        string filePath = Path.Combine(uploadDir, fileName);
        await using (var stream = new FileStream(filePath, FileMode.Create))
        {
            await file.CopyToAsync(stream, cancellationToken);
        }

        string url = $"/uploads/products/{folderName}/{fileName}";
        return Json(new { ok = true, url });
    }

    [HttpPost]
    public async Task<IActionResult> Create(ProductAdminUpsertVm vm, CancellationToken cancellationToken)
    {
        if (!TryGetSignedInAccount(out var accountId, out var role)) return Challenge();

        if (vm.HasSizeVariants && vm.Variants != null && vm.Variants.Count > 0)
        {
            var validPrices = vm.Variants.Where(x => x.Price > 0).Select(x => x.Price).ToList();
            if (validPrices.Count > 0 && vm.Price <= 0)
            {
                vm.Price = validPrices.Min();
                ModelState.Remove(nameof(vm.Price));
            }
            var validCosts = vm.Variants.Where(x => x.CostPrice > 0).Select(x => x.CostPrice).ToList();
            if (validCosts.Count > 0 && vm.SingleCostPrice <= 0)
            {
                vm.SingleCostPrice = validCosts.Min();
            }
        }

        if (!ModelState.IsValid)
        {
            await PopulateUpsertVmAsync(vm, cancellationToken);
            return View(vm);
        }

        var baseSlug = string.IsNullOrWhiteSpace(vm.Slug) ? GenerateSlug(vm.Name) : GenerateSlug(vm.Slug);
        var finalSlug = baseSlug;
        int slugCounter = 1;
        while (await _db.Products.AnyAsync(p => p.Slug == finalSlug, cancellationToken))
        {
            finalSlug = $"{baseSlug}-{slugCounter++}";
        }

        var shopId = string.Equals(role, "Shop", StringComparison.OrdinalIgnoreCase)
            ? accountId
            : (await _db.Accounts.Where(a => a.Role == "Shop").Select(a => (int?)a.Id).FirstOrDefaultAsync(cancellationToken) ?? accountId);

        var product = new Product
        {
            Name = vm.Name.Trim(),
            Slug = finalSlug,
            CategoryId = vm.CategoryId,
            BrandId = vm.BrandId > 0 ? vm.BrandId : null,
            WarrantyMonths = Math.Max(0, vm.WarrantyMonths),
            WarrantyType = string.IsNullOrWhiteSpace(vm.WarrantyType) ? null : vm.WarrantyType.Trim(),
            IsReturnable = vm.IsReturnable,
            IsDigital = vm.IsDigital,
            DigitalContent = string.IsNullOrWhiteSpace(vm.DigitalContent) ? null : vm.DigitalContent.Trim(),
            ManufacturingDate = vm.ManufacturingDate,
            ExpiryDate = vm.ExpiryDate,
            ShippingCondition = string.IsNullOrWhiteSpace(vm.ShippingCondition) ? "Tiêu chuẩn" : vm.ShippingCondition.Trim(),
            Description = vm.Description,
            Status = vm.Status,
            ShopId = shopId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.Products.Add(product);
        await _db.SaveChangesAsync(cancellationToken);

        await SyncImagesAsync(product.Id, vm, cancellationToken);
        await SyncAttributeValuesAsync(product.Id, vm.AttributeValues, cancellationToken);
        await SyncVariantsAsync(product.Id, vm, cancellationToken);
        await SaveSizeGuideAsync(product.Id, vm.SizeGuide, cancellationToken);

        TempData["AdminToast"] = $"Đã đăng sản phẩm \"{product.Name}\" thành công!";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Edit(ProductAdminUpsertVm vm, CancellationToken cancellationToken)
    {
        if (!TryGetSignedInAccount(out var accountId, out var role)) return Challenge();

        if (vm.HasSizeVariants && vm.Variants != null && vm.Variants.Count > 0)
        {
            var validPrices = vm.Variants.Where(x => x.Price > 0).Select(x => x.Price).ToList();
            if (validPrices.Count > 0)
            {
                vm.Price = validPrices.Min();
                ModelState.Remove(nameof(vm.Price));
            }
            var validCosts = vm.Variants.Where(x => x.CostPrice > 0).Select(x => x.CostPrice).ToList();
            if (validCosts.Count > 0)
            {
                vm.SingleCostPrice = validCosts.Min();
            }
        }

        if (!ModelState.IsValid)
        {
            await PopulateUpsertVmAsync(vm, cancellationToken);
            return View(vm);
        }

        var product = await ScopedProducts(accountId, role)
            .FirstOrDefaultAsync(p => p.Id == vm.Id, cancellationToken);

        if (product == null) return NotFound();

        var baseSlug = string.IsNullOrWhiteSpace(vm.Slug) ? GenerateSlug(vm.Name) : GenerateSlug(vm.Slug);
        var finalSlug = baseSlug;
        int slugCounter = 1;
        while (await _db.Products.AnyAsync(p => p.Slug == finalSlug && p.Id != product.Id, cancellationToken))
        {
            finalSlug = $"{baseSlug}-{slugCounter++}";
        }

        product.Name = vm.Name.Trim();
        product.Slug = finalSlug;
        product.CategoryId = vm.CategoryId;
        product.BrandId = vm.BrandId > 0 ? vm.BrandId : null;
        product.WarrantyMonths = Math.Max(0, vm.WarrantyMonths);
        product.WarrantyType = string.IsNullOrWhiteSpace(vm.WarrantyType) ? null : vm.WarrantyType.Trim();
        product.IsReturnable = vm.IsReturnable;
        product.IsDigital = vm.IsDigital;
        product.DigitalContent = string.IsNullOrWhiteSpace(vm.DigitalContent) ? null : vm.DigitalContent.Trim();
        product.ManufacturingDate = vm.ManufacturingDate;
        product.ExpiryDate = vm.ExpiryDate;
        product.ShippingCondition = string.IsNullOrWhiteSpace(vm.ShippingCondition) ? "Tiêu chuẩn" : vm.ShippingCondition.Trim();
        product.Description = vm.Description;
        product.Status = vm.Status;
        product.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);

        await SyncImagesAsync(product.Id, vm, cancellationToken);
        await SyncAttributeValuesAsync(product.Id, vm.AttributeValues, cancellationToken);
        await SyncVariantsAsync(product.Id, vm, cancellationToken);
        await SaveSizeGuideAsync(product.Id, vm.SizeGuide, cancellationToken);

        TempData["AdminToast"] = $"Đã cập nhật sản phẩm \"{product.Name}\" thành công!";
        return RedirectToAction(nameof(Index));
    }

    private async Task SyncAttributeValuesAsync(int productId, List<ProductAdminAttributeInputVm>? inputs, CancellationToken cancellationToken)
    {
        var existing = await _db.ProductAttributeValues
            .Where(v => v.ProductId == productId)
            .ToListAsync(cancellationToken);

        if (existing.Count > 0)
        {
            _db.ProductAttributeValues.RemoveRange(existing);
        }

        if (inputs != null && inputs.Count > 0)
        {
            foreach (var inp in inputs)
            {
                if (inp.CategoryAttributeId <= 0 || string.IsNullOrWhiteSpace(inp.Value))
                    continue;

                _db.ProductAttributeValues.Add(new ProductAttributeValue
                {
                    ProductId = productId,
                    CategoryAttributeId = inp.CategoryAttributeId,
                    Value = inp.Value.Trim()
                });
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task SaveSizeGuideAsync(int productId, List<ProductAdminSizeGuideRowVm> rows, CancellationToken cancellationToken)
    {
        var existing = await _db.Set<SizeGuide>().Where(g => g.ProductId == productId).ToListAsync(cancellationToken);
        _db.Set<SizeGuide>().RemoveRange(existing);

        if (rows == null || rows.Count == 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
            return;
        }

        int sortOrder = 0;
        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.SizeLabel)) continue;

            var guide = new SizeGuide
            {
                ProductId = productId,
                SizeLabel = row.SizeLabel,
                HeightRange = row.HeightRange,
                WeightRange = row.WeightRange,
                Note = row.Note,
                SortOrder = sortOrder++
            };

            _db.Set<SizeGuide>().Add(guide);

            void AddDetail(string name, string? value)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    guide.Details.Add(new SizeGuideDetail
                    {
                        Name = name,
                        Value = value,
                        SortOrder = 0
                    });
                }
            }

            AddDetail("ShoulderWidth", row.ShoulderWidth);
            AddDetail("Bust", row.Bust);
            AddDetail("Waist", row.Waist);
            AddDetail("Hip", row.Hip);
            AddDetail("Length", row.ClothingLength);
            AddDetail("Width", row.ClothingWidth);
            AddDetail("ShoeLength", row.ShoeLength);
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task SyncImagesAsync(int productId, ProductAdminUpsertVm vm, CancellationToken cancellationToken)
    {
        string uploadDir = Path.Combine(_env.WebRootPath, "uploads", "products", productId.ToString());
        if (!Directory.Exists(uploadDir)) Directory.CreateDirectory(uploadDir);

        int sortOrder = 0;

        if (vm.PrimaryImage != null)
        {
            var existingPrimary = await _db.ProductImages
                .Where(i => i.ProductId == productId && i.IsPrimary)
                .ToListAsync(cancellationToken);
            foreach (var ep in existingPrimary) ep.IsPrimary = false;

            string fileName = $"prod_{productId}_primary_{Guid.NewGuid().ToString("N")[..8]}{Path.GetExtension(vm.PrimaryImage.FileName)}";
            string filePath = Path.Combine(uploadDir, fileName);
            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await vm.PrimaryImage.CopyToAsync(stream, cancellationToken);
            }

            var pImg = new ProductImage
            {
                ProductId = productId,
                ImageUrl = $"/uploads/products/{productId}/{fileName}",
                IsPrimary = true,
                SortOrder = -1
            };
            _db.ProductImages.Add(pImg);
        }

        if (vm.ContentImages?.Count > 0)
        {
            foreach (var file in vm.ContentImages)
            {
                if (file == null || file.Length == 0) continue;

                string fileName = $"prod_{productId}_gallery_{Guid.NewGuid().ToString("N")[..8]}{Path.GetExtension(file.FileName)}";
                string filePath = Path.Combine(uploadDir, fileName);
                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await file.CopyToAsync(stream, cancellationToken);
                }

                var img = new ProductImage
                {
                    ProductId = productId,
                    ImageUrl = $"/uploads/products/{productId}/{fileName}",
                    IsPrimary = false,
                    SortOrder = sortOrder++
                };
                _db.ProductImages.Add(img);
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task SyncVariantsAsync(int productId, ProductAdminUpsertVm vm, CancellationToken cancellationToken)
    {
        static bool ResolveIsActive(ProductAdminVariantRowVm v)
        {
            if (!string.IsNullOrWhiteSpace(v.IsActiveStr))
            {
                return string.Equals(v.IsActiveStr, "true", StringComparison.OrdinalIgnoreCase);
            }
            return v.IsActive;
        }

        var existingVariants = await _db.ProductVariants
            .Include(v => v.Images)
            .Where(v => v.ProductId == productId && !v.IsDeleted)
            .ToListAsync(cancellationToken);

        // CASE 1: Single variant product (no dynamic option groups)
        if (!vm.HasSizeVariants && (vm.Variants == null || vm.Variants.Count == 0))
        {
            foreach (var old in existingVariants.Skip(1))
            {
                old.IsDeleted = true;
                old.IsActive = false;
                old.IsDefault = false;
            }

            var primaryVar = existingVariants.FirstOrDefault();
            if (primaryVar != null)
            {
                primaryVar.Price = vm.Price;
                primaryVar.CostPrice = Math.Max(0m, vm.SingleCostPrice);
                primaryVar.SalePrice = vm.SalePrice;
                primaryVar.StockQuantity = Math.Max(0, vm.SingleStockQuantity);
                primaryVar.WeightGram = vm.SingleWeightGram > 0 ? vm.SingleWeightGram : 300;
                primaryVar.LengthCm = vm.SingleLengthCm > 0 ? vm.SingleLengthCm : 10;
                primaryVar.WidthCm = vm.SingleWidthCm > 0 ? vm.SingleWidthCm : 10;
                primaryVar.HeightCm = vm.SingleHeightCm > 0 ? vm.SingleHeightCm : 10;
                primaryVar.OptionValue1Id = null;
                primaryVar.OptionValue2Id = null;
                primaryVar.IsDefault = true;
                primaryVar.IsActive = true;
                primaryVar.UpdatedAt = DateTime.UtcNow;
            }
            else
            {
                var sku = await EnsureUniqueSkuAsync($"P{productId}-DEFAULT", 0, cancellationToken);
                _db.ProductVariants.Add(new ProductVariant
                {
                    ProductId = productId,
                    Sku = sku,
                    Price = vm.Price,
                    CostPrice = Math.Max(0m, vm.SingleCostPrice),
                    SalePrice = vm.SalePrice,
                    StockQuantity = Math.Max(0, vm.SingleStockQuantity),
                    WeightGram = vm.SingleWeightGram > 0 ? vm.SingleWeightGram : 300,
                    LengthCm = vm.SingleLengthCm > 0 ? vm.SingleLengthCm : 10,
                    WidthCm = vm.SingleWidthCm > 0 ? vm.SingleWidthCm : 10,
                    HeightCm = vm.SingleHeightCm > 0 ? vm.SingleHeightCm : 10,
                    IsDefault = true,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
            }

            await _db.SaveChangesAsync(cancellationToken);
            return;
        }

        // CASE 2: Dynamic 1-tier or 2-tier Variants submitted via vm.Variants (Cartesian Product table)
        if (vm.Variants != null && vm.Variants.Count > 0)
        {
            // Upsert ProductOptions & ProductOptionValues
            var existingOptions = await _db.ProductOptions
                .Include(o => o.Values)
                .Where(o => o.ProductId == productId)
                .OrderBy(o => o.SortOrder)
                .ToListAsync(cancellationToken);

            var opt1Name = string.IsNullOrWhiteSpace(vm.Option1Name) ? "Phân loại 1" : vm.Option1Name.Trim();
            var opt2Name = string.IsNullOrWhiteSpace(vm.Option2Name) ? null : vm.Option2Name.Trim();

            var hasAnyOpt1 = vm.Variants.Any(v => !string.IsNullOrWhiteSpace(v.Option1Value) || !string.IsNullOrWhiteSpace(v.ColorName));
            var hasAnyOpt2 = vm.Variants.Any(v => !string.IsNullOrWhiteSpace(v.Option2Value) || !string.IsNullOrWhiteSpace(v.SizeName));

            ProductOption? option1 = null;
            ProductOption? option2 = null;

            if (hasAnyOpt1)
            {
                option1 = existingOptions.ElementAtOrDefault(0);
                if (option1 == null)
                {
                    option1 = new ProductOption { ProductId = productId, Name = opt1Name, SortOrder = 1 };
                    _db.ProductOptions.Add(option1);
                    await _db.SaveChangesAsync(cancellationToken);
                }
                else
                {
                    option1.Name = opt1Name;
                }
            }

            if (hasAnyOpt2)
            {
                if (string.IsNullOrWhiteSpace(opt2Name)) opt2Name = "Phân loại 2";
                option2 = existingOptions.ElementAtOrDefault(1);
                if (option2 == null)
                {
                    option2 = new ProductOption { ProductId = productId, Name = opt2Name, SortOrder = 2 };
                    _db.ProductOptions.Add(option2);
                    await _db.SaveChangesAsync(cancellationToken);
                }
                else
                {
                    option2.Name = opt2Name;
                }
            }

            // Helper to get or create ProductOptionValue
            var opt1ValueMap = option1?.Values.ToDictionary(x => x.Value, x => x, StringComparer.OrdinalIgnoreCase)
                               ?? new Dictionary<string, ProductOptionValue>(StringComparer.OrdinalIgnoreCase);
            var opt2ValueMap = option2?.Values.ToDictionary(x => x.Value, x => x, StringComparer.OrdinalIgnoreCase)
                               ?? new Dictionary<string, ProductOptionValue>(StringComparer.OrdinalIgnoreCase);

            async Task<int?> ResolveOptionValue1IdAsync(string? valText, string? imgUrl)
            {
                if (option1 == null || string.IsNullOrWhiteSpace(valText)) return null;
                var clean = valText.Trim();
                if (!opt1ValueMap.TryGetValue(clean, out var ov))
                {
                    ov = new ProductOptionValue
                    {
                        ProductOptionId = option1.Id,
                        Value = clean,
                        ImageUrl = string.IsNullOrWhiteSpace(imgUrl) ? null : imgUrl.Trim()
                    };
                    _db.ProductOptionValues.Add(ov);
                    await _db.SaveChangesAsync(cancellationToken);
                    opt1ValueMap[clean] = ov;
                }
                else if (!string.IsNullOrWhiteSpace(imgUrl))
                {
                    ov.ImageUrl = imgUrl.Trim();
                }
                return ov.Id;
            }

            async Task<int?> ResolveOptionValue2IdAsync(string? valText)
            {
                if (option2 == null || string.IsNullOrWhiteSpace(valText)) return null;
                var clean = valText.Trim();
                if (!opt2ValueMap.TryGetValue(clean, out var ov))
                {
                    ov = new ProductOptionValue
                    {
                        ProductOptionId = option2.Id,
                        Value = clean
                    };
                    _db.ProductOptionValues.Add(ov);
                    await _db.SaveChangesAsync(cancellationToken);
                    opt2ValueMap[clean] = ov;
                }
                return ov.Id;
            }

            var submittedIds = vm.Variants.Select(v => v.VariantId).Where(id => id > 0).ToHashSet();
            foreach (var e in existingVariants)
            {
                if (!submittedIds.Contains(e.Id))
                {
                    e.IsDeleted = true;
                    e.IsActive = false;
                    e.IsDefault = false;
                }
            }

            var activeOpt1ValIds = new HashSet<int>();
            var activeOpt2ValIds = new HashSet<int>();

            bool isFirst = true;
            int rowIdx = 1;
            foreach (var v in vm.Variants)
            {
                var opt1Text = !string.IsNullOrWhiteSpace(v.Option1Value) ? v.Option1Value : v.ColorName;
                var opt2Text = !string.IsNullOrWhiteSpace(v.Option2Value) ? v.Option2Value : v.SizeName;
                var submittedImgUrl = !string.IsNullOrWhiteSpace(v.Option1ImageUrl) ? v.Option1ImageUrl.Trim()
                                    : (!string.IsNullOrWhiteSpace(v.ImageUrl) ? v.ImageUrl.Trim() : null);

                var optVal1Id = await ResolveOptionValue1IdAsync(opt1Text, submittedImgUrl);
                var optVal2Id = await ResolveOptionValue2IdAsync(opt2Text);
                if (optVal1Id.HasValue) activeOpt1ValIds.Add(optVal1Id.Value);
                if (optVal2Id.HasValue) activeOpt2ValIds.Add(optVal2Id.Value);

                var isActive = ResolveIsActive(v);
                var weightGram = v.WeightGram > 0 ? v.WeightGram : (vm.SingleWeightGram > 0 ? vm.SingleWeightGram : 300);
                var lengthCm = v.LengthCm > 0 ? v.LengthCm : (vm.SingleLengthCm > 0 ? vm.SingleLengthCm : 10);
                var widthCm = v.WidthCm > 0 ? v.WidthCm : (vm.SingleWidthCm > 0 ? vm.SingleWidthCm : 10);
                var heightCm = v.HeightCm > 0 ? v.HeightCm : (vm.SingleHeightCm > 0 ? vm.SingleHeightCm : 10);
                var effPrice = v.Price > 0 ? v.Price : vm.Price;
                var effCostPrice = v.CostPrice > 0 ? v.CostPrice : vm.SingleCostPrice;

                var rawSku = string.IsNullOrWhiteSpace(v.Sku)
                    ? $"P{productId}-{optVal1Id ?? 0}-{optVal2Id ?? rowIdx}"
                    : v.Sku.Trim();

                if (v.VariantId > 0)
                {
                    var e = existingVariants.FirstOrDefault(x => x.Id == v.VariantId);
                    if (e != null)
                    {
                        e.Sku = await EnsureUniqueSkuAsync(rawSku, e.Id, cancellationToken);
                        e.OptionValue1Id = optVal1Id;
                        e.OptionValue2Id = optVal2Id;
                        e.Price = effPrice;
                        e.CostPrice = Math.Max(0m, effCostPrice);
                        // Chỉ giữ giá Sale nếu biến thể đang thực sự nằm trong chương trình Flash Sale có ngày bắt đầu & kết thúc
                        if (e.SaleStartDate == null || e.SaleEndDate == null)
                        {
                            e.SalePrice = null;
                        }
                        e.StockQuantity = Math.Max(0, v.StockQuantity);
                        e.WeightGram = weightGram;
                        e.LengthCm = lengthCm;
                        e.WidthCm = widthCm;
                        e.HeightCm = heightCm;
                        e.IsActive = isActive;
                        e.IsDefault = isFirst;
                        e.UpdatedAt = DateTime.UtcNow;

                        if (!string.IsNullOrWhiteSpace(submittedImgUrl))
                        {
                            var primaryVarImg = e.Images.FirstOrDefault(i => i.IsPrimary) ?? e.Images.FirstOrDefault();
                            if (primaryVarImg != null)
                            {
                                primaryVarImg.ImageUrl = submittedImgUrl;
                                primaryVarImg.IsPrimary = true;
                            }
                            else
                            {
                                e.Images.Add(new VariantImage
                                {
                                    VariantId = e.Id,
                                    ImageUrl = submittedImgUrl,
                                    IsPrimary = true,
                                    SortOrder = 0
                                });
                            }
                        }
                        else if (e.Images.Count > 0)
                        {
                            _db.Set<VariantImage>().RemoveRange(e.Images);
                        }

                        isFirst = false;
                        rowIdx++;
                        continue;
                    }
                }

                var uniqueSku = await EnsureUniqueSkuAsync(rawSku, 0, cancellationToken);
                var newVariant = new ProductVariant
                {
                    ProductId = productId,
                    OptionValue1Id = optVal1Id,
                    OptionValue2Id = optVal2Id,
                    Sku = uniqueSku,
                    Price = effPrice,
                    CostPrice = Math.Max(0m, effCostPrice),
                    SalePrice = null,
                    StockQuantity = Math.Max(0, v.StockQuantity),
                    WeightGram = weightGram,
                    LengthCm = lengthCm,
                    WidthCm = widthCm,
                    HeightCm = heightCm,
                    IsActive = isActive,
                    IsDefault = isFirst,
                    SaleStartDate = null,
                    SaleEndDate = null,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                if (!string.IsNullOrWhiteSpace(submittedImgUrl))
                {
                    newVariant.Images.Add(new VariantImage
                    {
                        ImageUrl = submittedImgUrl,
                        IsPrimary = true,
                        SortOrder = 0
                    });
                }
                _db.ProductVariants.Add(newVariant);
                await _db.SaveChangesAsync(cancellationToken);

                isFirst = false;
                rowIdx++;
            }

            // Gỡ liên kết OptionValue trên các biến thể đã bị xóa mềm để có thể dọn sạch ProductOptionValues cũ không còn sử dụng
            var softDeletedVariants = await _db.ProductVariants
                .Where(v => v.ProductId == productId && v.IsDeleted && (v.OptionValue1Id != null || v.OptionValue2Id != null))
                .ToListAsync(cancellationToken);
            foreach (var sdv in softDeletedVariants)
            {
                sdv.OptionValue1Id = null;
                sdv.OptionValue2Id = null;
                sdv.IsActive = false;
                sdv.IsDefault = false;
            }
            await _db.SaveChangesAsync(cancellationToken);

            if (option1 != null)
            {
                var unusedOpt1Vals = option1.Values.Where(ov => !activeOpt1ValIds.Contains(ov.Id)).ToList();
                if (unusedOpt1Vals.Count > 0)
                {
                    _db.ProductOptionValues.RemoveRange(unusedOpt1Vals);
                }
            }
            if (option2 != null)
            {
                var unusedOpt2Vals = option2.Values.Where(ov => !activeOpt2ValIds.Contains(ov.Id)).ToList();
                if (unusedOpt2Vals.Count > 0)
                {
                    _db.ProductOptionValues.RemoveRange(unusedOpt2Vals);
                }
            }
            else if (existingOptions.Count > 1)
            {
                for (int i = 1; i < existingOptions.Count; i++)
                {
                    if (existingOptions[i].Values.Count > 0)
                        _db.ProductOptionValues.RemoveRange(existingOptions[i].Values);
                    _db.ProductOptions.Remove(existingOptions[i]);
                }
            }

            await _db.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task<string> EnsureUniqueSkuAsync(string baseSku, int excludeVariantId, CancellationToken cancellationToken)
    {
        if (baseSku.Length > 90) baseSku = baseSku[..90];
        var candidate = baseSku;
        int suffix = 1;
        while (await _db.ProductVariants.AnyAsync(v => v.Sku == candidate && v.Id != excludeVariantId, cancellationToken))
        {
            candidate = $"{baseSku}-{suffix++}";
        }
        return candidate;
    }

    private async Task PopulateUpsertVmAsync(ProductAdminUpsertVm vm, CancellationToken cancellationToken)
    {
        vm.ParentCategoryOptions = await _db.Categories
            .AsNoTracking()
            .Where(c => c.ParentId == null && !c.IsDeleted)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .Select(c => new ProductAdminSelectOptionVm { Value = c.Id, Label = c.Name })
            .ToListAsync(cancellationToken);

        vm.BrandOptions = await _db.Brands
            .AsNoTracking()
            .Where(b => b.IsActive)
            .OrderBy(b => b.Name)
            .Select(b => new ProductAdminSelectOptionVm { Value = b.Id, Label = b.Name })
            .ToListAsync(cancellationToken);

        vm.StatusOptions = new List<ProductAdminSelectOptionVm>
        {
            new() { ValueString = "Active", Label = "Đang bán (Active)" },
            new() { ValueString = "Draft", Label = "Nháp (Draft)" },
            new() { ValueString = "Hidden", Label = "Ẩn (Hidden)" }
        };

        vm.ColorOptions = new List<ProductAdminSelectOptionVm>();

        if (vm.CategoryId > 0)
        {
            var cat = await _db.Categories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == vm.CategoryId, cancellationToken);
            if (cat?.ParentId != null)
            {
                vm.ParentCategoryId = cat.ParentId;
                vm.ChildCategoryOptions = await _db.Categories
                    .AsNoTracking()
                    .Where(c => c.ParentId == cat.ParentId && !c.IsDeleted)
                    .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
                    .Select(c => new ProductAdminSelectOptionVm { Value = c.Id, Label = c.Name })
                    .ToListAsync(cancellationToken);
            }
        }

        vm.SizeOptions = new List<ProductAdminSelectOptionVm>();
    }

    [HttpGet]
    public async Task<IActionResult> Inventory([FromQuery] AdminInventoryQueryVm query, CancellationToken cancellationToken)
    {
        if (!TryGetSignedInAccount(out var accountId, out var role)) return Challenge();

        var baseQ = ScopedProducts(accountId, role).AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            baseQ = baseQ.Where(p => p.Name.Contains(query.Search) || p.Id.ToString() == query.Search);
        }

        var productsList = await baseQ
            .OrderByDescending(p => p.CreatedAt)
            .Take(50)
            .Select(p => new AdminInventoryProductPickVm
            {
                ProductId = p.Id,
                Name = p.Name,
                PrimaryImageUrl = p.Images.Where(i => i.IsPrimary).Select(i => i.ImageUrl).FirstOrDefault()
                                  ?? p.Images.OrderBy(i => i.SortOrder).Select(i => i.ImageUrl).FirstOrDefault(),
                VariantCount = p.Variants.Count(v => !v.IsDeleted),
                TotalStockQuantity = p.Variants.Where(v => !v.IsDeleted).Sum(v => v.StockQuantity),
                ExpiryDate = p.ExpiryDate,
                ShippingCondition = p.ShippingCondition,
                IsDigital = p.IsDigital
            })
            .ToListAsync(cancellationToken);

        AdminInventorySelectedProductVm? selected = null;
        if (query.ProductId.HasValue)
        {
            var p = await baseQ
                .Include(p => p.Images)
                .Include(p => p.Variants).ThenInclude(v => v.OptionValue1)
                .Include(p => p.Variants).ThenInclude(v => v.OptionValue2)
                .FirstOrDefaultAsync(x => x.Id == query.ProductId.Value, cancellationToken);

            if (p != null)
            {
                selected = new AdminInventorySelectedProductVm
                {
                    ProductId = p.Id,
                    Name = p.Name,
                    PrimaryImageUrl = p.Images.Where(i => i.IsPrimary).Select(i => i.ImageUrl).FirstOrDefault()
                                      ?? p.Images.OrderBy(i => i.SortOrder).Select(i => i.ImageUrl).FirstOrDefault(),
                    ManufacturingDate = p.ManufacturingDate,
                    ExpiryDate = p.ExpiryDate,
                    ShippingCondition = p.ShippingCondition,
                    IsDigital = p.IsDigital,
                    Variants = p.Variants.Where(v => !v.IsDeleted).Select(v => new AdminInventoryVariantRowVm
                    {
                        VariantId = v.Id,
                        Sku = v.Sku,
                        ColorName = v.OptionValue1?.Value,
                        SizeName = v.OptionValue2?.Value,
                        VariantLabel = v.ShortVariantLabel,
                        WeightGram = v.WeightGram,
                        CostPrice = v.CostPrice,
                        StockQuantity = v.StockQuantity,
                        ReservedStock = v.ReservedStock,
                        AvailableStock = v.StockQuantity - v.ReservedStock,
                        IsActive = v.IsActive
                    }).ToList()
                };
            }
        }

        var vm = new AdminInventoryIndexVm
        {
            Query = query,
            Products = productsList,
            SelectedProduct = selected
        };

        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Inventory(AdminInventoryUpdateVm model, CancellationToken cancellationToken)
    {
        if (!TryGetSignedInAccount(out var accountId, out var role)) return Challenge();

        var product = await ScopedProducts(accountId, role)
            .Include(p => p.Variants)
            .FirstOrDefaultAsync(p => p.Id == model.ProductId, cancellationToken);

        if (product == null) return NotFound();

        foreach (var vUpdate in model.Variants)
        {
            var variant = product.Variants.FirstOrDefault(v => v.Id == vUpdate.VariantId);
            if (variant != null)
            {
                variant.StockQuantity = vUpdate.StockQuantity;
                if (vUpdate.CostPrice.HasValue && vUpdate.CostPrice.Value >= 0) variant.CostPrice = vUpdate.CostPrice.Value;
                variant.UpdatedAt = DateTime.UtcNow;
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
        TempData["AdminToast"] = "Cập nhật kho hàng thành công!";

        return RedirectToAction(nameof(Inventory), new { productId = model.ProductId });
    }

    [HttpGet]
    public async Task<IActionResult> ExportInventoryCsv(CancellationToken cancellationToken)
    {
        if (!TryGetSignedInAccount(out var accountId, out var role)) return Challenge();

        var products = await ScopedProducts(accountId, role)
            .AsNoTracking()
            .Include(p => p.Variants).ThenInclude(v => v.OptionValue1)
            .Include(p => p.Variants).ThenInclude(v => v.OptionValue2)
            .OrderByDescending(p => p.Id)
            .ToListAsync(cancellationToken);

        static string CsvEsc(string? val)
        {
            if (string.IsNullOrEmpty(val)) return "";
            if (val.Contains(',') || val.Contains('"') || val.Contains('\n') || val.Contains('\r'))
                return "\"" + val.Replace("\"", "\"\"") + "\"";
            return val;
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("VariantId,ProductId,ProductName,Sku,VariantLabel,CostPrice,StockQuantity,ReservedStock,AvailableStock");

        foreach (var p in products)
        {
            foreach (var v in p.Variants.Where(x => !x.IsDeleted))
            {
                var label = v.ShortVariantLabel;
                var avail = Math.Max(0, v.StockQuantity - v.ReservedStock);
                sb.AppendLine($"{v.Id},{p.Id},{CsvEsc(p.Name)},{CsvEsc(v.Sku)},{CsvEsc(label)},{v.CostPrice:0},{v.StockQuantity},{v.ReservedStock},{avail}");
            }
        }

        var preamble = System.Text.Encoding.UTF8.GetPreamble();
        var body = System.Text.Encoding.UTF8.GetBytes(sb.ToString());
        var result = new byte[preamble.Length + body.Length];
        Buffer.BlockCopy(preamble, 0, result, 0, preamble.Length);
        Buffer.BlockCopy(body, 0, result, preamble.Length, body.Length);

        return File(result, "text/csv; charset=utf-8", $"sellzy_inventory_{DateTime.Now:yyyyMMdd_HHmm}.csv");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ImportInventoryCsv(IFormFile? csvFile, CancellationToken cancellationToken)
    {
        if (!TryGetSignedInAccount(out var accountId, out var role)) return Challenge();

        if (csvFile == null || csvFile.Length == 0)
        {
            TempData["AdminToast"] = "Vui lòng chọn file CSV để nhập tồn kho.";
            return RedirectToAction(nameof(Inventory));
        }

        var allowedProductIds = await ScopedProducts(accountId, role)
            .Select(p => p.Id)
            .ToListAsync(cancellationToken);
        var allowedSet = allowedProductIds.ToHashSet();

        var variants = await _db.ProductVariants
            .Where(v => !v.IsDeleted && allowedSet.Contains(v.ProductId))
            .ToListAsync(cancellationToken);

        var byId = variants.ToDictionary(v => v.Id);
        var bySku = variants
            .Where(v => !string.IsNullOrWhiteSpace(v.Sku))
            .GroupBy(v => v.Sku!.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        int updatedCount = 0;
        using (var reader = new StreamReader(csvFile.OpenReadStream(), System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
        {
            bool isFirstLine = true;
            while (!reader.EndOfStream)
            {
                var line = await reader.ReadLineAsync(cancellationToken);
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (isFirstLine)
                {
                    isFirstLine = false;
                    if (line.Contains("VariantId", StringComparison.OrdinalIgnoreCase) || line.Contains("StockQuantity", StringComparison.OrdinalIgnoreCase))
                        continue;
                }

                // Parse simple CSV row supporting quoted fields and ',' or ';' delimiter
                char delim = line.Count(c => c == ';') > line.Count(c => c == ',') ? ';' : ',';
                var cols = new List<string>();
                var cur = new System.Text.StringBuilder();
                bool inQuotes = false;
                for (int i = 0; i < line.Length; i++)
                {
                    var ch = line[i];
                    if (ch == '"')
                    {
                        if (inQuotes && i + 1 < line.Length && line[i + 1] == '"') { cur.Append('"'); i++; }
                        else inQuotes = !inQuotes;
                    }
                    else if (ch == delim && !inQuotes)
                    {
                        cols.Add(cur.ToString().Trim());
                        cur.Clear();
                    }
                    else cur.Append(ch);
                }
                cols.Add(cur.ToString().Trim());

                if (cols.Count < 7) continue;

                ProductVariant? target = null;
                if (int.TryParse(cols[0], out var vid) && byId.TryGetValue(vid, out var foundById))
                {
                    target = foundById;
                }
                else if (!string.IsNullOrWhiteSpace(cols[3]) && bySku.TryGetValue(cols[3], out var foundBySku))
                {
                    target = foundBySku;
                }

                if (target == null) continue;

                bool changed = false;
                if (decimal.TryParse(cols[5], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var cost) && cost >= 0)
                {
                    target.CostPrice = cost;
                    changed = true;
                }
                if (int.TryParse(cols[6], out var stock) && stock >= 0)
                {
                    target.StockQuantity = stock;
                    changed = true;
                }

                if (changed)
                {
                    target.UpdatedAt = DateTime.UtcNow;
                    updatedCount++;
                }
            }
        }

        if (updatedCount > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }

        TempData["AdminToast"] = updatedCount > 0
            ? $"Đã nhập file CSV thành công và cập nhật tồn kho / giá vốn cho {updatedCount} phân loại!"
            : "Không tìm thấy dòng phân loại hợp lệ nào để cập nhật từ file CSV.";

        return RedirectToAction(nameof(Inventory));
    }

    [HttpGet]
    public async Task<IActionResult> Reviews(int id, CancellationToken cancellationToken)
    {
        if (!TryGetSignedInAccount(out var accountId, out var role)) return Challenge();

        var product = await ScopedProducts(accountId, role)
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (product == null) return NotFound();

        var reviews = await _db.ProductReviews
            .AsNoTracking()
            .Include(r => r.Account)
            .Where(r => r.ProductId == id)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new ProductAdminReviewItemVm
            {
                ReviewId = r.Id,
                AuthorName = r.Account != null ? (r.Account.Email ?? "Anonymous") : "Anonymous",
                Rating = r.Rating,
                Comment = r.Comment,
                CreatedAt = r.CreatedAt,
                SellerReply = r.SellerReply
            })
            .ToListAsync(cancellationToken);

        var vm = new ProductAdminReviewsVm
        {
            ProductId = product.Id,
            ProductName = product.Name,
            Reviews = reviews
        };

        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> HardDelete(int id, CancellationToken cancellationToken)
    {
        if (!TryGetSignedInAccount(out var accountId, out var role)) return Challenge();
        if (role != "Admin") return Forbid();

        var p = await _db.Products
            .Include(x => x.Images)
            .Include(x => x.Variants)
            .Include(x => x.SizeGuides).ThenInclude(g => g.Details)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (p != null)
        {
            _db.Products.Remove(p);
            await _db.SaveChangesAsync(cancellationToken);
            return Content($"Sản phẩm {id} ({p.Name}) đã được xóa vĩnh viễn khỏi DB.");
        }
        return NotFound();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        if (!TryGetSignedInAccount(out var accountId, out var role)) return Challenge();

        var product = await ScopedProducts(accountId, role)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (product == null) return NotFound();

        product.IsDeleted = true;
        product.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);
        TempData["AdminToast"] = $"Đã xóa sản phẩm '{product.Name}' thành công.";

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteProductImage(int productId, int imageId, CancellationToken cancellationToken)
    {
        if (!TryGetSignedInAccount(out var accountId, out var role)) return Challenge();

        var image = await _db.ProductImages
            .Include(i => i.Product)
            .FirstOrDefaultAsync(i => i.Id == imageId && i.ProductId == productId, cancellationToken);

        if (image == null) return NotFound();

        if (string.Equals(role, "Shop", StringComparison.OrdinalIgnoreCase) && image.Product?.ShopId != accountId)
            return Forbid();

        _db.ProductImages.Remove(image);
        await _db.SaveChangesAsync(cancellationToken);

        TempData["AdminToast"] = "Đã xóa ảnh thành công.";
        return RedirectToAction(nameof(Edit), new { id = productId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetPrimaryProductImage(int productId, int imageId, CancellationToken cancellationToken)
    {
        if (!TryGetSignedInAccount(out var accountId, out var role)) return Challenge();

        var product = await ScopedProducts(accountId, role)
            .Include(p => p.Images)
            .FirstOrDefaultAsync(p => p.Id == productId, cancellationToken);

        if (product == null) return NotFound();

        foreach (var img in product.Images)
        {
            img.IsPrimary = (img.Id == imageId);
            if (img.IsPrimary) img.SortOrder = -1;
            else if (img.SortOrder == -1) img.SortOrder = 0;
        }

        await _db.SaveChangesAsync(cancellationToken);

        TempData["AdminToast"] = "Đã thay đổi ảnh chính thành công.";
        return RedirectToAction(nameof(Edit), new { id = productId });
    }

    [HttpGet]
    public async Task<IActionResult> DeleteVariant(int productId, int variantId, CancellationToken cancellationToken)
    {
        if (!TryGetSignedInAccount(out var accountId, out var role)) return Challenge();

        var variant = await _db.ProductVariants
            .Include(v => v.Product)
            .FirstOrDefaultAsync(v => v.Id == variantId && v.ProductId == productId, cancellationToken);

        if (variant == null) return NotFound();

        if (string.Equals(role, "Shop", StringComparison.OrdinalIgnoreCase) && variant.Product?.ShopId != accountId)
            return Forbid();

        variant.IsDeleted = true;
        await _db.SaveChangesAsync(cancellationToken);

        TempData["AdminToast"] = "Đã xóa biến thể thành công.";
        return RedirectToAction(nameof(Edit), new { id = productId });
    }
}
