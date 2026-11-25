using DOAN_BANHANG.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

namespace DOAN_BANHANG.Areas.Admin.Controllers;

[Area("Admin")]
[Route("Admin/[controller]/[action]")]
[Authorize(Roles = "Admin")]
public class CategoriesController : Controller
{
    private readonly DataContext _db;
    private readonly IWebHostEnvironment _env;

    public CategoriesController(DataContext db, IWebHostEnvironment env)
    {
        _db = db;
        _env = env;
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
        return slug;
    }

    private async Task<string?> SaveCategoryIconAsync(IFormFile? iconFile)
    {
        if (iconFile == null || iconFile.Length == 0) return null;

        var uploadDir = Path.Combine(_env.WebRootPath, "uploads", "categories");
        if (!Directory.Exists(uploadDir)) Directory.CreateDirectory(uploadDir);

        var ext = Path.GetExtension(iconFile.FileName);
        var fileName = $"cat_{Guid.NewGuid():N}{ext}";
        var filePath = Path.Combine(uploadDir, fileName);

        await using var stream = new FileStream(filePath, FileMode.Create);
        await iconFile.CopyToAsync(stream);

        return $"/uploads/categories/{fileName}";
    }

    [HttpGet]
    [HttpGet("/Admin/Categories")]
    public async Task<IActionResult> Index()
    {
        var categories = await _db.Categories
            .Include(c => c.Parent)
            .Include(c => c.Attributes)
            .Where(c => !c.IsDeleted)
            .OrderBy(c => c.ParentId)
            .ThenBy(c => c.SortOrder)
            .ToListAsync();
        return View(categories);
    }

    [HttpGet]
    public async Task<IActionResult> Create()
    {
        ViewBag.AllCategories = await _db.Categories.Where(c => !c.IsDeleted).OrderBy(c => c.SortOrder).ToListAsync();
        ViewBag.Parents = await GetHierarchicalCategories();
        return View(new Category { IsActive = true, IsReturnableDefault = true, HasSizeGuide = false });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Category category, IFormFile? iconFile)
    {
        ModelState.Remove("Slug");
        ModelState.Remove("Parent");
        ModelState.Remove("Children");
        ModelState.Remove("Attributes");

        if (ModelState.IsValid)
        {
            category.Slug = GenerateSlug(category.Name);
            var baseSlug = category.Slug;
            var counter = 1;
            while (await _db.Categories.AnyAsync(c => c.Slug == category.Slug))
            {
                category.Slug = $"{baseSlug}-{counter++}";
            }

            var uploadedIcon = await SaveCategoryIconAsync(iconFile);
            if (!string.IsNullOrWhiteSpace(uploadedIcon))
            {
                category.IconUrl = uploadedIcon;
            }

            category.CreatedAt = DateTime.UtcNow;
            category.UpdatedAt = DateTime.UtcNow;
            _db.Categories.Add(category);
            await _db.SaveChangesAsync();
            TempData["AdminToast"] = "Tạo danh mục ngành hàng thành công!";
            return RedirectToAction(nameof(Edit), new { id = category.Id });
        }

        ViewBag.AllCategories = await _db.Categories.Where(c => !c.IsDeleted).OrderBy(c => c.SortOrder).ToListAsync();
        ViewBag.Parents = await GetHierarchicalCategories();
        return View(category);
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var category = await _db.Categories
            .Include(c => c.Attributes.OrderBy(a => a.SortOrder))
            .Include(c => c.Parent)
            .FirstOrDefaultAsync(c => c.Id == id && !c.IsDeleted);

        if (category == null) return NotFound();

        ViewBag.AllCategories = await _db.Categories.Where(c => !c.IsDeleted).OrderBy(c => c.SortOrder).ToListAsync();
        ViewBag.Parents = await GetHierarchicalCategories(category.Id);

        // If parent has attributes, also show inherited attributes for reference
        if (category.ParentId.HasValue)
        {
            ViewBag.ParentAttributes = await _db.CategoryAttributes
                .AsNoTracking()
                .Where(a => a.CategoryId == category.ParentId.Value)
                .OrderBy(a => a.SortOrder)
                .ToListAsync();
        }

        return View(category);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Category category, IFormFile? iconFile)
    {
        ModelState.Remove("Slug");
        ModelState.Remove("Parent");
        ModelState.Remove("Children");
        ModelState.Remove("Attributes");

        if (ModelState.IsValid)
        {
            var existing = await _db.Categories.FindAsync(category.Id);
            if (existing == null || existing.IsDeleted) return NotFound();

            existing.Name = category.Name;
            existing.ParentId = category.ParentId;
            existing.CommissionRate = category.CommissionRate;
            existing.MaxCommissionAmount = category.MaxCommissionAmount;
            existing.HasSizeGuide = category.HasSizeGuide;
            existing.IsReturnableDefault = category.IsReturnableDefault;
            existing.SortOrder = category.SortOrder;
            existing.IsActive = category.IsActive;
            existing.UpdatedAt = DateTime.UtcNow;

            var uploadedIcon = await SaveCategoryIconAsync(iconFile);
            if (!string.IsNullOrWhiteSpace(uploadedIcon))
            {
                existing.IconUrl = uploadedIcon;
            }
            else if (!string.IsNullOrWhiteSpace(category.IconUrl))
            {
                existing.IconUrl = category.IconUrl.Trim();
            }

            existing.Slug = GenerateSlug(category.Name);
            var baseSlug = existing.Slug;
            var counter = 1;
            while (await _db.Categories.AnyAsync(c => c.Slug == existing.Slug && c.Id != existing.Id))
            {
                existing.Slug = $"{baseSlug}-{counter++}";
            }

            await _db.SaveChangesAsync();
            TempData["AdminToast"] = "Cập nhật danh mục thành công!";
            return RedirectToAction(nameof(Edit), new { id = existing.Id });
        }

        ViewBag.AllCategories = await _db.Categories.Where(c => !c.IsDeleted).OrderBy(c => c.SortOrder).ToListAsync();
        ViewBag.Parents = await GetHierarchicalCategories(category.Id);
        return View(category);
    }

