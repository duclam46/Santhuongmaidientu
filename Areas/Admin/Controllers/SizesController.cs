using DOAN_BANHANG.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DOAN_BANHANG.Areas.Admin.Controllers;

[Area("Admin")]
[Route("Admin/[controller]/[action]")]
[Authorize(Roles = "Admin")]
public class SizesController : Controller
{
    private readonly DataContext _db;

    public SizesController(DataContext db)
    {
        _db = db;
    }

    public async Task<IActionResult> Index()
    {
        var sizes = await _db.Sizes
            .OrderBy(s => s.SortOrder)
            .ThenByDescending(s => s.CreatedAt)
            .ToListAsync();
        return View(sizes);
    }

    [HttpGet]
    public IActionResult Create()
    {
        return View(new Size());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Size size)
    {
        if (ModelState.IsValid)
        {
            size.CreatedAt = DateTime.UtcNow;
            _db.Sizes.Add(size);
            await _db.SaveChangesAsync();
            TempData["AdminToast"] = "Thêm kích thước thành công!";
            return RedirectToAction(nameof(Index));
        }
        return View(size);
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var size = await _db.Sizes.FindAsync(id);
        if (size == null) return NotFound();
        return View(size);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Size size)
    {
        if (ModelState.IsValid)
        {
            var existing = await _db.Sizes.FindAsync(size.Id);
            if (existing == null) return NotFound();

            existing.Name = size.Name;
            existing.SortOrder = size.SortOrder;
            
            await _db.SaveChangesAsync();
            TempData["AdminToast"] = "Cập nhật kích thước thành công!";
            return RedirectToAction(nameof(Index));
        }
        return View(size);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var size = await _db.Sizes.FindAsync(id);
        if (size == null) return NotFound();

        // Check if there are variants using this size
        var hasVariants = await _db.ProductVariants.AnyAsync(v => v.SizeId == id && !v.IsDeleted);
        if (hasVariants)
        {
            TempData["AdminError"] = "Không thể xóa kích thước đang được sử dụng trong sản phẩm!";
            return RedirectToAction(nameof(Index));
        }

        _db.Sizes.Remove(size);
        await _db.SaveChangesAsync();
        TempData["AdminToast"] = "Xóa kích thước thành công!";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> UpdateOrder([FromBody] List<int> ids)
    {
        if (ids == null || !ids.Any()) return BadRequest();

        var sizes = await _db.Sizes.Where(s => ids.Contains(s.Id)).ToListAsync();
        for (int i = 0; i < ids.Count; i++)
        {
            var size = sizes.FirstOrDefault(s => s.Id == ids[i]);
            if (size != null)
            {
                size.SortOrder = i + 1;
            }
        }

        await _db.SaveChangesAsync();
        return Ok();
    }
}
