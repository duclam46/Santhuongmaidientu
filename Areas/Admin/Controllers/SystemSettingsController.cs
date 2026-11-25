using DOAN_BANHANG.Models;
using DOAN_BANHANG.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DOAN_BANHANG.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = "Admin")]
public class SystemSettingsController : Controller
{
    private readonly DataContext _context;
    private readonly AuditLogService _audit;

    public SystemSettingsController(DataContext context, AuditLogService audit)
    {
        _context = context;
        _audit = audit;
    }

    public async Task<IActionResult> Index()
    {
        var settings = await _context.SystemSettings.ToListAsync();

        var requiredDefaults = new List<SystemSetting>
        {
            new() { Id = "Tax_VAT_Default", Value = "8", Description = "Mức thuế GTGT (VAT) mặc định xuất hóa đơn", Type = "percent" },
            new() { Id = "Tax_Seller_Threshold", Value = "500000000", Description = "Ngưỡng doanh thu chịu thuế cá nhân / hộ kinh doanh", Type = "money" },
            new() { Id = "Tax_Seller_Rate", Value = "1.5", Description = "Thuế khấu trừ tại nguồn cho Shop Thường (Cá nhân / Hộ KD)", Type = "percent" },
            new() { Id = "Fee_Commission_Default", Value = "10", Description = "Chiết khấu cố định mặc định (khi ngành hàng chưa có phí riêng)", Type = "percent" },
            new() { Id = "Fee_Mall_Surcharge", Value = "2", Description = "Phụ phí dịch vụ cố định Sellzy Mall", Type = "percent" },
            new() { Id = "Fee_Marketing", Value = "6", Description = "Phí Marketing / Chương trình Freeship & Voucher Xtra", Type = "percent" }
        };

        bool needsUpdate = false;

        // Xóa các key cũ gán cứng theo Thời trang / Mỹ phẩm / Phí thanh toán
        var deprecatedKeys = settings
            .Where(s => s.Id == "Fee_Payment" || s.Id == "Fee_Commission_Fashion" || s.Id == "Fee_Commission_Cosmetics")
            .ToList();

        if (deprecatedKeys.Count > 0)
        {
            _context.SystemSettings.RemoveRange(deprecatedKeys);
            foreach (var dk in deprecatedKeys) settings.Remove(dk);
            needsUpdate = true;
        }

        foreach (var def in requiredDefaults)
        {
            var existing = settings.FirstOrDefault(s => s.Id == def.Id);
            if (existing == null)
            {
                def.UpdatedAt = DateTime.UtcNow;
                _context.SystemSettings.Add(def);
                settings.Add(def);
                needsUpdate = true;
            }
            else if (existing.Id == "Fee_Mall_Surcharge" && existing.Description != def.Description)
            {
                existing.Description = def.Description;
                existing.UpdatedAt = DateTime.UtcNow;
                needsUpdate = true;
            }
        }

        if (needsUpdate)
        {
            await _context.SaveChangesAsync();
        }

        var rootCategories = await _context.Categories
            .AsNoTracking()
            .Where(c => c.IsActive && !c.IsDeleted && c.ParentId == null)
            .OrderBy(c => c.Id)
            .ToListAsync();

        ViewBag.RootCategories = rootCategories;

        return View(settings);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Update(Dictionary<string, string> settings)
    {
        var existingSettings = await _context.SystemSettings.ToListAsync();

        foreach (var existing in existingSettings)
        {
            if (settings.TryGetValue(existing.Id, out var newValue))
            {
                existing.Value = (newValue ?? string.Empty).Trim();
                existing.UpdatedAt = DateTime.UtcNow;
            }
        }

        await _context.SaveChangesAsync();
        await _audit.LogAsync(HttpContext, "UpdateSystemSettings", "SystemSetting", "Global",
            string.Join("; ", settings.Select(kv => $"{kv.Key}={kv.Value}")));
        TempData["SuccessMessage"] = "Cập nhật cấu hình tài chính, thuế và biểu phí thành công.";
        return RedirectToAction(nameof(Index));
    }
}
