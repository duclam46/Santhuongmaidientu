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

    public CategoriesController(DataContext db)
    {
        _db = db;
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

    public async Task<IActionResult> Index()
    {
        var categories = await _db.Categories
            .Include(c => c.Parent)
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
        return View(new Category());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Category category)
    {
        ModelState.Remove("Slug");
        ModelState.Remove("Parent");
        ModelState.Remove("Children");

        if (ModelState.IsValid)
        {
            category.Slug = GenerateSlug(category.Name);
            var baseSlug = category.Slug;
            var counter = 1;
            while (await _db.Categories.AnyAsync(c => c.Slug == category.Slug))
            {
                category.Slug = $"{baseSlug}-{counter++}";
            }

            category.CreatedAt = DateTime.UtcNow;
            category.UpdatedAt = DateTime.UtcNow;
            _db.Categories.Add(category);
            await _db.SaveChangesAsync();
            TempData["AdminToast"] = "Tạo danh mục thành công!";
            return RedirectToAction(nameof(Index));
        }

        ViewBag.AllCategories = await _db.Categories.Where(c => !c.IsDeleted).OrderBy(c => c.SortOrder).ToListAsync();
        ViewBag.Parents = await GetHierarchicalCategories();
        return View(category);
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var category = await _db.Categories.FindAsync(id);
        if (category == null || category.IsDeleted) return NotFound();

        ViewBag.AllCategories = await _db.Categories.Where(c => !c.IsDeleted).OrderBy(c => c.SortOrder).ToListAsync();
        ViewBag.Parents = await GetHierarchicalCategories(category.Id);
        return View(category);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Category category)
    {
        ModelState.Remove("Slug");
        ModelState.Remove("Parent");
        ModelState.Remove("Children");

        if (ModelState.IsValid)
        {
            var existing = await _db.Categories.FindAsync(category.Id);
            if (existing == null || existing.IsDeleted) return NotFound();

            existing.Name = category.Name;
            existing.ParentId = category.ParentId;
            existing.SortOrder = category.SortOrder;
            existing.IsActive = category.IsActive;
            existing.UpdatedAt = DateTime.UtcNow;

            existing.Slug = GenerateSlug(category.Name);
            var baseSlug = existing.Slug;
            var counter = 1;
            while (await _db.Categories.AnyAsync(c => c.Slug == existing.Slug && c.Id != existing.Id))
            {
                existing.Slug = $"{baseSlug}-{counter++}";
            }

            await _db.SaveChangesAsync();
            TempData["AdminToast"] = "Cập nhật danh mục thành công!";
            return RedirectToAction(nameof(Index));
        }

        ViewBag.AllCategories = await _db.Categories.Where(c => !c.IsDeleted).OrderBy(c => c.SortOrder).ToListAsync();
        ViewBag.Parents = await GetHierarchicalCategories(category.Id);
        return View(category);
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

        // 1. Move child categories to parent
        var children = await _db.Categories.Where(c => c.ParentId == id && !c.IsDeleted).ToListAsync();
        foreach (var child in children)
        {
            child.ParentId = category.ParentId;
        }

        // 2. Handle products
        var hasProducts = await _db.Products.AnyAsync(p => p.CategoryId == id && !p.IsDeleted);
        if (hasProducts)
        {
            if (category.ParentId == null)
            {
                // Cannot move products to null because CategoryId is required
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
