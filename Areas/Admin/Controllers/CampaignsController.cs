using DOAN_BANHANG.Models;
using DOAN_BANHANG.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DOAN_BANHANG.Areas.Admin.Controllers;

[Area("Admin")]
[Route("Admin/[controller]/[action]")]
[Authorize(Roles = "Admin")]
public class CampaignsController : Controller
{
    private readonly DataContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly AuditLogService _audit;

    public CampaignsController(DataContext db, IWebHostEnvironment env, AuditLogService audit)
    {
        _db = db;
        _env = env;
        _audit = audit;
    }

    [HttpGet]
    [HttpGet("/Admin/Campaigns")]
    public async Task<IActionResult> Index(CancellationToken ct = default)
    {
        var banners = await _db.HomeBanners
            .AsNoTracking()
            .OrderBy(b => b.SortOrder)
            .ThenByDescending(b => b.Id)
            .ToListAsync(ct);

        var campaigns = await _db.PlatformCampaigns
            .AsNoTracking()
            .OrderByDescending(c => c.IsActive)
            .ThenByDescending(c => c.StartDate)
            .ToListAsync(ct);

        ViewBag.Banners = banners;
        ViewBag.Campaigns = campaigns;
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateBanner(
        string title,
        string? subtitle,
        string? badgeText,
        string? imageUrl,
        IFormFile? imageFile,
        string? linkUrl,
        string? buttonText,
        int sortOrder = 0,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            TempData["AdminToastError"] = "Vui lòng nhập tiêu đề Banner.";
            return RedirectToAction(nameof(Index));
        }

        string finalImg = imageUrl?.Trim() ?? "/assets/images/home-4/hero-1.png";
        if (imageFile != null && imageFile.Length > 0)
        {
            var ext = Path.GetExtension(imageFile.FileName)?.ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(ext)) ext = ".jpg";
            var dir = Path.Combine(_env.WebRootPath, "uploads", "banners");
            Directory.CreateDirectory(dir);
            var fileName = $"{Guid.NewGuid():N}{ext}";
            await using var fs = System.IO.File.Create(Path.Combine(dir, fileName));
            await imageFile.CopyToAsync(fs, ct);
            finalImg = $"/uploads/banners/{fileName}";
        }

        var banner = new HomeBanner
        {
            Title = title.Trim(),
            Subtitle = string.IsNullOrWhiteSpace(subtitle) ? null : subtitle.Trim(),
            BadgeText = string.IsNullOrWhiteSpace(badgeText) ? "Ưu đãi độc quyền Sellzy" : badgeText.Trim(),
            ImageUrl = finalImg,
            LinkUrl = string.IsNullOrWhiteSpace(linkUrl) ? "/vouchers" : linkUrl.Trim(),
            ButtonText = string.IsNullOrWhiteSpace(buttonText) ? "Khám phá ngay" : buttonText.Trim(),
            SortOrder = sortOrder,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        _db.HomeBanners.Add(banner);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(HttpContext, "CreateHomeBanner", "HomeBanner", banner.Id.ToString(), banner.Title);

        TempData["AdminToast"] = $"Đã thêm Banner trang chủ '{banner.Title}' thành công!";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleBanner(int id, CancellationToken ct = default)
    {
        var b = await _db.HomeBanners.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (b != null)
        {
            b.IsActive = !b.IsActive;
            await _db.SaveChangesAsync(ct);
            await _audit.LogAsync(HttpContext, "ToggleHomeBanner", "HomeBanner", b.Id.ToString(), $"{b.Title} -> IsActive={b.IsActive}");
            TempData["AdminToast"] = $"Đã cập nhật trạng thái Banner '{b.Title}'.";
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteBanner(int id, CancellationToken ct = default)
    {
        var b = await _db.HomeBanners.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (b != null)
        {
            _db.HomeBanners.Remove(b);
            await _db.SaveChangesAsync(ct);
            await _audit.LogAsync(HttpContext, "DeleteHomeBanner", "HomeBanner", id.ToString(), b.Title);
            TempData["AdminToast"] = $"Đã xóa Banner '{b.Title}'.";
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateCampaign(
        string name,
        string? discountUpToText,
        string? description,
        DateTime? startDate,
        DateTime? endDate,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            TempData["AdminToastError"] = "Vui lòng nhập tên Chiến dịch Sàn.";
            return RedirectToAction(nameof(Index));
        }

        var slug = $"camp-{DateTime.UtcNow:yyyyMMddHHmmss}";
        var camp = new PlatformCampaign
        {
            Name = name.Trim(),
            Slug = slug,
            DiscountUpToText = string.IsNullOrWhiteSpace(discountUpToText) ? "Giảm đến 50% + Freeship Toàn Sàn" : discountUpToText.Trim(),
            Description = string.IsNullOrWhiteSpace(description) ? "Sự kiện siêu sale toàn sàn Sellzy với hàng ngàn deal chính hãng và mã Freeship." : description.Trim(),
            StartDate = startDate ?? DateTime.UtcNow,
            EndDate = endDate ?? DateTime.UtcNow.AddDays(7),
            ThemeColor = "#008374",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        _db.PlatformCampaigns.Add(camp);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(HttpContext, "CreatePlatformCampaign", "PlatformCampaign", camp.Id.ToString(), camp.Name);

        TempData["AdminToast"] = $"Đã phát động Chiến dịch Sàn '{camp.Name}' thành công!";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleCampaign(int id, CancellationToken ct = default)
    {
        var c = await _db.PlatformCampaigns.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c != null)
        {
            c.IsActive = !c.IsActive;
            await _db.SaveChangesAsync(ct);
            await _audit.LogAsync(HttpContext, "TogglePlatformCampaign", "PlatformCampaign", c.Id.ToString(), $"{c.Name} -> IsActive={c.IsActive}");
            TempData["AdminToast"] = $"Đã cập nhật trạng thái Chiến dịch '{c.Name}'.";
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteCampaign(int id, CancellationToken ct = default)
    {
        var c = await _db.PlatformCampaigns.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c != null)
        {
            _db.PlatformCampaigns.Remove(c);
            await _db.SaveChangesAsync(ct);
            await _audit.LogAsync(HttpContext, "DeletePlatformCampaign", "PlatformCampaign", id.ToString(), c.Name);
            TempData["AdminToast"] = $"Đã xóa Chiến dịch '{c.Name}'.";
        }
        return RedirectToAction(nameof(Index));
    }
}
