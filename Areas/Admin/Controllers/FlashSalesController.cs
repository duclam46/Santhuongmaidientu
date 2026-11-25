using System.Security.Claims;
using DOAN_BANHANG.Models;
using DOAN_BANHANG.Helpers;
using DOAN_BANHANG.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using System.Text.Json;
using System.Globalization;

namespace DOAN_BANHANG.Areas.Admin.Controllers;

[Area("Admin")]
[Route("Admin/[controller]/[action]")]
[Microsoft.AspNetCore.Authorization.Authorize(Roles = "Shop")]
public class FlashSalesController : Controller
{
    private readonly DataContext _context;

    public FlashSalesController(DataContext context)
    {
        _context = context;
    }

    private static string BuildVariantLabel(ProductVariant v)
    {
        var part1 = v.OptionValue1?.Value;
        var part2 = v.OptionValue2?.Value;
        var parts = new[] { part1, part2 }
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToArray();

        return parts.Length > 0 ? string.Join(" - ", parts) : "Mặc định";
    }

    [HttpGet]
    [HttpGet("/Admin/FlashSales")]
    public async Task<IActionResult> Index(string? search, CancellationToken cancellationToken)
    {
        var currentShopId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
        var q = _context.Products
            .AsNoTracking()
            .Where(p => p.ShopId == currentShopId && !p.IsDeleted)
            .Where(p => p.Variants.Any(v => !v.IsDeleted
                && v.SalePrice != null
                && v.SaleStartDate != null
                && v.SaleEndDate != null));

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            q = q.Where(p => p.Name.Contains(term));
        }

        var items = await q
            .OrderByDescending(p => p.Id)
            .Take(200)
            .Select(p => new AdminFlashSaleConfiguredItemVm
            {
                ProductId = p.Id,
                Name = p.Name,
                PrimaryImageUrl = p.Images
                    .OrderByDescending(i => i.IsPrimary)
                    .ThenBy(i => i.SortOrder)
                    .Select(i => i.ImageUrl)
                    .FirstOrDefault(),
                VariantOnSaleCount = p.Variants.Count(v => !v.IsDeleted
                    && v.SalePrice != null
                    && v.SaleStartDate != null
                    && v.SaleEndDate != null),
                MinPrice = p.Variants
                    .Where(v => !v.IsDeleted)
                    .Select(v => (decimal?)v.Price)
                    .Min() ?? 0m,
                MinSalePrice = p.Variants
                    .Where(v => !v.IsDeleted
                        && v.SalePrice != null
                        && v.SaleStartDate != null
                        && v.SaleEndDate != null)
                    .Select(v => v.SalePrice)
                    .Min(),
                SaleStartDate = p.Variants
                    .Where(v => !v.IsDeleted
                        && v.SalePrice != null
                        && v.SaleStartDate != null
                        && v.SaleEndDate != null)
                    .Select(v => v.SaleStartDate)
                    .Min(),
                SaleEndDate = p.Variants
                    .Where(v => !v.IsDeleted
                        && v.SalePrice != null
                        && v.SaleStartDate != null
                        && v.SaleEndDate != null)
                    .Select(v => v.SaleEndDate)
                    .Max(),
            })
            .ToListAsync(cancellationToken);

        foreach (var item in items)
        {
            item.PrimaryImageUrl = ImageUrlHelper.NormalizeStaticFileUrl(item.PrimaryImageUrl);
        }

        var vm = new AdminFlashSaleListVm
        {
            Search = search,
            Items = items
        };

        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> Setup(string? search, CancellationToken cancellationToken)
    {
        var currentShopId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
        var q = _context.Products
            .AsNoTracking()
            .Where(p => p.ShopId == currentShopId && !p.IsDeleted);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            q = q.Where(p => p.Name.Contains(term));
        }

        var products = await q
            .OrderByDescending(p => p.Id)
            .Take(100)
            .Select(p => new AdminFlashSaleProductItemVm
            {
                ProductId = p.Id,
                Name = p.Name,
                Status = p.Status,
                CreatedAt = p.CreatedAt,
                VariantCount = p.Variants.Count,
                PrimaryImageUrl = p.Images
                    .OrderByDescending(i => i.IsPrimary)
                    .ThenBy(i => i.SortOrder)
                    .Select(i => i.ImageUrl)
                    .FirstOrDefault(),
                MinPrice = p.Variants
                    .Where(v => !v.IsDeleted)
                    .Select(v => (decimal?)v.Price)
                    .Min() ?? 0m,
                MinSalePrice = p.Variants
                    .Where(v => !v.IsDeleted)
                    .Select(v => v.SalePrice)
                    .Min(),
                SaleStartDate = p.Variants
                    .Where(v => !v.IsDeleted)
                    .Select(v => v.SaleStartDate)
                    .Max(),
                SaleEndDate = p.Variants
                    .Where(v => !v.IsDeleted)
                    .Select(v => v.SaleEndDate)
                    .Max()
            })
            .ToListAsync(cancellationToken);

