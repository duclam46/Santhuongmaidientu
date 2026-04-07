using DOAN_BANHANG.Models;
using DOAN_BANHANG.Helpers;
using DOAN_BANHANG.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text.Json;

namespace DOAN_BANHANG.Areas.Admin.Controllers;

[Area("Admin")]
[Route("Admin/[controller]/[action]")]
public class ProductsController : Controller
{
    private readonly DataContext _context;
    private readonly IWebHostEnvironment _env;

    public ProductsController(DataContext context, IWebHostEnvironment env)
    {
        _context = context;
        _env = env;
    }

    private bool IsAjaxRequest()
        => string.Equals(Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);

    [HttpGet]
    [ActionName("Index")]
    public async Task<IActionResult> Index(ProductAdminListQueryVm query, CancellationToken cancellationToken)
    {
        var categories = await _context.Categories
            .AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.Name)
            .Select(c => new ProductAdminSelectOptionVm { Value = c.Id, Label = c.Name })
            .ToListAsync(cancellationToken);

        var q = _context.Products.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            q = q.Where(p => p.Name.Contains(term));
        }

        if (query.CategoryId.HasValue && query.CategoryId.Value > 0)
        {
            q = q.Where(p => p.CategoryId == query.CategoryId.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            var status = query.Status.Trim();
            q = q.Where(p => p.Status == status);
        }

        var totalItems = await q.CountAsync(cancellationToken);

        var page = query.Page <= 0 ? 1 : query.Page;
        var pageSize = query.PageSize <= 0 ? 10 : Math.Min(query.PageSize, 50);
        var skip = (page - 1) * pageSize;

        var products = await q
            .OrderByDescending(p => p.Id)
            .Skip(skip)
            .Take(pageSize)
            .Select(p => new ProductAdminListItemVm
            {
                Id = p.Id,
                Name = p.Name,
                CategoryName = p.Category != null ? p.Category.Name : null,
                Status = p.Status,
                CreatedAt = p.CreatedAt,
                VariantCount = p.Variants.Count,
                PrimaryImageUrl = p.Images
                    .OrderByDescending(i => i.IsPrimary)
                    .ThenBy(i => i.SortOrder)
                    .Select(i => i.ImageUrl)
                    .FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        foreach (var p in products)
        {
            p.PrimaryImageUrl = ImageUrlHelper.NormalizeStaticFileUrl(p.PrimaryImageUrl);
        }

        var vm = new ProductAdminListVm
        {
            Query = new ProductAdminListQueryVm
            {
                Search = query.Search,
                CategoryId = query.CategoryId,
                Status = query.Status,
                Page = page,
                PageSize = pageSize
            },
            CategoryOptions = categories,
            Products = products,
            TotalItems = totalItems
        };

        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> Create(CancellationToken cancellationToken)
    {
        var vm = await BuildUpsertVmAsync(null, cancellationToken);
        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id, int? variantId, CancellationToken cancellationToken)
    {
        if (id <= 0) return NotFound();

        var product = await _context.Products
            .AsNoTracking()
            .Include(p => p.Category)
            .Include(p => p.Images)
            .Include(p => p.Variants)
                .ThenInclude(v => v.Color)
            .Include(p => p.Variants)
                .ThenInclude(v => v.Size)
            .Include(p => p.SizeGuides)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (product is null) return NotFound();

        var recentReviews = await _context.ProductReviews
            .AsNoTracking()
            .Where(r => r.ProductId == id)
            .OrderByDescending(r => r.CreatedAt)
            .Take(5)
            .Select(r => new ProductAdminReviewItemVm
            {
                ReviewId = r.Id,
                Rating = r.Rating,
                Comment = r.Comment,
                CreatedAt = r.CreatedAt,
                AuthorName = r.Account != null
                    ? (r.Account.Profile != null && !string.IsNullOrWhiteSpace(r.Account.Profile.FullName)
                        ? r.Account.Profile.FullName
                        : r.Account.Email)
                    : "Khách hàng"
            })
            .ToListAsync(cancellationToken);

        var images = (product.Images ?? new List<ProductImage>())
            .OrderByDescending(i => i.IsPrimary)
            .ThenBy(i => i.SortOrder)
            .ThenBy(i => i.Id)
            .Select(i => new ProductAdminDetailsImageVm
            {
                Url = i.ImageUrl,
                Alt = product.Name,
                IsPrimary = i.IsPrimary,
                SortOrder = i.SortOrder
            })
            .ToList();

        foreach (var img in images)
        {
            img.Url = ImageUrlHelper.NormalizeStaticFileUrl(img.Url) ?? img.Url;
        }

        if (images.Count == 0)
        {
            images.Add(new ProductAdminDetailsImageVm
            {
                Url = "/assets/images/vitamin-c.png",
                Alt = product.Name,
                IsPrimary = true,
                SortOrder = 0
            });
        }

        var variants = (product.Variants ?? new List<ProductVariant>())
            .OrderByDescending(v => v.IsDefault)
            .ThenByDescending(v => v.IsActive)
            .ThenBy(v => v.Id)
            .Select(v => new ProductAdminDetailsVariantVm
            {
                VariantId = v.Id,
                Sku = v.Sku,
                ColorId = v.ColorId,
                ColorName = v.Color != null ? v.Color.Name : null,
                ColorHex = v.Color != null ? v.Color.Code : null,
                SizeId = v.SizeId,
                SizeName = v.Size != null ? v.Size.Name : null,
                Price = v.Price,
                SalePrice = v.SalePrice,
                SaleStartDate = v.SaleStartDate,
                SaleEndDate = v.SaleEndDate,
                StockQuantity = v.StockQuantity,
                IsActive = v.IsActive,
                IsDefault = v.IsDefault
            })
            .ToList();

        var selectedVariantId = variantId.HasValue && variants.Any(v => v.VariantId == variantId.Value)
            ? variantId
            : variants.FirstOrDefault(v => v.IsDefault)?.VariantId;

        selectedVariantId ??= variants.FirstOrDefault()?.VariantId;

        var vm = new ProductAdminDetailsVm
        {
            ProductId = product.Id,
            Name = product.Name,
            Slug = product.Slug,
            Status = product.Status,
            CategoryName = product.Category != null ? product.Category.Name : null,
            CreatedAt = product.CreatedAt,
            UpdatedAt = product.UpdatedAt,
            AverageRating = product.AverageRating,
            ReviewCount = product.ReviewCount,
            TotalStockQuantity = variants.Sum(v => Math.Max(0, v.StockQuantity)),
            Description = product.Description,
            Images = images,
            Variants = variants,
            SelectedVariantId = selectedVariantId,
            SizeGuides = (product.SizeGuides ?? new List<SizeGuide>())
                .OrderBy(sg => sg.SortOrder)
                .ThenBy(sg => sg.Id)
                .Select(sg => new ProductAdminSizeGuideRowVm
                {
                    SizeLabel = sg.SizeLabel,
                    HeightRange = sg.HeightRange,
                    WeightRange = sg.WeightRange,
                    Note = sg.Note
                })
                .ToList(),
            RecentReviews = recentReviews
        };

        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> Inventory(AdminInventoryQueryVm query, CancellationToken cancellationToken)
    {
        var term = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim();

        var productQuery = _context.Products
            .AsNoTracking()
            .Where(p => !p.IsDeleted);

        if (!string.IsNullOrWhiteSpace(term))
        {
            productQuery = productQuery.Where(p => p.Name.Contains(term));
        }

        var products = await productQuery
            .OrderByDescending(p => p.UpdatedAt)
            .ThenByDescending(p => p.Id)
            .Take(50)
            .Select(p => new AdminInventoryProductPickVm
            {
                ProductId = p.Id,
                Name = p.Name,
                PrimaryImageUrl = p.Images
                    .OrderByDescending(i => i.IsPrimary)
                    .ThenBy(i => i.SortOrder)
                    .Select(i => i.ImageUrl)
                    .FirstOrDefault(),
                VariantCount = p.Variants.Count(v => !v.IsDeleted),
                TotalStockQuantity = p.Variants
                    .Where(v => !v.IsDeleted)
                    .Select(v => (int?)v.StockQuantity)
                    .Sum() ?? 0
            })
            .ToListAsync(cancellationToken);

        foreach (var p in products)
        {
            p.PrimaryImageUrl = ImageUrlHelper.NormalizeStaticFileUrl(p.PrimaryImageUrl);
        }

        var selectedProductId = query.ProductId;
        if (selectedProductId.HasValue && selectedProductId.Value <= 0)
        {
            selectedProductId = null;
        }

        AdminInventorySelectedProductVm? selected = null;
        AdminInventoryUpdateVm updateVm = new();

        if (selectedProductId.HasValue)
        {
            var selectedProduct = await _context.Products
                .AsNoTracking()
                .Where(p => p.Id == selectedProductId.Value && !p.IsDeleted)
                .Select(p => new
                {
                    p.Id,
                    p.Name,
                    PrimaryImageUrl = p.Images
                        .OrderByDescending(i => i.IsPrimary)
                        .ThenBy(i => i.SortOrder)
                        .Select(i => i.ImageUrl)
                        .FirstOrDefault(),
                    Variants = p.Variants
                        .Where(v => !v.IsDeleted)
                        .OrderByDescending(v => v.IsDefault)
                        .ThenByDescending(v => v.IsActive)
                        .ThenBy(v => v.Id)
                        .Select(v => new AdminInventoryVariantRowVm
                        {
                            VariantId = v.Id,
                            Sku = v.Sku,
                            ColorName = v.Color != null ? v.Color.Name : null,
                            SizeName = v.Size != null ? v.Size.Name : null,
                            StockQuantity = v.StockQuantity,
                            ReservedStock = v.ReservedStock,
                            AvailableStock = v.StockQuantity - v.ReservedStock,
                            IsActive = v.IsActive
                        })
                        .ToList()
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (selectedProduct != null)
            {
                selected = new AdminInventorySelectedProductVm
                {
                    ProductId = selectedProduct.Id,
                    Name = selectedProduct.Name,
                    PrimaryImageUrl = ImageUrlHelper.NormalizeStaticFileUrl(selectedProduct.PrimaryImageUrl),
                    Variants = selectedProduct.Variants
                };

                updateVm = new AdminInventoryUpdateVm
                {
                    ProductId = selectedProduct.Id,
                    Variants = selectedProduct.Variants
                        .Select(v => new AdminInventoryVariantUpdateRowVm
                        {
                            VariantId = v.VariantId,
                            StockQuantity = Math.Max(0, v.StockQuantity)
                        })
                        .ToList()
                };
            }
        }

        var vm = new AdminInventoryIndexVm
        {
            Query = new AdminInventoryQueryVm
            {
                Search = query.Search,
                ProductId = selected?.ProductId
            },
            Products = products,
            SelectedProduct = selected,
            Update = updateVm
        };

        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Inventory(AdminInventoryUpdateVm model, CancellationToken cancellationToken)
    {
        if (model.ProductId <= 0) return NotFound();

        var rows = (model.Variants ?? new List<AdminInventoryVariantUpdateRowVm>())
            .Where(x => x != null && x.VariantId > 0)
            .GroupBy(x => x.VariantId)
            .Select(g => g.Last())
            .ToList();

        if (rows.Count == 0)
        {
            TempData["AdminToast"] = "Không có biến thể để cập nhật.";
            return RedirectToAction(nameof(Inventory), new { productId = model.ProductId });
        }

        if (rows.Any(r => r.StockQuantity < 0))
        {
            TempData["AdminToast"] = "Số lượng tồn kho không hợp lệ.";
            return RedirectToAction(nameof(Inventory), new { productId = model.ProductId });
        }

        var variantIds = rows.Select(r => r.VariantId).Distinct().ToList();

        var variants = await _context.ProductVariants
            .Include(v => v.Product)
            .Where(v => v.ProductId == model.ProductId && variantIds.Contains(v.Id) && !v.IsDeleted && v.Product != null && !v.Product.IsDeleted)
            .ToListAsync(cancellationToken);

        if (variants.Count == 0) return NotFound();

        var byId = variants.ToDictionary(v => v.Id, v => v);
        var nowUtc = DateTime.UtcNow;
        var note = string.IsNullOrWhiteSpace(model.Note) ? null : model.Note.Trim();

        var anyChanged = false;
        foreach (var r in rows)
        {
            if (!byId.TryGetValue(r.VariantId, out var v)) continue;

            var newQty = Math.Max(0, r.StockQuantity);
            if (newQty == v.StockQuantity) continue;

            var delta = newQty - v.StockQuantity;
            v.StockQuantity = newQty;
            v.UpdatedAt = nowUtc;

            _context.InventoryTransactions.Add(new InventoryTransaction
            {
                VariantId = v.Id,
                QuantityChanged = delta,
                TransactionType = "Adjust",
                ReferenceId = "ADMIN",
                Note = note
            });

            anyChanged = true;
        }

        if (anyChanged)
        {
            await _context.SaveChangesAsync(cancellationToken);
            TempData["AdminToast"] = "Đã cập nhật tồn kho.";
        }
        else
        {
            TempData["AdminToast"] = "Không có thay đổi.";
        }

        return RedirectToAction(nameof(Inventory), new { productId = model.ProductId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ProductAdminUpsertVm model, CancellationToken cancellationToken)
    {
        // Validate category parent/child selection early (before creating anything)
        if (!model.ParentCategoryId.HasValue || model.ParentCategoryId.Value <= 0)
        {
            ModelState.AddModelError(nameof(ProductAdminUpsertVm.ParentCategoryId), "Vui lòng chọn danh mục lớn.");
        }

        // Collect requested stock rows (SKU will be auto-generated: ProductId-Size)
        var requestedSizeStocks = (model.InitialSizeStocks ?? new List<ProductAdminInitialSizeStockVm>())
            .Where(x => x != null && x.SizeId > 0 && x.StockQuantity > 0)
            .Select(x => new ProductAdminInitialSizeStockVm
            {
                SizeId = x.SizeId,
                StockQuantity = x.StockQuantity
            })
            .ToList();

        var scheme = NormalizeScheme(model.SizeScheme);
        var requestedVolumeStocks = (model.VolumeOptionStocks ?? new List<ProductAdminCustomOptionStockVm>())
            .Where(x => x != null && !string.IsNullOrWhiteSpace(x.OptionName) && x.StockQuantity > 0)
            .Select(x => new ProductAdminCustomOptionStockVm
            {
                OptionName = x.OptionName?.Trim(),
                StockQuantity = x.StockQuantity
            })
            .ToList();

        // Colors (optional)
        var requestedColorIds = (model.SelectedColorIds ?? new List<int>())
            .Where(id => id > 0)
            .Distinct()
            .ToList();

        var colorNameById = new Dictionary<int, string>();
        if (requestedColorIds.Count > 0)
        {
            var colors = await _context.Colors
                .AsNoTracking()
                .Where(c => requestedColorIds.Contains(c.Id))
                .Select(c => new { c.Id, c.Name })
                .ToListAsync(cancellationToken);

            colorNameById = colors.ToDictionary(x => x.Id, x => x.Name);

            if (colorNameById.Count != requestedColorIds.Count)
            {
                ModelState.AddModelError(nameof(ProductAdminUpsertVm.SelectedColorIds), "Màu sắc không hợp lệ.");
            }
        }

        if (model.HasSizeVariants)
        {
            if (scheme == "volume")
            {
                if (requestedVolumeStocks.Count == 0)
                {
                    ModelState.AddModelError(nameof(ProductAdminUpsertVm.VolumeOptionStocks), "Vui lòng nhập ít nhất 1 quy cách (VD: 50ml) và số lượng > 0.");
                }
            }
            else
            {
                if (requestedSizeStocks.Count == 0)
                {
                    ModelState.AddModelError(nameof(ProductAdminUpsertVm.InitialSizeStocks), "Vui lòng nhập số lượng cho ít nhất 1 quy cách.");
                }
            }
        }

        if (!ModelState.IsValid)
        {
            model = await BuildUpsertVmAsync(model, cancellationToken);
            return View(model);
        }

        // Validate category relationship
        var selectedCategory = await _context.Categories
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == model.CategoryId && c.IsActive, cancellationToken);

        if (selectedCategory is null)
        {
            ModelState.AddModelError(nameof(ProductAdminUpsertVm.CategoryId), "Danh mục không hợp lệ.");
            model = await BuildUpsertVmAsync(model, cancellationToken);
            return View(model);
        }

        var expectedParentId = selectedCategory.ParentId ?? selectedCategory.Id;
        if (model.ParentCategoryId.Value != expectedParentId)
        {
            ModelState.AddModelError(nameof(ProductAdminUpsertVm.CategoryId), "Danh mục con không thuộc danh mục lớn đã chọn.");
            model = await BuildUpsertVmAsync(model, cancellationToken);
            return View(model);
        }

        var product = new Product
        {
            Name = model.Name.Trim(),
            Slug = string.IsNullOrWhiteSpace(model.Slug) ? Slugify(model.Name) : model.Slug.Trim(),
            Description = model.Description,
            CategoryId = model.CategoryId,
            Status = model.Status,
            ShopId = 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _context.Products.Add(product);
        await _context.SaveChangesAsync(cancellationToken);

        var uploadFolder = EnsureUploadsFolder("products", product.Id.ToString());
        var sortOrder = 0;

        if (model.PrimaryImage != null && model.PrimaryImage.Length > 0)
        {
            var savedUrl = await SaveUploadedImageAsync(model.PrimaryImage, uploadFolder, $"p{product.Id}", cancellationToken);
            if (!string.IsNullOrWhiteSpace(savedUrl))
            {
                sortOrder++;
                _context.ProductImages.Add(new ProductImage
                {
                    ProductId = product.Id,
                    ImageUrl = savedUrl,
                    IsPrimary = true,
                    SortOrder = sortOrder
                });
            }
        }

        if (model.ContentImages != null && model.ContentImages.Count > 0)
        {
            foreach (var file in model.ContentImages.Where(f => f != null && f.Length > 0))
            {
                var savedUrl = await SaveUploadedImageAsync(file, uploadFolder, $"p{product.Id}", cancellationToken);
                if (string.IsNullOrWhiteSpace(savedUrl)) continue;

                sortOrder++;
                _context.ProductImages.Add(new ProductImage
                {
                    ProductId = product.Id,
                    ImageUrl = savedUrl,
                    IsPrimary = false,
                    SortOrder = sortOrder
                });
            }
        }

        if (_context.ChangeTracker.HasChanges())
        {
            // ensure there's always a primary image if any image exists
            var anyPrimary = _context.ChangeTracker.Entries<ProductImage>().Any(e => e.State == EntityState.Added && e.Entity.IsPrimary);
            if (!anyPrimary)
            {
                var firstAdded = _context.ChangeTracker.Entries<ProductImage>()
                    .Where(e => e.State == EntityState.Added)
                    .OrderBy(e => e.Entity.SortOrder)
                    .FirstOrDefault();
                if (firstAdded != null) firstAdded.Entity.IsPrimary = true;
            }

            await _context.SaveChangesAsync(cancellationToken);
        }

        // Create initial variants by size (optional)
        if (!model.HasSizeVariants)
        {
            var colorsToCreate = requestedColorIds.Count > 0
                ? requestedColorIds
                    .OrderBy(id => colorNameById.TryGetValue(id, out var name) ? name : id.ToString())
                    .Select(id => (ColorId: (int?)id, ColorName: colorNameById.TryGetValue(id, out var name) ? name : null))
                    .ToList()
                : new List<(int? ColorId, string? ColorName)> { (null, null) };

            var first = true;
            foreach (var c in colorsToCreate)
            {
                _context.ProductVariants.Add(new ProductVariant
                {
                    ProductId = product.Id,
                    SizeId = null,
                    ColorId = c.ColorId,
                    Sku = BuildVariantSku(product.Id, c.ColorName, null),
                    Price = 0,
                    StockQuantity = Math.Max(0, model.SingleStockQuantity),
                    IsActive = true,
                    IsDefault = first
                });
                first = false;
            }

            await _context.SaveChangesAsync(cancellationToken);
        }
        else if (scheme == "volume" && requestedVolumeStocks.Count > 0)
        {
            // Create/find sizes by name (50ml, 100ml...) then create variants
            var normalized = requestedVolumeStocks
                .Select(x => new { Name = NormalizeOptionName(x.OptionName!), Qty = x.StockQuantity })
                .Where(x => !string.IsNullOrWhiteSpace(x.Name) && x.Qty > 0)
                .ToList();

            var names = normalized.Select(x => x.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            var existingSizes = await _context.Sizes
                .AsNoTracking()
                .Where(s => names.Contains(s.Name))
                .Select(s => new { s.Id, s.Name })
                .ToListAsync(cancellationToken);

            var byName = existingSizes.ToDictionary(x => x.Name, x => x.Id, StringComparer.OrdinalIgnoreCase);

            foreach (var n in names)
            {
                if (byName.ContainsKey(n)) continue;
                var size = new Size { Name = n, SortOrder = 0, CreatedAt = DateTime.UtcNow };
                _context.Sizes.Add(size);
                await _context.SaveChangesAsync(cancellationToken);
                byName[n] = size.Id;
            }

            var stockMap = normalized
                .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.Qty), StringComparer.OrdinalIgnoreCase);

            var ordered = byName.Keys
                .OrderBy(k => GetSizeSortKey(k))
                .ThenBy(k => k)
                .ToList();

            var colorsToCreate = requestedColorIds.Count > 0
                ? requestedColorIds
                    .OrderBy(id => colorNameById.TryGetValue(id, out var name) ? name : id.ToString())
                    .Select(id => (ColorId: (int?)id, ColorName: colorNameById.TryGetValue(id, out var name) ? name : null))
                    .ToList()
                : new List<(int? ColorId, string? ColorName)> { (null, null) };

            var first = true;
            foreach (var opt in ordered)
            {
                if (!stockMap.TryGetValue(opt, out var qty) || qty <= 0) continue;
                var sizeId = byName[opt];

                foreach (var c in colorsToCreate)
                {
                    _context.ProductVariants.Add(new ProductVariant
                    {
                        ProductId = product.Id,
                        SizeId = sizeId,
                        ColorId = c.ColorId,
                        Sku = BuildVariantSku(product.Id, c.ColorName, opt),
                        Price = 0,
                        StockQuantity = qty,
                        IsActive = true,
                        IsDefault = first
                    });
                    first = false;
                }
            }

            await _context.SaveChangesAsync(cancellationToken);
        }
        else if (requestedSizeStocks.Count > 0)
        {
            var sizeIds = requestedSizeStocks.Select(x => x.SizeId).Distinct().ToList();
            var sizes = await _context.Sizes
                .AsNoTracking()
                .Where(s => sizeIds.Contains(s.Id))
                .Select(s => new { s.Id, s.Name })
                .ToListAsync(cancellationToken);

            var stockMap = requestedSizeStocks
                .GroupBy(x => x.SizeId)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.StockQuantity));

            var orderedSizes = sizes
                .OrderBy(s => GetSizeSortKey(s.Name))
                .ThenBy(s => s.Name)
                .ToList();

            if (scheme != "all")
            {
                orderedSizes = orderedSizes
                    .Where(s => ClassifyScheme(s.Name) == scheme)
                    .ToList();
            }

            var colorsToCreate = requestedColorIds.Count > 0
                ? requestedColorIds
                    .OrderBy(id => colorNameById.TryGetValue(id, out var name) ? name : id.ToString())
                    .Select(id => (ColorId: (int?)id, ColorName: colorNameById.TryGetValue(id, out var name) ? name : null))
                    .ToList()
                : new List<(int? ColorId, string? ColorName)> { (null, null) };

            var first = true;
            foreach (var s in orderedSizes)
            {
                if (!stockMap.TryGetValue(s.Id, out var qty) || qty <= 0) continue;

                foreach (var c in colorsToCreate)
                {
                    _context.ProductVariants.Add(new ProductVariant
                    {
                        ProductId = product.Id,
                        SizeId = s.Id,
                        ColorId = c.ColorId,
                        Sku = BuildVariantSku(product.Id, c.ColorName, s.Name),
                        Price = 0,
                        StockQuantity = qty,
                        IsActive = true,
                        IsDefault = first
                    });
                    first = false;
                }
            }

            await _context.SaveChangesAsync(cancellationToken);
        }

        // Save size guide + per-size measurements into details
        static string FmtNum(decimal value)
            => decimal.Round(value, 2).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

        List<SizeGuideDetail> BuildDetails(ProductAdminSizeGuideRowVm row)
        {
            var details = new List<SizeGuideDetail>();

            void Add(string name, decimal? value, string unit, int sort)
            {
                if (!value.HasValue) return;
                if (value.Value <= 0) return;
                details.Add(new SizeGuideDetail
                {
                    Name = name,
                    Value = FmtNum(value.Value),
                    Unit = unit,
                    SortOrder = sort
                });
            }

            // Measurements follow scheme
            if (scheme == "clothing")
            {
                Add("Dài", row.ClothingLengthCm, "cm", 0);
                Add("Rộng", row.ClothingWidthCm, "cm", 1);
                Add("Cao", row.ClothingHeightCm, "cm", 2);
            }
            else if (scheme == "shoe")
            {
                Add("Chiều dài", row.ShoeLengthCm, "cm", 0);
            }

            return details;
        }

        var guideRows = (model.SizeGuide ?? new List<ProductAdminSizeGuideRowVm>())
            .Where(r => r != null)
            .Select(r => new ProductAdminSizeGuideRowVm
            {
                SizeLabel = string.IsNullOrWhiteSpace(r.SizeLabel) ? null : r.SizeLabel.Trim(),
                HeightRange = string.IsNullOrWhiteSpace(r.HeightRange) ? null : r.HeightRange.Trim(),
                WeightRange = string.IsNullOrWhiteSpace(r.WeightRange) ? null : r.WeightRange.Trim(),
                ClothingLengthCm = r.ClothingLengthCm,
                ClothingWidthCm = r.ClothingWidthCm,
                ClothingHeightCm = r.ClothingHeightCm,
                ShoeLengthCm = r.ShoeLengthCm,
                Note = string.IsNullOrWhiteSpace(r.Note) ? null : r.Note.Trim()
            })
            .Where(r => !string.IsNullOrWhiteSpace(r.SizeLabel)
                || !string.IsNullOrWhiteSpace(r.HeightRange)
                || !string.IsNullOrWhiteSpace(r.WeightRange)
                || !string.IsNullOrWhiteSpace(r.Note)
                || r.ClothingLengthCm.HasValue
                || r.ClothingWidthCm.HasValue
                || r.ClothingHeightCm.HasValue
                || r.ShoeLengthCm.HasValue)
            .ToList();

        if (guideRows.Count > 0)
        {
            var order = 0;
            foreach (var r in guideRows)
            {
                _context.SizeGuides.Add(new SizeGuide
                {
                    ProductId = product.Id,
                    SizeLabel = r.SizeLabel,
                    HeightRange = r.HeightRange,
                    WeightRange = r.WeightRange,
                    Note = r.Note,
                    SortOrder = order++,
                    Details = BuildDetails(r)
                });
            }
        }

        if (_context.ChangeTracker.HasChanges())
        {
            await _context.SaveChangesAsync(cancellationToken);
        }

        TempData["AdminToast"] = "Đã tạo sản phẩm.";
        return RedirectToAction(nameof(Edit), new { id = product.Id });
    }

    private static string NormalizeSkuPart(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "NA";
        var s = input.Trim().ToUpperInvariant();
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\s+", "-");
        s = System.Text.RegularExpressions.Regex.Replace(s, @"[^A-Z0-9\-]", "");
        s = System.Text.RegularExpressions.Regex.Replace(s, "-+", "-");
        return s.Trim('-');
    }

    private static string BuildVariantSku(int productId, string? colorName, string? sizeName)
    {
        var parts = new List<string> { productId.ToString() };

        if (!string.IsNullOrWhiteSpace(colorName))
        {
            parts.Add(NormalizeSkuPart(colorName));
        }

        if (!string.IsNullOrWhiteSpace(sizeName))
        {
            parts.Add(NormalizeSkuPart(sizeName));
        }

        return string.Join("-", parts);
    }

    private static string NormalizeScheme(string? scheme)
    {
        var s = (scheme ?? string.Empty).Trim().ToLowerInvariant();
        return s switch
        {
            "shoe" => "shoe",
            "clothing" => "clothing",
            "volume" => "volume",
            "weight" => "weight",
            "all" => "all",
            _ => "clothing"
        };
    }

    private static string ClassifyScheme(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "other";
        var s = name.Trim().ToUpperInvariant();

        if (System.Text.RegularExpressions.Regex.IsMatch(s, @"(\d+(?:\.\d+)?)\s*ML\b") || System.Text.RegularExpressions.Regex.IsMatch(s, @"\bML\b"))
            return "volume";
        if (System.Text.RegularExpressions.Regex.IsMatch(s, @"(\d+(?:\.\d+)?)\s*L\b"))
            return "volume";

        if (System.Text.RegularExpressions.Regex.IsMatch(s, @"(\d+(?:\.\d+)?)\s*G\b") || System.Text.RegularExpressions.Regex.IsMatch(s, @"(\d+(?:\.\d+)?)\s*KG\b"))
            return "weight";

        if (System.Text.RegularExpressions.Regex.IsMatch(s, @"^\d+(?:\.\d+)?$") || System.Text.RegularExpressions.Regex.IsMatch(s, @"\bEU\s*\d+"))
            return "shoe";

        if (System.Text.RegularExpressions.Regex.IsMatch(s, @"\b(XXS|XS|S|M|L|XL|XXL|XXXL)\b"))
            return "clothing";

        return "other";
    }

    private static string NormalizeOptionName(string input)
    {
        var s = (input ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(s)) return string.Empty;

        // Normalize common volume inputs: "50" -> "50ml", "50 ML" -> "50ml"
        var upper = s.ToUpperInvariant();
        if (System.Text.RegularExpressions.Regex.IsMatch(upper, @"^\d+(?:\.\d+)?$") && !upper.EndsWith("ML"))
        {
            return s + "ml";
        }

        // compact spaces
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\s+", string.Empty);
        return s;
    }

    private static (int group, int value) GetSizeSortKey(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return (99, int.MaxValue);
        var s = name.Trim().ToUpperInvariant();

        // Volume (ml / l)
        var ml = System.Text.RegularExpressions.Regex.Match(s, @"(\d+(?:\.\d+)?)\s*ML\b");
        if (ml.Success && decimal.TryParse(ml.Groups[1].Value, out var mlValue))
        {
            // store as milliliters
            return (3, (int)Math.Round(mlValue, 0));
        }
        var l = System.Text.RegularExpressions.Regex.Match(s, @"(\d+(?:\.\d+)?)\s*L\b");
        if (l.Success && decimal.TryParse(l.Groups[1].Value, out var lValue))
        {
            // liters -> milliliters
            return (3, (int)Math.Round(lValue * 1000m, 0));
        }

        // Weight (g / kg)
        var g = System.Text.RegularExpressions.Regex.Match(s, @"(\d+(?:\.\d+)?)\s*G\b");
        if (g.Success && decimal.TryParse(g.Groups[1].Value, out var gValue))
        {
            // store as grams
            return (4, (int)Math.Round(gValue, 0));
        }
        var kg = System.Text.RegularExpressions.Regex.Match(s, @"(\d+(?:\.\d+)?)\s*KG\b");
        if (kg.Success && decimal.TryParse(kg.Groups[1].Value, out var kgValue))
        {
            // kilograms -> grams
            return (4, (int)Math.Round(kgValue * 1000m, 0));
        }

        // Shoe sizes like "EU 40"
        var eu = System.Text.RegularExpressions.Regex.Match(s, @"\bEU\s*(\d+(?:\.\d+)?)\b");
        if (eu.Success && decimal.TryParse(eu.Groups[1].Value, out var euValue))
        {
            return (1, (int)Math.Round(euValue * 10m, 0));
        }

        // Numeric sizes first
        var firstNumber = System.Text.RegularExpressions.Regex.Match(s, @"\d+(?:\.\d+)?").Value;
        if (decimal.TryParse(firstNumber, out var numDec))
        {
            // shoe sizes: 38/39/40... (pure numeric)
            if (System.Text.RegularExpressions.Regex.IsMatch(s, @"^\d+(?:\.\d+)?$"))
            {
                return (1, (int)Math.Round(numDec * 10m, 0));
            }
            // fallback numeric
            return (8, (int)Math.Round(numDec * 10m, 0));
        }

        // Common apparel sizes
        return s switch
        {
            "XXS" => (2, 0),
            "XS" => (2, 1),
            "S" => (2, 2),
            "M" => (2, 3),
            "L" => (2, 4),
            "XL" => (2, 5),
            "XXL" => (2, 6),
            "XXXL" => (2, 7),
            _ => (9, int.MaxValue)
        };
    }

    [HttpGet]
    public async Task<IActionResult> ChildCategories(int parentId, CancellationToken cancellationToken)
    {
        if (parentId <= 0) return Json(Array.Empty<object>());

        var children = await _context.Categories
            .AsNoTracking()
            .Where(c => c.IsActive && c.ParentId == parentId)
            .OrderBy(c => c.Name)
            .Select(c => new { value = c.Id, label = c.Name })
            .ToListAsync(cancellationToken);

        if (children.Count > 0) return Json(children);

        var parent = await _context.Categories
            .AsNoTracking()
            .Where(c => c.IsActive && c.Id == parentId)
            .Select(c => new { value = c.Id, label = c.Name })
            .FirstOrDefaultAsync(cancellationToken);

        return parent != null ? Json(new[] { parent }) : Json(Array.Empty<object>());
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        var product = await _context.Products
            .AsNoTracking()
            .Include(p => p.Images)
            .Include(p => p.Variants)
                .ThenInclude(v => v.Color)
            .Include(p => p.Variants)
                .ThenInclude(v => v.Size)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (product is null) return NotFound();

        var vm = await BuildUpsertVmAsync(new ProductAdminUpsertVm
        {
            Id = product.Id,
            Name = product.Name,
            Slug = product.Slug,
            Description = product.Description,
            CategoryId = product.CategoryId,
            Status = product.Status,
            SizeGuide = product.SizeGuides
                .OrderBy(sg => sg.Id)
                .Select(sg => new ProductAdminSizeGuideRowVm
                {
                    SizeLabel = sg.SizeLabel,
                    HeightRange = sg.HeightRange,
                    WeightRange = sg.WeightRange,
                    Note = sg.Note
                })
                .ToList(),
            Variants = product.Variants
                .OrderByDescending(v => v.IsActive)
                .ThenBy(v => v.Id)
                .Select(v => new ProductAdminVariantRowVm
                {
                    VariantId = v.Id,
                    ColorId = v.ColorId,
                    ColorName = v.Color != null ? v.Color.Name : null,
                    SizeId = v.SizeId,
                    SizeName = v.Size != null ? v.Size.Name : null,
                    Sku = v.Sku,
                    Price = v.Price,
                    SalePrice = v.SalePrice,
                    SaleStartDate = v.SaleStartDate,
                    SaleEndDate = v.SaleEndDate,
                    StockQuantity = v.StockQuantity,
                    IsActive = v.IsActive
                })
                .ToList(),
            Images = product.Images
                .OrderByDescending(i => i.IsPrimary)
                .ThenBy(i => i.SortOrder)
                .ThenBy(i => i.Id)
                .Select(i => new ProductAdminImageRowVm
                {
                    ImageId = i.Id,
                    ImageUrl = i.ImageUrl,
                    IsPrimary = i.IsPrimary,
                    SortOrder = i.SortOrder
                })
                .ToList()
        }, cancellationToken);

        if (vm.Images != null)
        {
            foreach (var img in vm.Images)
            {
                img.ImageUrl = ImageUrlHelper.NormalizeStaticFileUrl(img.ImageUrl);
            }
        }

        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> VariantsSection(int productId, CancellationToken cancellationToken)
    {
        if (productId <= 0) return NotFound();

        var variants = await _context.ProductVariants
            .AsNoTracking()
            .Where(v => v.ProductId == productId)
            .Include(v => v.Color)
            .Include(v => v.Size)
            .OrderByDescending(v => v.IsActive)
            .ThenBy(v => v.Id)
            .Select(v => new ProductAdminVariantRowVm
            {
                VariantId = v.Id,
                ColorId = v.ColorId,
                ColorName = v.Color != null ? v.Color.Name : null,
                SizeId = v.SizeId,
                SizeName = v.Size != null ? v.Size.Name : null,
                Sku = v.Sku,
                Price = v.Price,
                SalePrice = v.SalePrice,
                SaleStartDate = v.SaleStartDate,
                SaleEndDate = v.SaleEndDate,
                StockQuantity = v.StockQuantity,
                IsActive = v.IsActive
            })
            .ToListAsync(cancellationToken);

        var vm = new ProductAdminUpsertVm
        {
            Id = productId,
            Variants = variants
        };

        return PartialView("_VariantsSection", vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(ProductAdminUpsertVm model, CancellationToken cancellationToken)
    {
        if (model.Id <= 0) return NotFound();

        var product = await _context.Products.FirstOrDefaultAsync(p => p.Id == model.Id, cancellationToken);
        if (product is null) return NotFound();

        if (!ModelState.IsValid)
        {
            model = await BuildUpsertVmAsync(model, cancellationToken);
            return View(model);
        }

        product.Name = model.Name.Trim();
        product.Slug = string.IsNullOrWhiteSpace(model.Slug) ? Slugify(model.Name) : model.Slug.Trim();
        product.Description = model.Description;
        product.CategoryId = model.CategoryId;
        product.Status = model.Status;
        product.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);

        TempData["AdminToast"] = "Đã cập nhật sản phẩm.";
        return RedirectToAction(nameof(Edit), new { id = product.Id });
    }

    [HttpGet]
    public async Task<IActionResult> FlashSale(int id, CancellationToken cancellationToken)
    {
        // Legacy endpoint: FlashSale is now managed under Admin/FlashSales/Edit.
        return RedirectToAction("Edit", "FlashSales", new { area = "Admin", id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> FlashSale(ProductAdminFlashSaleVm model, CancellationToken cancellationToken)
    {
        // Legacy endpoint: FlashSale is now managed under Admin/FlashSales/Edit.
        TempData["AdminToast"] = "Trang Flash Sale theo sản phẩm đã được chuyển sang mục Flash Sale. Vui lòng thao tác tại đây.";
        return RedirectToAction("Edit", "FlashSales", new { area = "Admin", id = model.ProductId });
    }

    [HttpGet]
    public async Task<IActionResult> Reviews(int id, CancellationToken cancellationToken)
    {
        var product = await _context.Products
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (product is null) return NotFound();

        var reviews = await _context.ProductReviews
            .AsNoTracking()
            .Where(r => r.ProductId == id)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new ProductAdminReviewItemVm
            {
                ReviewId = r.Id,
                Rating = r.Rating,
                Comment = r.Comment,
                CreatedAt = r.CreatedAt,
                AuthorName = r.Account != null
                    ? (r.Account.Profile != null && !string.IsNullOrWhiteSpace(r.Account.Profile.FullName)
                        ? r.Account.Profile.FullName
                        : r.Account.Email)
                    : "Khách hàng"
            })
            .ToListAsync(cancellationToken);

        var vm = new ProductAdminReviewsVm
        {
            ProductId = id,
            ProductName = product.Name,
            Reviews = reviews
        };

        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReplyReview(ProductAdminReplyReviewVm model, CancellationToken cancellationToken)
    {
        if (model.ProductId <= 0 || model.ReviewId <= 0) return NotFound();

        var review = await _context.ProductReviews
            .FirstOrDefaultAsync(r => r.Id == model.ReviewId && r.ProductId == model.ProductId, cancellationToken);

        if (review is null) return NotFound();

        review.SellerReply = model.SellerReply?.Trim();
        review.RepliedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);

        TempData["AdminToast"] = "Đã trả lời đánh giá.";
        return RedirectToAction(nameof(Reviews), new { id = model.ProductId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GenerateVariants(ProductAdminGenerateVariantsVm model, CancellationToken cancellationToken)
    {
        if (model.ProductId <= 0) return NotFound();

        var productExists = await _context.Products.AnyAsync(p => p.Id == model.ProductId, cancellationToken);
        if (!productExists) return NotFound();

        var requestedColorIds = (model.ColorIds ?? new List<int>())
            .Where(id => id > 0)
            .Distinct()
            .ToList();

        var requestedSizeIds = (model.SizeIds ?? new List<int>())
            .Where(id => id > 0)
            .Distinct()
            .ToList();

        var colors = new List<(int? Id, string? Name)>();
        if (requestedColorIds.Count > 0)
        {
            var rows = await _context.Colors
                .AsNoTracking()
                .Where(c => requestedColorIds.Contains(c.Id))
                .Select(c => new { c.Id, c.Name })
                .ToListAsync(cancellationToken);

            if (rows.Count != requestedColorIds.Count)
            {
                TempData["AdminToast"] = "Màu sắc không hợp lệ.";
                return RedirectToAction(nameof(Edit), new { id = model.ProductId, tab = "variants" });
            }

            colors = rows
                .OrderBy(x => x.Name)
                .Select(x => ((int?)x.Id, (string?)x.Name))
                .ToList();
        }
        else
        {
            colors.Add((null, null));
        }

        var sizes = new List<(int? Id, string? Name)>();
        if (requestedSizeIds.Count > 0)
        {
            var rows = await _context.Sizes
                .AsNoTracking()
                .Where(s => requestedSizeIds.Contains(s.Id))
                .Select(s => new { s.Id, s.Name })
                .ToListAsync(cancellationToken);

            if (rows.Count != requestedSizeIds.Count)
            {
                TempData["AdminToast"] = "Size không hợp lệ.";
                return RedirectToAction(nameof(Edit), new { id = model.ProductId, tab = "variants" });
            }

            sizes = rows
                .OrderBy(s => GetSizeSortKey(s.Name))
                .ThenBy(s => s.Name)
                .Select(s => ((int?)s.Id, (string?)s.Name))
                .ToList();
        }
        else
        {
            sizes.Add((null, null));
        }

        var existingVariants = await _context.ProductVariants
            .Where(v => v.ProductId == model.ProductId)
            .ToListAsync(cancellationToken);

        var existingKeys = new HashSet<string>(
            existingVariants.Select(v => $"{v.ColorId?.ToString() ?? "-"}|{v.SizeId?.ToString() ?? "-"}"));

        var defaultAlreadySet = existingVariants.Any(v => v.IsDefault);
        var firstNew = !defaultAlreadySet && existingVariants.Count == 0;

        var created = 0;
        foreach (var c in colors)
        {
            foreach (var s in sizes)
            {
                var key = $"{c.Id?.ToString() ?? "-"}|{s.Id?.ToString() ?? "-"}";
                if (existingKeys.Contains(key)) continue;

                _context.ProductVariants.Add(new ProductVariant
                {
                    ProductId = model.ProductId,
                    ColorId = c.Id,
                    SizeId = s.Id,
                    Sku = BuildVariantSku(model.ProductId, c.Name, s.Name),
                    Price = 0,
                    StockQuantity = 0,
                    IsActive = true,
                    IsDefault = firstNew
                });

                firstNew = false;
                created++;
            }
        }

        if (created > 0)
        {
            await _context.SaveChangesAsync(cancellationToken);
            TempData["AdminToast"] = $"Đã tạo {created} biến thể còn thiếu.";
        }
        else
        {
            TempData["AdminToast"] = "Không có biến thể mới để tạo.";
        }

        return RedirectToAction(nameof(Edit), new { id = model.ProductId, tab = "variants" });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddVariant(ProductAdminVariantEditVm model, CancellationToken cancellationToken)
    {
        if (model.ProductId <= 0) return NotFound();
        if (!ModelState.IsValid)
        {
            TempData["AdminToast"] = "Dữ liệu biến thể không hợp lệ.";
            return RedirectToAction(nameof(Edit), new { id = model.ProductId, tab = "variants" });
        }

        var productExists = await _context.Products.AnyAsync(p => p.Id == model.ProductId, cancellationToken);
        if (!productExists) return NotFound();

        string? colorName = null;
        if (model.ColorId.HasValue)
        {
            colorName = await _context.Colors
                .AsNoTracking()
                .Where(c => c.Id == model.ColorId.Value)
                .Select(c => c.Name)
                .FirstOrDefaultAsync(cancellationToken);

            if (string.IsNullOrWhiteSpace(colorName))
            {
                TempData["AdminToast"] = "Màu không hợp lệ.";
                return RedirectToAction(nameof(Edit), new { id = model.ProductId, tab = "variants" });
            }
        }

        string? sizeName = null;
        if (model.SizeId.HasValue)
        {
            sizeName = await _context.Sizes
                .AsNoTracking()
                .Where(s => s.Id == model.SizeId.Value)
                .Select(s => s.Name)
                .FirstOrDefaultAsync(cancellationToken);

            if (string.IsNullOrWhiteSpace(sizeName))
            {
                TempData["AdminToast"] = "Size không hợp lệ.";
                return RedirectToAction(nameof(Edit), new { id = model.ProductId, tab = "variants" });
            }
        }

        var sku = BuildVariantSku(model.ProductId, colorName, sizeName);

        var keyExists = await _context.ProductVariants
            .AnyAsync(v => v.ProductId == model.ProductId && v.ColorId == model.ColorId && v.SizeId == model.SizeId, cancellationToken);
        if (keyExists)
        {
            TempData["AdminToast"] = "Biến thể này đã tồn tại.";
            return RedirectToAction(nameof(Edit), new { id = model.ProductId, tab = "variants" });
        }

        var skuExists = await _context.ProductVariants
            .AnyAsync(v => v.Sku == sku, cancellationToken);
        if (skuExists)
        {
            TempData["AdminToast"] = "SKU đã tồn tại (trùng).";
            return RedirectToAction(nameof(Edit), new { id = model.ProductId, tab = "variants" });
        }

        var variant = new ProductVariant
        {
            ProductId = model.ProductId,
            ColorId = model.ColorId,
            SizeId = model.SizeId,
            Sku = sku,
            Price = model.Price,
            StockQuantity = model.StockQuantity,
            IsActive = model.IsActive,
            IsDefault = false
        };

        _context.ProductVariants.Add(variant);
        await _context.SaveChangesAsync(cancellationToken);

        TempData["AdminToast"] = "Đã thêm biến thể.";
        return RedirectToAction(nameof(Edit), new { id = model.ProductId, tab = "variants" });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateVariant(ProductAdminVariantEditVm model, CancellationToken cancellationToken)
    {
        if (model.ProductId <= 0 || !model.VariantId.HasValue || model.VariantId.Value <= 0) return NotFound();

        if (!ModelState.IsValid)
        {
            TempData["AdminToast"] = "Dữ liệu biến thể không hợp lệ.";
            return RedirectToAction(nameof(Edit), new { id = model.ProductId, tab = "variants" });
        }

        var variant = await _context.ProductVariants
            .FirstOrDefaultAsync(v => v.Id == model.VariantId.Value && v.ProductId == model.ProductId, cancellationToken);
        if (variant is null) return NotFound();

        string? colorName = null;
        if (model.ColorId.HasValue)
        {
            colorName = await _context.Colors
                .AsNoTracking()
                .Where(c => c.Id == model.ColorId.Value)
                .Select(c => c.Name)
                .FirstOrDefaultAsync(cancellationToken);

            if (string.IsNullOrWhiteSpace(colorName))
            {
                TempData["AdminToast"] = "Màu không hợp lệ.";
                return RedirectToAction(nameof(Edit), new { id = model.ProductId, tab = "variants" });
            }
        }

        string? sizeName = null;
        if (model.SizeId.HasValue)
        {
            sizeName = await _context.Sizes
                .AsNoTracking()
                .Where(s => s.Id == model.SizeId.Value)
                .Select(s => s.Name)
                .FirstOrDefaultAsync(cancellationToken);

            if (string.IsNullOrWhiteSpace(sizeName))
            {
                TempData["AdminToast"] = "Size không hợp lệ.";
                return RedirectToAction(nameof(Edit), new { id = model.ProductId, tab = "variants" });
            }
        }

        var sku = BuildVariantSku(model.ProductId, colorName, sizeName);

        var keyExists = await _context.ProductVariants
            .AnyAsync(v => v.ProductId == model.ProductId
                          && v.Id != model.VariantId.Value
                          && v.ColorId == model.ColorId
                          && v.SizeId == model.SizeId,
                cancellationToken);
        if (keyExists)
        {
            TempData["AdminToast"] = "Biến thể này đã tồn tại.";
            return RedirectToAction(nameof(Edit), new { id = model.ProductId, tab = "variants" });
        }

        var skuExists = await _context.ProductVariants
            .AnyAsync(v => v.Id != model.VariantId.Value && v.Sku == sku, cancellationToken);
        if (skuExists)
        {
            TempData["AdminToast"] = "SKU đã tồn tại (trùng).";
            return RedirectToAction(nameof(Edit), new { id = model.ProductId, tab = "variants" });
        }

        variant.ColorId = model.ColorId;
        variant.SizeId = model.SizeId;
        variant.Sku = sku;
        variant.Price = model.Price;
        variant.StockQuantity = model.StockQuantity;
        variant.IsActive = model.IsActive;

        await _context.SaveChangesAsync(cancellationToken);
        TempData["AdminToast"] = "Đã cập nhật biến thể.";
        return RedirectToAction(nameof(Edit), new { id = model.ProductId, tab = "variants" });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleVariantActive(ProductAdminVariantCommandVm model, CancellationToken cancellationToken)
    {
        if (model.ProductId <= 0 || model.VariantId <= 0) return NotFound();

        var variant = await _context.ProductVariants
            .FirstOrDefaultAsync(v => v.Id == model.VariantId && v.ProductId == model.ProductId, cancellationToken);
        if (variant is null) return NotFound();

        variant.IsActive = !variant.IsActive;

        if (!variant.IsActive && variant.IsDefault)
        {
            variant.IsDefault = false;
            var fallback = await _context.ProductVariants
                .Where(v => v.ProductId == model.ProductId && v.IsActive)
                .OrderBy(v => v.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (fallback is not null)
            {
                fallback.IsDefault = true;
            }
        }

        await _context.SaveChangesAsync(cancellationToken);

        TempData["AdminToast"] = variant.IsActive ? "Đã bật biến thể." : "Đã tắt biến thể.";

        if (IsAjaxRequest())
        {
            return Ok(new { success = true });
        }
        return RedirectToAction(nameof(Edit), new { id = model.ProductId, tab = "variants" });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteVariant(ProductAdminVariantCommandVm model, CancellationToken cancellationToken)
    {
        if (model.ProductId <= 0 || model.VariantId <= 0) return NotFound();

        var variant = await _context.ProductVariants
            .Include(v => v.Images)
            .FirstOrDefaultAsync(v => v.Id == model.VariantId && v.ProductId == model.ProductId, cancellationToken);
        if (variant is null) return NotFound();

        if (variant.Images != null && variant.Images.Count > 0)
        {
            foreach (var img in variant.Images)
            {
                TryDeletePhysicalFile(img.ImageUrl);
            }
            _context.VariantImages.RemoveRange(variant.Images);
        }

        _context.ProductVariants.Remove(variant);
        await _context.SaveChangesAsync(cancellationToken);

        TempData["AdminToast"] = "Đã xóa biến thể.";

        if (IsAjaxRequest())
        {
            return Ok(new { success = true });
        }
        return RedirectToAction(nameof(Edit), new { id = model.ProductId, tab = "variants" });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetDefaultVariant(ProductAdminVariantCommandVm model, CancellationToken cancellationToken)
    {
        if (model.ProductId <= 0 || model.VariantId <= 0) return NotFound();

        var variants = await _context.ProductVariants
            .Where(v => v.ProductId == model.ProductId)
            .ToListAsync(cancellationToken);

        if (variants.Count == 0) return NotFound();

        foreach (var v in variants)
        {
            v.IsDefault = v.Id == model.VariantId;
        }

        await _context.SaveChangesAsync(cancellationToken);
        TempData["AdminToast"] = "Đã đặt biến thể mặc định.";
        return RedirectToAction(nameof(Edit), new { id = model.ProductId, tab = "variants" });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UploadProductImages(ProductAdminUploadProductImagesVm model, CancellationToken cancellationToken)
    {
        if (model.ProductId <= 0) return NotFound();
        if (model.Files == null || model.Files.Count == 0)
        {
            TempData["AdminToast"] = "Chưa chọn ảnh.";
            return RedirectToAction(nameof(Edit), new { id = model.ProductId, tab = "images" });
        }

        var product = await _context.Products
            .Include(p => p.Images)
            .FirstOrDefaultAsync(p => p.Id == model.ProductId, cancellationToken);
        if (product is null) return NotFound();

        var physicalFolder = EnsureUploadsFolder("products", model.ProductId.ToString());
        var maxSort = product.Images?.Select(i => i.SortOrder).DefaultIfEmpty(0).Max() ?? 0;

        foreach (var file in model.Files.Where(f => f != null && f.Length > 0))
        {
            var savedUrl = await SaveUploadedImageAsync(file, physicalFolder, $"p{model.ProductId}", cancellationToken);
            if (string.IsNullOrWhiteSpace(savedUrl)) continue;

            maxSort++;
            product.Images.Add(new ProductImage
            {
                ProductId = model.ProductId,
                ImageUrl = savedUrl,
                IsPrimary = product.Images.Count == 0,
                SortOrder = maxSort
            });
        }

        await _context.SaveChangesAsync(cancellationToken);
        TempData["AdminToast"] = "Đã tải ảnh lên.";
        return RedirectToAction(nameof(Edit), new { id = model.ProductId, tab = "images" });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetPrimaryProductImage(ProductAdminSetPrimaryImageVm model, CancellationToken cancellationToken)
    {
        if (model.ProductId <= 0 || model.ImageId <= 0) return NotFound();

        var images = await _context.ProductImages
            .Where(i => i.ProductId == model.ProductId)
            .ToListAsync(cancellationToken);
        if (images.Count == 0) return NotFound();

        foreach (var img in images)
        {
            img.IsPrimary = img.Id == model.ImageId;
        }

        await _context.SaveChangesAsync(cancellationToken);
        TempData["AdminToast"] = "Đã đặt ảnh chính.";
        return RedirectToAction(nameof(Edit), new { id = model.ProductId, tab = "images" });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteProductImage(ProductAdminDeleteImageVm model, CancellationToken cancellationToken)
    {
        if (model.ProductId <= 0 || model.ImageId <= 0) return NotFound();

        var image = await _context.ProductImages
            .FirstOrDefaultAsync(i => i.Id == model.ImageId && i.ProductId == model.ProductId, cancellationToken);
        if (image is null) return NotFound();

        TryDeletePhysicalFile(image.ImageUrl);
        _context.ProductImages.Remove(image);
        await _context.SaveChangesAsync(cancellationToken);

        var remaining = await _context.ProductImages
            .Where(i => i.ProductId == model.ProductId)
            .OrderBy(i => i.SortOrder)
            .ToListAsync(cancellationToken);

        if (remaining.Count > 0 && !remaining.Any(i => i.IsPrimary))
        {
            remaining[0].IsPrimary = true;
            await _context.SaveChangesAsync(cancellationToken);
        }

        TempData["AdminToast"] = "Đã xóa ảnh.";
        return RedirectToAction(nameof(Edit), new { id = model.ProductId, tab = "images" });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReorderProductImages(ProductAdminReorderImagesVm model, CancellationToken cancellationToken)
    {
        if (model.ProductId <= 0) return NotFound();
        if (model.ImageIds == null || model.ImageIds.Count == 0)
        {
            return RedirectToAction(nameof(Edit), new { id = model.ProductId, tab = "images" });
        }

        var images = await _context.ProductImages
            .Where(i => i.ProductId == model.ProductId)
            .ToListAsync(cancellationToken);

        var map = images.ToDictionary(i => i.Id);
        var sort = 1;
        foreach (var id in model.ImageIds)
        {
            if (map.TryGetValue(id, out var img))
            {
                img.SortOrder = sort++;
            }
        }

        await _context.SaveChangesAsync(cancellationToken);
        TempData["AdminToast"] = "Đã sắp xếp ảnh.";
        return RedirectToAction(nameof(Edit), new { id = model.ProductId, tab = "images" });
    }

    [HttpGet]
    public async Task<IActionResult> VariantImages(int productId, int variantId, CancellationToken cancellationToken)
    {
        var variant = await _context.ProductVariants
            .AsNoTracking()
            .Include(v => v.Product)
            .Include(v => v.Color)
            .Include(v => v.Size)
            .Include(v => v.Images)
            .FirstOrDefaultAsync(v => v.ProductId == productId && v.Id == variantId, cancellationToken);

        if (variant is null) return NotFound();

        var label = BuildVariantLabel(variant);

        var vm = new ProductAdminVariantImagesVm
        {
            ProductId = productId,
            VariantId = variantId,
            ProductName = variant.Product != null ? variant.Product.Name : string.Empty,
            VariantLabel = label,
            Images = variant.Images
                .OrderByDescending(i => i.IsPrimary)
                .ThenBy(i => i.SortOrder)
                .ThenBy(i => i.Id)
                .Select(i => new ProductAdminVariantImageRowVm
                {
                    ImageId = i.Id,
                    ImageUrl = i.ImageUrl,
                    IsPrimary = i.IsPrimary,
                    SortOrder = i.SortOrder
                })
                .ToList()
        };

        foreach (var img in vm.Images)
        {
            img.ImageUrl = ImageUrlHelper.NormalizeStaticFileUrl(img.ImageUrl);
        }

        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UploadVariantImages(ProductAdminUploadVariantImagesVm model, CancellationToken cancellationToken)
    {
        if (model.ProductId <= 0 || model.VariantId <= 0) return NotFound();
        if (model.Files == null || model.Files.Count == 0)
        {
            TempData["AdminToast"] = "Chưa chọn ảnh.";
            return RedirectToAction(nameof(VariantImages), new { productId = model.ProductId, variantId = model.VariantId });
        }

        var variant = await _context.ProductVariants
            .Include(v => v.Images)
            .FirstOrDefaultAsync(v => v.ProductId == model.ProductId && v.Id == model.VariantId, cancellationToken);
        if (variant is null) return NotFound();

        var physicalFolder = EnsureUploadsFolder("variants", model.VariantId.ToString());
        var maxSort = variant.Images?.Select(i => i.SortOrder).DefaultIfEmpty(0).Max() ?? 0;

        foreach (var file in model.Files.Where(f => f != null && f.Length > 0))
        {
            var savedUrl = await SaveUploadedImageAsync(file, physicalFolder, $"v{model.VariantId}", cancellationToken);
            if (string.IsNullOrWhiteSpace(savedUrl)) continue;

            maxSort++;
            variant.Images.Add(new VariantImage
            {
                VariantId = model.VariantId,
                ImageUrl = savedUrl,
                IsPrimary = variant.Images.Count == 0,
                SortOrder = maxSort
            });
        }

        await _context.SaveChangesAsync(cancellationToken);
        TempData["AdminToast"] = "Đã tải ảnh biến thể.";
        return RedirectToAction(nameof(VariantImages), new { productId = model.ProductId, variantId = model.VariantId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetPrimaryVariantImage(int productId, int variantId, int imageId, CancellationToken cancellationToken)
    {
        var images = await _context.VariantImages
            .Where(i => i.VariantId == variantId)
            .ToListAsync(cancellationToken);
        if (images.Count == 0) return NotFound();

        foreach (var img in images)
        {
            img.IsPrimary = img.Id == imageId;
        }

        await _context.SaveChangesAsync(cancellationToken);
        TempData["AdminToast"] = "Đã đặt ảnh chính biến thể.";
        return RedirectToAction(nameof(VariantImages), new { productId, variantId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteVariantImage(int productId, int variantId, int imageId, CancellationToken cancellationToken)
    {
        var image = await _context.VariantImages
            .FirstOrDefaultAsync(i => i.Id == imageId && i.VariantId == variantId, cancellationToken);
        if (image is null) return NotFound();

        TryDeletePhysicalFile(image.ImageUrl);
        _context.VariantImages.Remove(image);
        await _context.SaveChangesAsync(cancellationToken);

        var remaining = await _context.VariantImages
            .Where(i => i.VariantId == variantId)
            .OrderBy(i => i.SortOrder)
            .ToListAsync(cancellationToken);
        if (remaining.Count > 0 && !remaining.Any(i => i.IsPrimary))
        {
            remaining[0].IsPrimary = true;
            await _context.SaveChangesAsync(cancellationToken);
        }

        TempData["AdminToast"] = "Đã xóa ảnh biến thể.";
        return RedirectToAction(nameof(VariantImages), new { productId, variantId });
    }

    private async Task<ProductAdminUpsertVm> BuildUpsertVmAsync(ProductAdminUpsertVm? existing, CancellationToken cancellationToken)
    {
        var vm = existing ?? new ProductAdminUpsertVm();

        vm.CategoryOptions = await _context.Categories
            .AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.Name)
            .Select(c => new ProductAdminSelectOptionVm { Value = c.Id, Label = c.Name })
            .ToListAsync(cancellationToken);

        vm.ParentCategoryOptions = await _context.Categories
            .AsNoTracking()
            .Where(c => c.IsActive && c.ParentId == null)
            .OrderBy(c => c.Name)
            .Select(c => new ProductAdminSelectOptionVm { Value = c.Id, Label = c.Name })
            .ToListAsync(cancellationToken);

        if (!vm.ParentCategoryId.HasValue || vm.ParentCategoryId.Value <= 0)
        {
            if (vm.CategoryId > 0)
            {
                var cat = await _context.Categories
                    .AsNoTracking()
                    .FirstOrDefaultAsync(c => c.Id == vm.CategoryId, cancellationToken);
                if (cat != null)
                {
                    vm.ParentCategoryId = cat.ParentId ?? cat.Id;
                }
            }
        }

        vm.ChildCategoryOptions = new List<ProductAdminSelectOptionVm>();
        if (vm.ParentCategoryId.HasValue && vm.ParentCategoryId.Value > 0)
        {
            vm.ChildCategoryOptions = await _context.Categories
                .AsNoTracking()
                .Where(c => c.IsActive && c.ParentId == vm.ParentCategoryId.Value)
                .OrderBy(c => c.Name)
                .Select(c => new ProductAdminSelectOptionVm { Value = c.Id, Label = c.Name })
                .ToListAsync(cancellationToken);

            if (vm.ChildCategoryOptions.Count == 0)
            {
                var parent = await _context.Categories
                    .AsNoTracking()
                    .Where(c => c.IsActive && c.Id == vm.ParentCategoryId.Value)
                    .Select(c => new ProductAdminSelectOptionVm { Value = c.Id, Label = c.Name })
                    .FirstOrDefaultAsync(cancellationToken);
                if (parent != null) vm.ChildCategoryOptions.Add(parent);
            }
        }

        vm.StatusOptions = new List<ProductAdminSelectOptionVm>
        {
            new() { ValueString = "Active", Label = "Bật (Active)" },
            new() { ValueString = "Hidden", Label = "Ẩn (Hidden)" },
            new() { ValueString = "Draft", Label = "Nháp (Draft)" }
        };

        vm.ColorOptions = await _context.Colors
            .AsNoTracking()
            .OrderBy(c => c.Name)
            .Select(c => new ProductAdminSelectOptionVm { Value = c.Id, Label = c.Name, Hex = c.Code })
            .ToListAsync(cancellationToken);

        var sizes = await _context.Sizes
            .AsNoTracking()
            .Select(s => new { s.Id, s.Name })
            .ToListAsync(cancellationToken);

        vm.SizeOptions = sizes
            .OrderBy(s => GetSizeSortKey(s.Name))
            .ThenBy(s => s.Name)
            .Select(s => new ProductAdminSelectOptionVm { Value = s.Id, Label = s.Name })
            .ToList();

        vm.InitialSizeStocks ??= new List<ProductAdminInitialSizeStockVm>();
        if (vm.InitialSizeStocks.Count == 0)
        {
            vm.InitialSizeStocks = vm.SizeOptions
                .Select(s => new ProductAdminInitialSizeStockVm
                {
                    SizeId = s.Value,
                    SizeName = s.Label,
                    StockQuantity = 0
                })
                .ToList();
        }
        else
        {
            var map = vm.SizeOptions.ToDictionary(x => x.Value, x => x.Label);
            foreach (var row in vm.InitialSizeStocks)
            {
                if (row.SizeName == null && map.TryGetValue(row.SizeId, out var name))
                {
                    row.SizeName = name;
                }
            }
        }

        vm.SizeGuide ??= new List<ProductAdminSizeGuideRowVm>();
        if (vm.SizeGuide.Count == 0)
        {
            vm.SizeGuide.Add(new ProductAdminSizeGuideRowVm());
        }

        vm.VolumeOptionStocks ??= new List<ProductAdminCustomOptionStockVm>();
        if (vm.VolumeOptionStocks.Count == 0)
        {
            vm.VolumeOptionStocks.Add(new ProductAdminCustomOptionStockVm());
        }

        vm.Variants ??= new List<ProductAdminVariantRowVm>();
        vm.Images ??= new List<ProductAdminImageRowVm>();

        return vm;
    }

    private static string Slugify(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        var slug = input.Trim().ToLowerInvariant();
        slug = slug.Replace("đ", "d");
        slug = System.Text.RegularExpressions.Regex.Replace(slug, @"[^a-z0-9\u00C0-\u024f\s-]", string.Empty);
        slug = System.Text.RegularExpressions.Regex.Replace(slug, @"\s+", "-");
        slug = System.Text.RegularExpressions.Regex.Replace(slug, "-+", "-");
        return slug.Trim('-');
    }

    private static string BuildVariantLabel(ProductVariant v)
    {
        var color = v.Color != null ? v.Color.Name : (v.ColorId.HasValue ? $"Màu #{v.ColorId.Value}" : "Màu -");
        var size = v.Size != null ? v.Size.Name : (v.SizeId.HasValue ? $"Size #{v.SizeId.Value}" : "Size -");
        return $"{color} / {size}";
    }

    private string EnsureUploadsFolder(params string[] parts)
    {
        var webRoot = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
        var root = Path.Combine(webRoot, "uploads");
        var full = parts.Aggregate(root, Path.Combine);
        Directory.CreateDirectory(full);
        return full;
    }

    private async Task<string?> SaveUploadedImageAsync(IFormFile file, string physicalFolder, string namePrefix, CancellationToken cancellationToken)
    {
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".webp" };
        if (!allowed.Contains(ext)) return null;
        if (file.Length > 5 * 1024 * 1024) return null;

        var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        var rand = Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant();
        var fileName = $"{namePrefix}_{stamp}_{rand}{ext}";
        var physicalPath = Path.Combine(physicalFolder, fileName);

        await using (var stream = System.IO.File.Create(physicalPath))
        {
            await file.CopyToAsync(stream, cancellationToken);
        }

        var webRoot = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
        var relative = Path.GetRelativePath(webRoot, physicalPath).Replace('\\', '/');
        return "/" + relative;
    }

    private void TryDeletePhysicalFile(string? imageUrl)
    {
        if (string.IsNullOrWhiteSpace(imageUrl)) return;
        if (!imageUrl.StartsWith("/", StringComparison.Ordinal)) return;

        var webRoot = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
        var rel = imageUrl.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        var physicalPath = Path.Combine(webRoot, rel);

        try
        {
            if (System.IO.File.Exists(physicalPath))
            {
                System.IO.File.Delete(physicalPath);
            }
        }
        catch
        {
            // best-effort
        }
    }
}