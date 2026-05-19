using System.Security.Claims;
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
            .Select(c => new { c.Id, c.Name, c.ParentId, c.SortOrder })
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .ToListAsync(cancellationToken);
        return Json(all);
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
        // Remove Vietnamese diacritics
        slug = Regex.Replace(slug, @"[àáạảãâầấậẩẫăằắặẳẵ]", "a");
        slug = Regex.Replace(slug, @"[èéẹẻẽêềếệểễ]", "e");
        slug = Regex.Replace(slug, @"[ìíịỉĩ]", "i");
        slug = Regex.Replace(slug, @"[òóọỏõôồốộổỗơờớợởỡ]", "o");
        slug = Regex.Replace(slug, @"[ùúụủũưừứựửữ]", "u");
        slug = Regex.Replace(slug, @"[ỳýỵỷỹ]", "y");
        slug = Regex.Replace(slug, @"[đ]", "d");
        slug = Regex.Replace(slug, @"[^a-z0-9\s-]", "");
        slug = Regex.Replace(slug, @"[\s-]+", "-").Trim('-');
        return slug;
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

        var q = ScopedProducts(accountId, role).AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            q = q.Where(p => p.Name.Contains(query.Search) || p.Id.ToString() == query.Search);
        }

        if (query.CategoryId.HasValue)
        {
            q = q.Where(p => p.CategoryId == query.CategoryId.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            q = q.Where(p => p.Status == query.Status);
        }

        var totalItems = await q.CountAsync(cancellationToken);
        var page = query.Page <= 0 ? 1 : query.Page;
        var pageSize = query.PageSize <= 0 ? 10 : query.PageSize;

        var products = await q
            .OrderByDescending(p => p.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new ProductAdminListItemVm
            {
                Id = p.Id,
                Name = p.Name,
                CategoryName = p.Category != null ? p.Category.Name : null,
                Status = p.Status,
                CreatedAt = p.CreatedAt,
                VariantCount = p.Variants.Count(v => !v.IsDeleted),
                PrimaryImageUrl = p.Images.Where(i => i.IsPrimary).Select(i => i.ImageUrl).FirstOrDefault()
                                  ?? p.Images.OrderBy(i => i.SortOrder).Select(i => i.ImageUrl).FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

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
                Page = page,
                PageSize = pageSize
            },
            Products = products,
            TotalItems = totalItems,
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
            .Include(p => p.Images)
            .Include(p => p.Variants).ThenInclude(v => v.Color)
            .Include(p => p.Variants).ThenInclude(v => v.Size)
            .Include(p => p.SizeGuides).ThenInclude(g => g.Details)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (product == null) return NotFound();

        var vm = new ProductAdminDetailsVm
        {
            ProductId = product.Id,
            Name = product.Name,
            Slug = product.Slug,
            CategoryName = product.Category?.Name,
            Status = product.Status ?? "Active",
            Description = product.Description,
            AverageRating = product.AverageRating,
            ReviewCount = product.ReviewCount,
            CreatedAt = product.CreatedAt,
            UpdatedAt = product.UpdatedAt,
            SelectedVariantId = selectedVariantId,
            TotalStockQuantity = product.Variants.Where(v => !v.IsDeleted).Sum(v => v.StockQuantity),
            Images = product.Images.Select(i => new ProductAdminDetailsImageVm
            {
                Url = i.ImageUrl,
                Alt = product.Name,
                IsPrimary = i.IsPrimary,
                SortOrder = i.SortOrder
            }).ToList(),
            Variants = product.Variants.Where(v => !v.IsDeleted).Select(v => new ProductAdminDetailsVariantVm
            {
                VariantId = v.Id,
                Sku = v.Sku,
                ColorId = v.ColorId,
                ColorName = v.Color?.Name,
                ColorHex = v.Color?.Code,
                SizeId = v.SizeId,
                SizeName = v.Size?.Name,
                Price = v.Price,
                SalePrice = v.SalePrice,
                SaleStartDate = v.SaleStartDate,
                SaleEndDate = v.SaleEndDate,
                StockQuantity = v.StockQuantity,
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
        var vm = new ProductAdminUpsertVm();
        await PopulateUpsertVmAsync(vm, cancellationToken);
        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        if (!TryGetSignedInAccount(out var accountId, out var role)) return Challenge();

        var product = await ScopedProducts(accountId, role)
            .Include(p => p.Images)
            .Include(p => p.Variants).ThenInclude(v => v.Color)
            .Include(p => p.Variants).ThenInclude(v => v.Size)
            .Include(p => p.SizeGuides).ThenInclude(g => g.Details)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (product == null) return NotFound();

        var vm = new ProductAdminUpsertVm
        {
            Id = product.Id,
            Name = product.Name,
            Slug = product.Slug,
            CategoryId = product.CategoryId,
            Description = product.Description,
            Status = product.Status,
            Price = product.Variants.FirstOrDefault(v => v.IsDefault)?.Price ?? product.Variants.FirstOrDefault()?.Price ?? 0m,
            SalePrice = product.Variants.FirstOrDefault(v => v.IsDefault)?.SalePrice ?? product.Variants.FirstOrDefault()?.SalePrice,
            HasSizeVariants = product.Variants.Count > 1 || (product.Variants.Count == 1 && product.Variants.First().SizeId != null),
            Variants = product.Variants
                .Where(v => !v.IsDeleted)
                .OrderBy(v => v.Color != null ? v.Color.Name : "")
                .ThenBy(v => v.Size != null ? v.Size.SortOrder : 0)
                .ThenBy(v => v.Size != null ? v.Size.Name : "")
                .Select(v => new ProductAdminVariantRowVm
                {
                    VariantId = v.Id,
                    Sku = v.Sku,
                    ColorId = v.ColorId,
                    ColorName = v.Color?.Name,
                    ColorHex = v.Color?.Code,
                    SizeId = v.SizeId,
                    SizeName = v.Size?.Name,
                    Price = v.Price,
                    SalePrice = v.SalePrice,
                    SaleEndDate = v.SaleEndDate,
                    StockQuantity = v.StockQuantity,
                    IsActive = v.IsActive,
                    IsActiveStr = v.IsActive ? "true" : null
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
    public async Task<IActionResult> Create(ProductAdminUpsertVm vm, CancellationToken cancellationToken)
    {
        if (!TryGetSignedInAccount(out var accountId, out var role)) return Challenge();

        if (!ModelState.IsValid)
        {
            await PopulateUpsertVmAsync(vm, cancellationToken);
            return View(vm);
        }

        var product = new Product
        {
            Name = vm.Name,
            Slug = string.IsNullOrWhiteSpace(vm.Slug) ? GenerateSlug(vm.Name) : vm.Slug,
            CategoryId = vm.CategoryId,
            Description = vm.Description,
            Status = vm.Status,
            ShopId = string.Equals(role, "Shop", StringComparison.OrdinalIgnoreCase) ? accountId : 0,
            CreatedAt = DateTime.UtcNow
        };

        _db.Products.Add(product);
        await _db.SaveChangesAsync(cancellationToken);

        // Save Images
        await SyncImagesAsync(product.Id, vm, cancellationToken);

        // Save Variants
        await SyncVariantsAsync(product.Id, vm, cancellationToken);

        // Save Size Guide
        await SaveSizeGuideAsync(product.Id, vm.SizeGuide, cancellationToken);

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Edit(ProductAdminUpsertVm vm, CancellationToken cancellationToken)
    {
        if (!TryGetSignedInAccount(out var accountId, out var role)) return Challenge();

        if (!ModelState.IsValid)
        {
            await PopulateUpsertVmAsync(vm, cancellationToken);
            return View(vm);
        }

        var product = await ScopedProducts(accountId, role)
            .FirstOrDefaultAsync(p => p.Id == vm.Id, cancellationToken);

        if (product == null) return NotFound();

        product.Name = vm.Name;
        product.Slug = string.IsNullOrWhiteSpace(vm.Slug) ? GenerateSlug(vm.Name) : vm.Slug;
        product.CategoryId = vm.CategoryId;
        product.Description = vm.Description;
        product.Status = vm.Status;

        await _db.SaveChangesAsync(cancellationToken);

        // Save Images
        await SyncImagesAsync(product.Id, vm, cancellationToken);

        // Save Variants
        await SyncVariantsAsync(product.Id, vm, cancellationToken);

        // Save Size Guide
        await SaveSizeGuideAsync(product.Id, vm.SizeGuide, cancellationToken);

        return RedirectToAction(nameof(Index));
    }

    private async Task SaveSizeGuideAsync(int productId, List<ProductAdminSizeGuideRowVm> rows, CancellationToken cancellationToken)
    {
        // Clear existing
        var existing = await _db.Set<SizeGuide>().Where(g => g.ProductId == productId).ToListAsync(cancellationToken);
        _db.Set<SizeGuide>().RemoveRange(existing);

        if (rows == null || rows.Count == 0) return;

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

            // Details
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

        // If in Edit mode, we might want to preserve existing images
        // For simplicity in Create, we just add new ones.
        // In Edit, vm.Images might contain existing URLs.

        int sortOrder = 0;

        // Primary Image
        if (vm.PrimaryImage != null)
        {
            // If editing, mark previous primary as not primary
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
                SortOrder = -1 // Always first
            };
            _db.ProductImages.Add(pImg);
        }

        // Gallery Images
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

        // In Edit mode, handle existing images if they were marked for deletion?
        // For now, we assume vm.Images (List<ProductAdminImageRowVm>) is what they want to KEEP.
        // But the current UI doesn't send back which ones to delete easily.
        // Let's at least save the new ones.

        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task SyncVariantsAsync(int productId, ProductAdminUpsertVm vm, CancellationToken cancellationToken)
    {
        if (vm.Id == 0) // Create mode: generate from initial stocks
        {
            if (!vm.HasSizeVariants)
            {
                var variant = new ProductVariant
                {
                    ProductId = productId,
                    Sku = GenerateSku(productId, null, null),
                    Price = vm.Price,
                    SalePrice = vm.SalePrice,
                    StockQuantity = vm.SingleStockQuantity,
                    IsDefault = true,
                    IsActive = true
                };
                _db.ProductVariants.Add(variant);
            }
            else
            {
                bool isFirst = true;

                if (vm.SizeScheme == "volume" || vm.SizeScheme == "weight")
                {
                    foreach (var opt in vm.VolumeOptionStocks)
                    {
                        if (string.IsNullOrWhiteSpace(opt.OptionName)) continue;
                        var sizeId = await GetOrCreateSizeIdAsync(opt.OptionName, cancellationToken);
                        var cId = opt.ColorId > 0 ? opt.ColorId : null;

                        var variant = new ProductVariant
                        {
                            ProductId = productId,
                            ColorId = cId,
                            SizeId = sizeId,
                            Sku = GenerateSku(productId, cId, sizeId),
                            Price = vm.Price,
                            SalePrice = vm.SalePrice,
                            StockQuantity = opt.StockQuantity,
                            IsActive = true,
                            IsDefault = isFirst
                        };
                        _db.ProductVariants.Add(variant);
                        isFirst = false;
                    }
                }
                else
                {
                    foreach (var opt in vm.InitialSizeStocks)
                    {
                        if (!opt.IsSelected) continue;
                        if (opt.StockQuantity < 0) continue;
                        
                        var cId = opt.ColorId > 0 ? opt.ColorId : null;

                        var variant = new ProductVariant
                        {
                            ProductId = productId,
                            ColorId = cId,
                            SizeId = opt.SizeId,
                            Sku = GenerateSku(productId, cId, opt.SizeId),
                            Price = vm.Price,
                            SalePrice = vm.SalePrice,
                            StockQuantity = opt.StockQuantity,
                            IsActive = true,
                            IsDefault = isFirst
                        };
                        _db.ProductVariants.Add(variant);
                        isFirst = false;
                    }
                }
            }
        }
        else // Edit mode: update existing or add new from vm.Variants
        {
            // Safety: if the form didn't post any variants (e.g., JS failed to render rows),
            // do not wipe/disable existing variants.
            if (vm.Variants == null || vm.Variants.Count == 0) return;

            var existing = await _db.ProductVariants.Where(v => v.ProductId == productId && !v.IsDeleted).ToListAsync(cancellationToken);
            var submittedIds = vm.Variants.Select(v => v.VariantId).Where(id => id > 0).ToHashSet();

            static bool ResolveIsActive(ProductAdminVariantRowVm v)
            {
                if (!string.IsNullOrWhiteSpace(v.IsActiveStr))
                {
                    return string.Equals(v.IsActiveStr, "true", StringComparison.OrdinalIgnoreCase);
                }

                return v.IsActive;
            }

            // Mark removed ones as deleted
            foreach (var e in existing)
            {
                if (!submittedIds.Contains(e.Id)) e.IsDeleted = true;
            }

            foreach (var v in vm.Variants)
            {
                var isActive = ResolveIsActive(v);
                if (v.VariantId > 0)
                {
                    var e = existing.FirstOrDefault(x => x.Id == v.VariantId);
                    if (e != null)
                    {
                        e.Sku = v.Sku;
                        e.Price = v.Price;
                        e.SalePrice = v.SalePrice;
                        e.StockQuantity = v.StockQuantity;
                        e.IsActive = isActive;
                        e.SaleStartDate = v.SaleStartDate;
                        e.SaleEndDate = v.SaleEndDate;
                    }
                }
                else
                {
                    // Add new variant in Edit mode (e.g., added via JS in the future)
                    var variant = new ProductVariant
                    {
                        ProductId = productId,
                        ColorId = v.ColorId,
                        SizeId = v.SizeId,
                        Sku = v.Sku ?? GenerateSku(productId, v.ColorId, v.SizeId),
                        Price = v.Price,
                        SalePrice = v.SalePrice,
                        StockQuantity = v.StockQuantity,
                        IsActive = isActive,
                        SaleStartDate = v.SaleStartDate,
                        SaleEndDate = v.SaleEndDate
                    };
                    _db.ProductVariants.Add(variant);
                }
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task<int> GetOrCreateSizeIdAsync(string name, CancellationToken cancellationToken)
    {
        var size = await _db.Sizes.FirstOrDefaultAsync(s => s.Name == name, cancellationToken);
        if (size != null) return size.Id;

        size = new Size { Name = name, SortOrder = 999 };
        _db.Sizes.Add(size);
        await _db.SaveChangesAsync(cancellationToken);
        return size.Id;
    }

    private string GenerateSku(int productId, int? colorId, int? sizeId)
    {
        return $"P{productId}-{colorId ?? 0}-{sizeId ?? 0}";
    }

    private async Task PopulateUpsertVmAsync(ProductAdminUpsertVm vm, CancellationToken cancellationToken)
    {
        vm.ParentCategoryOptions = await _db.Categories
            .AsNoTracking()
            .Where(c => c.ParentId == null && !c.IsDeleted)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .Select(c => new ProductAdminSelectOptionVm { Value = c.Id, Label = c.Name })
            .ToListAsync(cancellationToken);

        vm.StatusOptions = new List<ProductAdminSelectOptionVm>
        {
            new() { ValueString = "Active", Label = "Đang bán (Active)" },
            new() { ValueString = "Draft", Label = "Nháp (Draft)" },
            new() { ValueString = "Hidden", Label = "Ẩn (Hidden)" }
        };

        vm.ColorOptions = await _db.Colors
            .AsNoTracking()
            .OrderBy(c => c.Name)
            .Select(c => new ProductAdminSelectOptionVm { Value = c.Id, Label = c.Name, Hex = c.Code })
            .ToListAsync(cancellationToken);
            
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

        // Populate sizes for the variant management table
        var sortedSizes = await _db.Sizes
            .AsNoTracking()
            .OrderBy(s => s.SortOrder)
            .ThenBy(s => s.Id)
            .ToListAsync(cancellationToken);

        vm.SizeOptions = sortedSizes.Select(s => new ProductAdminSelectOptionVm { Value = s.Id, Label = s.Name }).ToList();
        
        if (vm.InitialSizeStocks.Count == 0)
        {
            vm.InitialSizeStocks = sortedSizes.Select(s => new ProductAdminInitialSizeStockVm
            {
                SizeId = s.Id,
                SizeName = s.Name,
                StockQuantity = 0
            }).ToList();
        }
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
                TotalStockQuantity = p.Variants.Where(v => !v.IsDeleted).Sum(v => v.StockQuantity)
            })
            .ToListAsync(cancellationToken);

        AdminInventorySelectedProductVm? selected = null;
        if (query.ProductId.HasValue)
        {
            var p = await baseQ
                .Include(p => p.Images)
                .Include(p => p.Variants).ThenInclude(v => v.Color)
                .Include(p => p.Variants).ThenInclude(v => v.Size)
                .FirstOrDefaultAsync(x => x.Id == query.ProductId.Value, cancellationToken);

            if (p != null)
            {
                selected = new AdminInventorySelectedProductVm
                {
                    ProductId = p.Id,
                    Name = p.Name,
                    PrimaryImageUrl = p.Images.Where(i => i.IsPrimary).Select(i => i.ImageUrl).FirstOrDefault()
                                      ?? p.Images.OrderBy(i => i.SortOrder).Select(i => i.ImageUrl).FirstOrDefault(),
                    Variants = p.Variants.Where(v => !v.IsDeleted).Select(v => new AdminInventoryVariantRowVm
                    {
                        VariantId = v.Id,
                        Sku = v.Sku,
                        ColorName = v.Color?.Name,
                        SizeName = v.Size?.Name,
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
                variant.UpdatedAt = DateTime.UtcNow;
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
        TempData["AdminToast"] = "Cập nhật kho hàng thành công!";

        return RedirectToAction(nameof(Inventory), new { productId = model.ProductId });
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
