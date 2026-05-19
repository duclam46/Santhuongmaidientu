using DOAN_BANHANG.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DOAN_BANHANG.Areas.Admin.Controllers;

[Area("Admin")]
[Route("Admin/[controller]/[action]")]
[Authorize(Roles = "Admin")]
public class ColorsController : Controller
{
    private readonly DataContext _db;

    public ColorsController(DataContext db)
    {
        _db = db;
    }

    public async Task<IActionResult> Index()
    {
        var colors = await _db.Colors
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync();
        return View(colors);
    }

    [HttpGet]
    public IActionResult Create()
    {
        return View(new Color());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Color color)
    {
        if (ModelState.IsValid)
        {
            color.CreatedAt = DateTime.UtcNow;
            _db.Colors.Add(color);
            await _db.SaveChangesAsync();
            TempData["AdminToast"] = "Thêm màu sắc thành công!";
            return RedirectToAction(nameof(Index));
        }
        return View(color);
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var color = await _db.Colors.FindAsync(id);
        if (color == null) return NotFound();
        return View(color);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Color color)
    {
        if (ModelState.IsValid)
        {
            var existing = await _db.Colors.FindAsync(color.Id);
            if (existing == null) return NotFound();

            existing.Name = color.Name;
            existing.Code = color.Code;
            
            await _db.SaveChangesAsync();
            TempData["AdminToast"] = "Cập nhật màu sắc thành công!";
            return RedirectToAction(nameof(Index));
        }
        return View(color);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var color = await _db.Colors.FindAsync(id);
        if (color == null) return NotFound();

        // Check if there are variants using this color
        var hasVariants = await _db.ProductVariants.AnyAsync(v => v.ColorId == id && !v.IsDeleted);
        if (hasVariants)
        {
            TempData["AdminError"] = "Không thể xóa màu sắc đang được sử dụng trong sản phẩm!";
            return RedirectToAction(nameof(Index));
        }

        _db.Colors.Remove(color);
        await _db.SaveChangesAsync();
        TempData["AdminToast"] = "Xóa màu sắc thành công!";
        return RedirectToAction(nameof(Index));
    }
}
