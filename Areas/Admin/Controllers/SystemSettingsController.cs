using DOAN_BANHANG.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DOAN_BANHANG.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = "Admin")]
public class SystemSettingsController : Controller
{
    private readonly DataContext _context;

    public SystemSettingsController(DataContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> Index()
    {
        var settings = await _context.SystemSettings.ToListAsync();
        
        // Seed default values if not exists
        if (!settings.Any())
        {
            var defaultSettings = new List<SystemSetting>
            {
                new SystemSetting { Id = "Tax_VAT_Default", Value = "8", Description = "Mức thuế VAT mặc định", Type = "percent" },
                new SystemSetting { Id = "Tax_Seller_Threshold", Value = "500000000", Description = "Ngưỡng doanh thu chịu thuế", Type = "money" },
                new SystemSetting { Id = "Tax_Seller_Rate", Value = "1.5", Description = "Mức thuế áp dụng cho hộ KD > 500 triệu", Type = "percent" },
                new SystemSetting { Id = "Fee_Commission_Fashion", Value = "12", Description = "Chiết khấu cố định — Thời trang", Type = "percent" },
                new SystemSetting { Id = "Fee_Commission_Cosmetics", Value = "10", Description = "Chiết khấu cố định — Mỹ phẩm", Type = "percent" },
                new SystemSetting { Id = "Fee_Marketing", Value = "6", Description = "Phí Marketing chung (Freeship / Voucher Xtra)", Type = "percent" }
            };

            _context.SystemSettings.AddRange(defaultSettings);
            await _context.SaveChangesAsync();
            settings = defaultSettings;
        }
        else
        {
            bool needsUpdate = false;
            
            // Xóa Phí thanh toán nếu còn tồn tại
            var paymentFee = settings.FirstOrDefault(s => s.Id == "Fee_Payment");
            if (paymentFee != null)
            {
                _context.SystemSettings.Remove(paymentFee);
                settings.Remove(paymentFee);
                needsUpdate = true;
            }

            foreach (var setting in settings)
            {
                if (setting.Type == "number" || setting.Description!.Contains("VD: 0."))
                {
                    needsUpdate = true;
                    if (setting.Id == "Tax_Seller_Threshold")
                    {
                        setting.Type = "money";
                        setting.Description = "Ngưỡng doanh thu chịu thuế";
                    }
                    else
                    {
                        setting.Type = "percent";
                        // Chuyển 0.08 -> 8
                        var valStr = setting.Value.Replace(",", ".");
                        if (double.TryParse(valStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double val))
                        {
                            if (val <= 1.0)
                            {
                                setting.Value = (val * 100).ToString("G", System.Globalization.CultureInfo.InvariantCulture);
                            }
                        }

                        // Cập nhật lại Description cho gọn
                        if (setting.Id == "Tax_VAT_Default") setting.Description = "Mức thuế VAT mặc định";
                        if (setting.Id == "Tax_Seller_Rate") setting.Description = "Mức thuế áp dụng cho hộ KD > 500 triệu";
                        if (setting.Id == "Fee_Payment") setting.Description = "Phí thanh toán";
                        if (setting.Id == "Fee_Commission_Fashion") setting.Description = "Chiết khấu cố định — Thời trang";
                        if (setting.Id == "Fee_Commission_Cosmetics") setting.Description = "Chiết khấu cố định — Mỹ phẩm";
                        if (setting.Id == "Fee_Marketing") setting.Description = "Phí Marketing chung (Freeship / Voucher Xtra)";
                    }
                }
            }
            if (needsUpdate)
            {
                await _context.SaveChangesAsync();
            }
        }

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
                existing.Value = newValue;
                existing.UpdatedAt = DateTime.UtcNow;
            }
        }

        await _context.SaveChangesAsync();
        TempData["SuccessMessage"] = "Cập nhật cấu hình thành công.";
        return RedirectToAction(nameof(Index));
    }
}