    // ── CRUD CategoryAttributes (Thông số kỹ thuật theo Ngành) ──

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddAttribute(int categoryId, string name, string? unit, bool isFilterable = true, int sortOrder = 0)
    {
        if (categoryId <= 0 || string.IsNullOrWhiteSpace(name))
        {
            TempData["AdminError"] = "Vui lòng nhập tên thuộc tính kỹ thuật.";
            return RedirectToAction(nameof(Edit), new { id = categoryId });
        }

        var attr = new CategoryAttribute
        {
            CategoryId = categoryId,
            Name = name.Trim(),
            Unit = string.IsNullOrWhiteSpace(unit) ? null : unit.Trim(),
            IsFilterable = isFilterable,
            SortOrder = sortOrder
        };

        _db.CategoryAttributes.Add(attr);
        await _db.SaveChangesAsync();
        TempData["AdminToast"] = $"Đã thêm thuộc tính kỹ thuật \"{attr.Name}\".";
        return RedirectToAction(nameof(Edit), new { id = categoryId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateAttribute(int id, int categoryId, string name, string? unit, bool isFilterable = false, int sortOrder = 0)
    {
        var attr = await _db.CategoryAttributes.FirstOrDefaultAsync(a => a.Id == id && a.CategoryId == categoryId);
        if (attr == null) return RedirectToAction(nameof(Edit), new { id = categoryId });

        if (!string.IsNullOrWhiteSpace(name))
        {
            attr.Name = name.Trim();
            attr.Unit = string.IsNullOrWhiteSpace(unit) ? null : unit.Trim();
            attr.IsFilterable = isFilterable;
            attr.SortOrder = sortOrder;
            await _db.SaveChangesAsync();
            TempData["AdminToast"] = $"Đã cập nhật thuộc tính \"{attr.Name}\".";
        }

        return RedirectToAction(nameof(Edit), new { id = categoryId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteAttribute(int id, int categoryId)
    {
        var attr = await _db.CategoryAttributes.FirstOrDefaultAsync(a => a.Id == id && a.CategoryId == categoryId);
        if (attr != null)
        {
            var usedValues = await _db.ProductAttributeValues.Where(v => v.CategoryAttributeId == id).ToListAsync();
            if (usedValues.Count > 0)
            {
                _db.ProductAttributeValues.RemoveRange(usedValues);
            }
            _db.CategoryAttributes.Remove(attr);
            await _db.SaveChangesAsync();
            TempData["AdminToast"] = $"Đã xóa thuộc tính \"{attr.Name}\".";
        }

        return RedirectToAction(nameof(Edit), new { id = categoryId });
    }

    private async Task<List<Category>> GetHierarchicalCategories(int? excludeId = null)
    {
        var allCategories = await _db.Categories
            .Where(c => !c.IsDeleted)
            .OrderBy(c => c.SortOrder)
            .ToListAsync();

        var result = new List<Category>();
        BuildHierarchy(allCategories, null, 0, result, excludeId);
        return result;
    }

    private void BuildHierarchy(List<Category> all, int? parentId, int level, List<Category> result, int? excludeId)
    {
        var children = all.Where(c => c.ParentId == parentId && c.Id != excludeId).ToList();
        foreach (var child in children)
        {
            var prefix = new string('-', level * 2) + (level > 0 ? " " : "");
            var displayCategory = new Category
            {
                Id = child.Id,
                Name = prefix + child.Name
            };
            result.Add(displayCategory);
            BuildHierarchy(all, child.Id, level + 1, result, excludeId);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var category = await _db.Categories.FindAsync(id);
        if (category == null) return NotFound();

        var children = await _db.Categories.Where(c => c.ParentId == id && !c.IsDeleted).ToListAsync();
        foreach (var child in children)
        {
            child.ParentId = category.ParentId;
        }

        var hasProducts = await _db.Products.AnyAsync(p => p.CategoryId == id && !p.IsDeleted);
        if (hasProducts)
        {
            if (category.ParentId == null)
            {
                TempData["AdminError"] = "Không thể xóa danh mục gốc đang có sản phẩm. Vui lòng chuyển sản phẩm sang danh mục khác trước!";
                return RedirectToAction(nameof(Index));
            }

            var products = await _db.Products.Where(p => p.CategoryId == id && !p.IsDeleted).ToListAsync();
            foreach (var product in products)
            {
                product.CategoryId = category.ParentId.Value;
            }
            TempData["AdminToast"] = "Xóa danh mục thành công, các sản phẩm và danh mục con đã được chuyển lên cấp cha!";
        }
        else
        {
            TempData["AdminToast"] = "Xóa danh mục thành công!";
        }

        // Remove category attributes and values
        var attrIds = await _db.CategoryAttributes.Where(a => a.CategoryId == id).Select(a => a.Id).ToListAsync();
        if (attrIds.Count > 0)
        {
            var pVals = await _db.ProductAttributeValues.Where(v => attrIds.Contains(v.CategoryAttributeId)).ToListAsync();
            _db.ProductAttributeValues.RemoveRange(pVals);
        }

        _db.Categories.Remove(category);
        await _db.SaveChangesAsync();

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> UpdateOrder([FromBody] List<int> ids)
    {
        if (ids == null || !ids.Any()) return BadRequest();

        var categories = await _db.Categories.Where(c => ids.Contains(c.Id)).ToListAsync();
        for (int i = 0; i < ids.Count; i++)
        {
            var cat = categories.FirstOrDefault(c => c.Id == ids[i]);
            if (cat != null)
            {
                cat.SortOrder = i + 1;
            }
        }

        await _db.SaveChangesAsync();
        return Ok();
    }
}