        foreach (var p in products)
        {
            p.PrimaryImageUrl = ImageUrlHelper.NormalizeStaticFileUrl(p.PrimaryImageUrl);
        }

        var vm = new AdminFlashSaleIndexVm
        {
            Search = search,
            Products = products
        };

        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken)
    {
        if (id <= 0) return NotFound();

        var product = await _context.Products
            .AsNoTracking()
            .Include(p => p.Variants)
                .ThenInclude(v => v.OptionValue1)
            .Include(p => p.Variants)
                .ThenInclude(v => v.OptionValue2)
            .Include(p => p.Images)
            .FirstOrDefaultAsync(p => !p.IsDeleted && p.Id == id, cancellationToken);

        if (product is null) return NotFound();

        var vm = new ProductAdminFlashSaleVm
        {
            ProductId = product.Id,
            ProductName = product.Name,
            Variants = product.Variants
                .Where(v => !v.IsDeleted && v.SalePrice != null && v.SaleStartDate != null && v.SaleEndDate != null)
                .OrderBy(v => v.Id)
                .Select(v => new ProductAdminFlashSaleVariantVm
                {
                    VariantId = v.Id,
                    VariantLabel = BuildVariantLabel(v),
                    Price = v.Price,
                    SalePrice = v.SalePrice,
                    SaleStartDate = v.SaleStartDate,
                    SaleEndDate = v.SaleEndDate,
                    IsOnSale = true,
                    IsActive = v.IsActive
                })
                .ToList()
        };

        return View("~/Areas/Admin/Views/FlashSales/Details.cshtml", vm);
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        if (id <= 0) return NotFound();

        var product = await _context.Products
            .AsNoTracking()
            .Include(p => p.Variants)
                .ThenInclude(v => v.OptionValue1)
            .Include(p => p.Variants)
                .ThenInclude(v => v.OptionValue2)
            .FirstOrDefaultAsync(p => !p.IsDeleted && p.Id == id, cancellationToken);

        if (product is null) return NotFound();

        var vm = new ProductAdminFlashSaleVm
        {
            ProductId = product.Id,
            ProductName = product.Name,
            Variants = product.Variants
                .Where(v => !v.IsDeleted)
                .OrderBy(v => v.Id)
                .Select(v => new ProductAdminFlashSaleVariantVm
                {
                    VariantId = v.Id,
                    VariantLabel = BuildVariantLabel(v),
                    Price = v.Price,
                    SalePrice = v.SalePrice,
                    SaleStartDate = v.SaleStartDate,
                    SaleEndDate = v.SaleEndDate,
                    IsOnSale = v.SalePrice != null || v.SaleStartDate != null || v.SaleEndDate != null,
                    IsActive = v.IsActive
                })
                .ToList()
        };

        return View("~/Areas/Admin/Views/FlashSales/Edit.cshtml", vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(ProductAdminFlashSaleVm model, CancellationToken cancellationToken)
    {
        if (model.ProductId <= 0) return NotFound();

        var selectedCount = model.Variants.Count(v => v.IsOnSale);

        // If a row is not selected, ignore any binding errors from its empty inputs.
        for (var i = 0; i < model.Variants.Count; i++)
        {
            if (model.Variants[i].IsOnSale) continue;
            ModelState.Remove($"Variants[{i}].SalePrice");
            ModelState.Remove($"Variants[{i}].SaleStartDate");
            ModelState.Remove($"Variants[{i}].SaleEndDate");
        }

        // Validate selected variants: Index page only counts a flash sale when SalePrice + Start + End are all set.
        for (var i = 0; i < model.Variants.Count; i++)
        {
            var row = model.Variants[i];
            if (!row.IsOnSale) continue;

            if (!row.SalePrice.HasValue || row.SalePrice.Value <= 0)
            {
                ModelState.AddModelError(string.Empty, $"Biến thể #{row.VariantId}: Vui lòng nhập Giá sale hợp lệ.");
            }

            if (!row.SaleStartDate.HasValue)
            {
                ModelState.AddModelError(string.Empty, $"Biến thể #{row.VariantId}: Vui lòng chọn thời gian Bắt đầu.");
            }

            if (!row.SaleEndDate.HasValue)
            {
                ModelState.AddModelError(string.Empty, $"Biến thể #{row.VariantId}: Vui lòng chọn thời gian Kết thúc.");
            }

            if (row.SaleStartDate.HasValue && row.SaleEndDate.HasValue && row.SaleEndDate.Value <= row.SaleStartDate.Value)
            {
                ModelState.AddModelError(string.Empty, $"Biến thể #{row.VariantId}: Thời gian Kết thúc phải lớn hơn Bắt đầu.");
            }
        }

        if (!ModelState.IsValid)
        {
            return View("~/Areas/Admin/Views/FlashSales/Edit.cshtml", model);
        }

        var variants = await _context.ProductVariants
            .Where(v => v.ProductId == model.ProductId)
            .ToListAsync(cancellationToken);

        var byId = variants.ToDictionary(v => v.Id);

        foreach (var row in model.Variants)
        {
            if (!byId.TryGetValue(row.VariantId, out var variant)) continue;

            if (!row.IsOnSale)
            {
                variant.SalePrice = null;
                variant.SaleStartDate = null;
                variant.SaleEndDate = null;
            }
            else
            {
                variant.SalePrice = row.SalePrice;
                variant.SaleStartDate = row.SaleStartDate;
                variant.SaleEndDate = row.SaleEndDate;
            }

            variant.UpdatedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync(cancellationToken);

        var configuredCount = await _context.ProductVariants
            .AsNoTracking()
            .Where(v => v.ProductId == model.ProductId && !v.IsDeleted)
            .CountAsync(v => v.SalePrice != null && v.SaleStartDate != null && v.SaleEndDate != null, cancellationToken);

        TempData["AdminToast"] = $"Đã cập nhật Flash Sale ({selectedCount} biến thể được chọn, {configuredCount} biến thể đủ điều kiện hiển thị ở danh sách Flash Sale).";
        return RedirectToAction(nameof(Edit), new { id = model.ProductId });
    }

    [HttpGet]
    public async Task<IActionResult> GetVariants(int productId, CancellationToken cancellationToken)
    {
        if (productId <= 0)
        {
            return BadRequest(new { message = "ProductId không hợp lệ." });
        }

        var exists = await _context.Products
            .AsNoTracking()
            .AnyAsync(p => !p.IsDeleted && p.Id == productId, cancellationToken);

        if (!exists)
        {
            return NotFound(new { message = "Không tìm thấy sản phẩm." });
        }

        var items = await _context.ProductVariants
            .AsNoTracking()
            .Where(v => !v.IsDeleted && v.ProductId == productId)
            .OrderByDescending(v => v.IsDefault)
            .ThenBy(v => v.Id)
            .Select(v => new
            {
                id = v.Id,
                sku = v.Sku,
                price = v.Price,
                isActive = v.IsActive,
                color = v.OptionValue1 != null ? v.OptionValue1.Value : null,
                size = v.OptionValue2 != null ? v.OptionValue2.Value : null
            })
            .ToListAsync(cancellationToken);

        var shaped = items.Select(v =>
        {
            var parts = new[] { v.color, v.size }.Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
            return new
            {
                v.id,
                v.sku,
                v.price,
                v.isActive,
                label = parts.Length > 0 ? string.Join(" - ", parts) : "Mặc định"
            };
        });

        return Json(shaped);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelEvent(CancellationToken cancellationToken)
    {
        var variants = await _context.ProductVariants
            .Where(v => !v.IsDeleted)
            .Where(v => v.SalePrice != null || v.SaleStartDate != null || v.SaleEndDate != null)
            .ToListAsync(cancellationToken);

        if (variants.Count == 0)
        {
            TempData["AdminToast"] = "Hiện không có FlashSale nào để hủy.";
            return RedirectToAction(nameof(Index));
        }

        foreach (var v in variants)
        {
            v.SalePrice = null;
            v.SaleStartDate = null;
            v.SaleEndDate = null;
            v.UpdatedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync(cancellationToken);
        TempData["AdminToast"] = "Đã hủy sự kiện FlashSale.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelProduct(int productId, string? search, CancellationToken cancellationToken)
    {
        if (productId <= 0)
        {
            TempData["AdminToast"] = "Sản phẩm không hợp lệ.";
            return RedirectToAction(nameof(Index), new { search });
        }

        var variants = await _context.ProductVariants
            .Where(v => !v.IsDeleted)
            .Where(v => v.ProductId == productId)
            .Where(v => v.SalePrice != null || v.SaleStartDate != null || v.SaleEndDate != null)
            .ToListAsync(cancellationToken);

        if (variants.Count == 0)
        {
            TempData["AdminToast"] = "Sản phẩm này hiện không có FlashSale để hủy.";
            return RedirectToAction(nameof(Index), new { search });
        }

        foreach (var v in variants)
        {
            v.SalePrice = null;
            v.SaleStartDate = null;
            v.SaleEndDate = null;
            v.UpdatedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync(cancellationToken);
        TempData["AdminToast"] = "Đã hủy FlashSale của sản phẩm.";
        return RedirectToAction(nameof(Index), new { search });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Apply(AdminFlashSaleApplyVm model, CancellationToken cancellationToken)
    {
        model.Scope = (model.Scope ?? string.Empty).Trim().ToLowerInvariant();
        model.Action = (model.Action ?? string.Empty).Trim().ToLowerInvariant();
        model.PricingMode = (model.PricingMode ?? string.Empty).Trim().ToLowerInvariant();

        if (model.PricingMode != "bulk" && model.PricingMode != "pervariant")
        {
            model.PricingMode = "bulk";
        }

        Dictionary<int, (string Mode, HashSet<int> VariantIds)> variantSelectionByProduct = new();
        if (!string.IsNullOrWhiteSpace(model.VariantSelectionJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(model.VariantSelectionJson);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        if (!int.TryParse(prop.Name, out var pid) || pid <= 0)
                        {
                            continue;
                        }

                        var mode = "all";
                        var variantIds = new HashSet<int>();

                        if (prop.Value.ValueKind == JsonValueKind.Object)
                        {
                            if (prop.Value.TryGetProperty("mode", out var modeEl) && modeEl.ValueKind == JsonValueKind.String)
                            {
                                mode = (modeEl.GetString() ?? "all").Trim().ToLowerInvariant();
                            }

                            if (prop.Value.TryGetProperty("variantIds", out var idsEl) && idsEl.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var idEl in idsEl.EnumerateArray())
                                {
                                    if (idEl.ValueKind == JsonValueKind.Number && idEl.TryGetInt32(out var vid) && vid > 0)
                                    {
                                        variantIds.Add(vid);
                                    }
                                }
                            }
                        }

                        if (mode != "selected")
                        {
                            mode = "all";
                        }

                        variantSelectionByProduct[pid] = (mode, variantIds);
                    }
                }
            }
            catch
            {
                // Ignore invalid JSON; default behavior = all variants
            }
        }

        Dictionary<int, (int DiscountPercent, DateTime Start, DateTime End)> perVariantConfig = new();
        if (model.PricingMode == "pervariant" && !string.IsNullOrWhiteSpace(model.PerVariantConfigJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(model.PerVariantConfigJson);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        if (!int.TryParse(prop.Name, out var variantId) || variantId <= 0)
                        {
                            continue;
                        }

                        if (prop.Value.ValueKind != JsonValueKind.Object)
                        {
                            continue;
                        }

                        var pct = 0;
                        if (prop.Value.TryGetProperty("discountPercent", out var pctEl))
                        {
                            if (pctEl.ValueKind == JsonValueKind.Number)
                            {
                                pctEl.TryGetInt32(out pct);
                            }
                            else if (pctEl.ValueKind == JsonValueKind.String)
                            {
                                int.TryParse(pctEl.GetString(), out pct);
                            }
                        }

                        var startStr = prop.Value.TryGetProperty("saleStartDate", out var sEl) && sEl.ValueKind == JsonValueKind.String
                            ? sEl.GetString()
                            : null;
                        var endStr = prop.Value.TryGetProperty("saleEndDate", out var eEl) && eEl.ValueKind == JsonValueKind.String
                            ? eEl.GetString()
                            : null;

                        if (string.IsNullOrWhiteSpace(startStr) || string.IsNullOrWhiteSpace(endStr))
                        {
                            continue;
                        }

                        static bool TryParseDtLocal(string input, out DateTime dt)
                        {
                            // from <input type="datetime-local">: "yyyy-MM-ddTHH:mm" (sometimes with seconds)
                            var formats = new[] { "yyyy-MM-dd'T'HH:mm", "yyyy-MM-dd'T'HH:mm:ss" };
                            return DateTime.TryParseExact(input, formats, CultureInfo.InvariantCulture,
                                DateTimeStyles.AllowWhiteSpaces, out dt);
                        }

                        if (!TryParseDtLocal(startStr!, out var start) || !TryParseDtLocal(endStr!, out var end))
                        {
                            continue;
                        }

                        perVariantConfig[variantId] = (pct, start, end);
                    }
                }
            }
            catch
            {
                // Ignore invalid JSON
            }
        }

        if (model.Scope != "all" && model.Scope != "selected" && model.Scope != "single")
        {
            ModelState.AddModelError(nameof(AdminFlashSaleApplyVm.Scope), "Phạm vi không hợp lệ.");
        }

        if (model.Action != "apply" && model.Action != "clear")
        {
            ModelState.AddModelError(nameof(AdminFlashSaleApplyVm.Action), "Thao tác không hợp lệ.");
        }

        var productIds = new List<int>();

        if (model.Scope == "all")
        {
            productIds = await _context.Products
                .AsNoTracking()
                .Where(p => !p.IsDeleted)
                .Select(p => p.Id)
                .ToListAsync(cancellationToken);
        }
        else
        {
            productIds = (model.ProductIds ?? new List<int>()).Where(id => id > 0).Distinct().ToList();
            if (productIds.Count == 0)
            {
                ModelState.AddModelError(nameof(AdminFlashSaleApplyVm.ProductIds), "Vui lòng chọn ít nhất 1 sản phẩm.");
            }

            if (model.Scope == "single" && productIds.Count != 1)
            {
                ModelState.AddModelError(nameof(AdminFlashSaleApplyVm.ProductIds), "Vui lòng chọn đúng 1 sản phẩm.");
            }
        }

        if (model.Action == "apply")
        {
            if (!TryValidateModel(model))
            {
                TempData["AdminToast"] = "Dữ liệu FlashSale không hợp lệ.";
                return RedirectToAction(nameof(Setup));
            }

            if (model.PricingMode == "bulk")
            {
                if (!model.SaleStartDate.HasValue || !model.SaleEndDate.HasValue)
                {
                    TempData["AdminToast"] = "Vui lòng chọn đủ thời gian bắt đầu/kết thúc.";
                    return RedirectToAction(nameof(Setup));
                }

                if (model.SaleEndDate.Value <= model.SaleStartDate.Value)
                {
                    TempData["AdminToast"] = "Thời gian kết thúc phải lớn hơn thời gian bắt đầu.";
                    return RedirectToAction(nameof(Setup));
                }
            }
        }

        if (!ModelState.IsValid)
        {
            TempData["AdminToast"] = "Dữ liệu FlashSale không hợp lệ.";
            return RedirectToAction(nameof(Setup));
        }

        var variants = await _context.ProductVariants
            .Where(v => !v.IsDeleted)
            .Where(v => productIds.Contains(v.ProductId))
            .ToListAsync(cancellationToken);

        if (variants.Count == 0)
        {
            TempData["AdminToast"] = "Không có biến thể để áp dụng.";
            return RedirectToAction(nameof(Setup));
        }

        // Partition by product for variant-level selection.
        var variantsByProduct = variants
            .GroupBy(v => v.ProductId)
            .ToDictionary(g => g.Key, g => g.ToList());

        static (string Mode, HashSet<int> VariantIds) GetSelection(Dictionary<int, (string Mode, HashSet<int> VariantIds)> map, int productId)
        {
            return map.TryGetValue(productId, out var sel) ? sel : ("all", new HashSet<int>());
        }

        if (model.Action == "clear")
        {
            foreach (var pid in productIds)
            {
                if (!variantsByProduct.TryGetValue(pid, out var list) || list.Count == 0)
                {
                    continue;
                }

                // Scope=all => always clear all variants.
                var (mode, variantIds) = model.Scope == "all" ? ("all", new HashSet<int>()) : GetSelection(variantSelectionByProduct, pid);
                var targets = mode == "selected" && variantIds.Count > 0
                    ? list.Where(v => variantIds.Contains(v.Id))
                    : list;

                foreach (var v in targets)
                {
                    v.SalePrice = null;
                    v.SaleStartDate = null;
                    v.SaleEndDate = null;
                    v.UpdatedAt = DateTime.UtcNow;
                }
            }

            await _context.SaveChangesAsync(cancellationToken);
            TempData["AdminToast"] = "Đã hủy FlashSale.";
            return RedirectToAction(nameof(Index));
        }

        var percent = Math.Clamp(model.DiscountPercent, 1, 90);

        foreach (var pid in productIds)
        {
            if (!variantsByProduct.TryGetValue(pid, out var list) || list.Count == 0)
            {
                continue;
            }

            var (mode, variantIds) = model.Scope == "all" ? ("all", new HashSet<int>()) : GetSelection(variantSelectionByProduct, pid);

            // Per-variant pricing is allowed when user targets 2+ variants.
            // (UI already controls availability; server still validates config presence.)
            var effectivePricingMode = model.PricingMode;

            if (mode == "selected" && variantIds.Count == 0)
            {
                TempData["AdminToast"] = "Vui lòng chọn ít nhất 1 biến thể cho sản phẩm đã chọn.";
                return RedirectToAction(nameof(Setup));
            }

            var targets = mode == "selected"
                ? list.Where(v => variantIds.Contains(v.Id)).ToList()
                : list;

            // If choosing selected variants, clear sale for the remaining variants of that product.
            if (mode == "selected")
            {
                foreach (var v in list)
                {
                    if (variantIds.Contains(v.Id))
                    {
                        continue;
                    }

                    v.SalePrice = null;
                    v.SaleStartDate = null;
                    v.SaleEndDate = null;
                    v.UpdatedAt = DateTime.UtcNow;
                }
            }

            foreach (var v in targets)
            {
                var start = model.SaleStartDate;
                var end = model.SaleEndDate;
                var pct = percent;

                if (effectivePricingMode == "pervariant")
                {
                    if (!perVariantConfig.TryGetValue(v.Id, out var cfg))
                    {
                        TempData["AdminToast"] = "Thiếu cấu hình giảm giá/thời gian cho một số biến thể.";
                        return RedirectToAction(nameof(Setup));
                    }

                    pct = Math.Clamp(cfg.DiscountPercent, 1, 90);
                    start = cfg.Start;
                    end = cfg.End;

                    if (end <= start)
                    {
                        TempData["AdminToast"] = "Thời gian kết thúc phải lớn hơn thời gian bắt đầu (theo từng biến thể).";
                        return RedirectToAction(nameof(Setup));
                    }
                }

                if (!start.HasValue || !end.HasValue)
                {
                    TempData["AdminToast"] = "Vui lòng chọn đủ thời gian bắt đầu/kết thúc.";
                    return RedirectToAction(nameof(Setup));
                }

                if (v.Price <= 0m)
                {
                    v.SalePrice = null;
                    v.SaleStartDate = start;
                    v.SaleEndDate = end;
                    v.UpdatedAt = DateTime.UtcNow;
                    continue;
                }

                var sale = v.Price * (100m - pct) / 100m;
                // VND: round to whole currency
                sale = Math.Round(sale, 0, MidpointRounding.AwayFromZero);

                if (sale <= 0m || sale >= v.Price)
                {
                    v.SalePrice = null;
                }
                else
                {
                    v.SalePrice = sale;
                }

                v.SaleStartDate = start;
                v.SaleEndDate = end;
                v.UpdatedAt = DateTime.UtcNow;
            }
        }

        await _context.SaveChangesAsync(cancellationToken);
        TempData["AdminToast"] = model.Scope == "all"
            ? "Đã áp dụng FlashSale toàn shop."
            : (model.Scope == "single" ? "Đã áp dụng FlashSale cho sản phẩm." : "Đã áp dụng FlashSale cho các sản phẩm đã chọn.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RestartProduct(int productId, DateTime startDate, DateTime endDate, CancellationToken cancellationToken)
    {
        if (productId <= 0) return BadRequest();

        if (endDate <= startDate)
        {
            TempData["AdminToast"] = "Thời gian kết thúc phải lớn hơn thời gian bắt đầu.";
            return RedirectToAction(nameof(Setup), new { restartProductId = productId });
        }

        var variants = await _context.ProductVariants
            .Where(v => !v.IsDeleted && v.ProductId == productId)
            .Where(v => v.SalePrice != null)
            .ToListAsync(cancellationToken);

        if (variants.Count == 0)
        {
            TempData["AdminToast"] = "Sản phẩm này không có biến thể nào có mức giảm giá đã được thiết lập. Vui lòng thiết lập mới.";
            return RedirectToAction(nameof(Setup));
        }

        foreach (var v in variants)
        {
            v.SaleStartDate = startDate;
            v.SaleEndDate = endDate;
            v.UpdatedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync(cancellationToken);
        TempData["AdminToast"] = "Đã khởi động lại sự kiện Flash Sale cho sản phẩm.";
        return RedirectToAction(nameof(Index));
    }
}
