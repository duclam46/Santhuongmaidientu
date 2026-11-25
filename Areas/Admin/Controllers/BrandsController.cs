using DOAN_BANHANG.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

namespace DOAN_BANHANG.Areas.Admin.Controllers;

[Area("Admin")]
[Route("Admin/[controller]/[action]")]
[Authorize(Roles = "Admin")]
public class BrandsController : Controller
{
    private readonly DataContext _db;
    private readonly IWebHostEnvironment _env;

    public BrandsController(DataContext db, IWebHostEnvironment env)
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
        return string.IsNullOrWhiteSpace(slug) ? "brand" : slug;
    }

    private async Task<string?> SaveBrandLogoAsync(IFormFile? logoFile)
    {
        if (logoFile == null || logoFile.Length == 0) return null;

        var uploadDir = Path.Combine(_env.WebRootPath, "uploads", "brands");
        if (!Directory.Exists(uploadDir)) Directory.CreateDirectory(uploadDir);

        var ext = Path.GetExtension(logoFile.FileName);
        var fileName = $"brand_{Guid.NewGuid():N}{ext}";
        var filePath = Path.Combine(uploadDir, fileName);

        await using var stream = new FileStream(filePath, FileMode.Create);
        await logoFile.CopyToAsync(stream);

        return $"/uploads/brands/{fileName}";
    }

    [HttpGet]
    [HttpGet("/Admin/Brands")]
    public async Task<IActionResult> Index(string? search)
    {
        var query = _db.Brands.AsNoTracking().Include(b => b.Products).AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(b => b.Name.Contains(term) || b.Slug.Contains(term));
        }

        var brands = await query.OrderBy(b => b.Name).ToListAsync();
        ViewBag.Search = search;
        return View(brands);
    }

    [HttpGet]
    public IActionResult Create()
    {
        return View(new Brand { IsActive = true });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Brand brand, IFormFile? logoFile)
    {
        ModelState.Remove("Slug");
        ModelState.Remove("Products");

        if (ModelState.IsValid)
        {
            brand.Slug = GenerateSlug(brand.Name);
            var baseSlug = brand.Slug;
            var counter = 1;
            while (await _db.Brands.AnyAsync(b => b.Slug == brand.Slug))
            {
                brand.Slug = $"{baseSlug}-{counter++}";
            }

            var uploadedLogo = await SaveBrandLogoAsync(logoFile);
            if (!string.IsNullOrWhiteSpace(uploadedLogo))
            {
                brand.LogoUrl = uploadedLogo;
            }

            brand.CreatedAt = DateTime.UtcNow;
            _db.Brands.Add(brand);
            await _db.SaveChangesAsync();
            TempData["AdminToast"] = $"Đã tạo thương hiệu \"{brand.Name}\" thành công!";
            return RedirectToAction(nameof(Index));
        }

        return View(brand);
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var brand = await _db.Brands.FindAsync(id);
        if (brand == null) return NotFound();
        return View(brand);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Brand brand, IFormFile? logoFile)
    {
        ModelState.Remove("Slug");
        ModelState.Remove("Products");

        if (ModelState.IsValid)
        {
            var existing = await _db.Brands.FindAsync(brand.Id);
            if (existing == null) return NotFound();

            existing.Name = brand.Name.Trim();
            existing.IsActive = brand.IsActive;

            var uploadedLogo = await SaveBrandLogoAsync(logoFile);
            if (!string.IsNullOrWhiteSpace(uploadedLogo))
            {
                existing.LogoUrl = uploadedLogo;
            }
            else if (!string.IsNullOrWhiteSpace(brand.LogoUrl))
            {
                existing.LogoUrl = brand.LogoUrl.Trim();
            }

            existing.Slug = GenerateSlug(brand.Name);
            var baseSlug = existing.Slug;
            var counter = 1;
            while (await _db.Brands.AnyAsync(b => b.Slug == existing.Slug && b.Id != existing.Id))
            {
                existing.Slug = $"{baseSlug}-{counter++}";
            }

            await _db.SaveChangesAsync();
            TempData["AdminToast"] = $"Cập nhật thương hiệu \"{existing.Name}\" thành công!";
            return RedirectToAction(nameof(Index));
        }

        return View(brand);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var brand = await _db.Brands.FindAsync(id);
        if (brand == null) return NotFound();

        var productsWithBrand = await _db.Products.Where(p => p.BrandId == id).ToListAsync();
        foreach (var p in productsWithBrand)
        {
            p.BrandId = null;
        }

        _db.Brands.Remove(brand);
        await _db.SaveChangesAsync();
        TempData["AdminToast"] = $"Đã xóa thương hiệu \"{brand.Name}\".";
        return RedirectToAction(nameof(Index));
    }
}
