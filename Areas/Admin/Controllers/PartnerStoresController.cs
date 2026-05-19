using System.Security.Claims;
using DOAN_BANHANG.Models;
using DOAN_BANHANG.ViewModels;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;

namespace DOAN_BANHANG.Areas.Admin.Controllers;

[Area("Admin")]
[Route("Admin/[controller]/[action]")]
[Microsoft.AspNetCore.Authorization.Authorize(Roles = "Shop")]
public class PartnerStoresController : Controller
{
    private readonly DataContext _db;

    public PartnerStoresController(DataContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        if (!(User?.Identity?.IsAuthenticated ?? false))
        {
            TempData["PartnerStoreError"] = "Vui lòng đăng nhập để quản lý cửa hàng.";
            return RedirectToAction("Index", "Home", new { area = "Admin" });
        }

        var idRaw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(idRaw, out var accountId) || accountId <= 0)
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            TempData["PartnerStoreError"] = "Phiên đăng nhập không hợp lệ. Vui lòng đăng nhập lại.";
            return RedirectToAction("Index", "Home", new { area = "Admin" });
        }

        var account = await _db.Accounts
            .Include(a => a.Profile)
            .FirstOrDefaultAsync(a => a.Id == accountId && !a.IsDeleted, cancellationToken);

        if (account is null)
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            TempData["PartnerStoreError"] = "Tài khoản không tồn tại. Vui lòng đăng nhập lại.";
            return RedirectToAction("Index", "Home", new { area = "Admin" });
        }

        var profile = account.Profile;
        if (profile is null)
        {
            profile = new UserProfile
            {
                AccountId = account.Id,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _db.UserProfiles.Add(profile);
            await _db.SaveChangesAsync(cancellationToken);
        }

        var addressParts = (profile.Address ?? "").Split(", ", StringSplitOptions.RemoveEmptyEntries);
        var street = "";
        var ward = "";
        var district = "";
        var province = "";

        if (addressParts.Length >= 4)
        {
            street = string.Join(", ", addressParts[..^3]);
            ward = addressParts[^3];
            district = addressParts[^2];
            province = addressParts[^1];
        }
        else
        {
            street = profile.Address ?? "";
        }

        var vm = new PartnerStoreManageVm
        {
            Email = account.Email,
            PhoneNumber = profile.PhoneNumber,
            ShopName = profile.ShopName,
            ShopSlug = profile.ShopSlug,
            ShopDescription = profile.ShopDescription,
            Address = profile.Address,
            StreetAddress = street,
            WardName = ward,
            DistrictName = district,
            ProvinceName = province,
            ShopLogoUrl = profile.ShopLogoUrl,
            ShopCoverUrl = profile.AvatarUrl
        };

        return View(vm);
    }

    public sealed record UpdateStoreProfileRequest(
        string? PhoneNumber,
        string? ShopName,
        string? ShopDescription,
        string? StreetAddress,
        string? WardName,
        string? DistrictName,
        string? ProvinceName,
        string? ShopLogoUrl,
        IFormFile? ShopLogoFile,
        string? ShopCoverUrl,
        IFormFile? ShopCoverFile);

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Update([FromForm] UpdateStoreProfileRequest req, CancellationToken cancellationToken)
    {
        if (!(User?.Identity?.IsAuthenticated ?? false))
        {
            TempData["PartnerStoreError"] = "Vui lòng đăng nhập để quản lý cửa hàng.";
            return RedirectToAction(nameof(Index));
        }

        var idRaw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(idRaw, out var accountId) || accountId <= 0)
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            TempData["PartnerStoreError"] = "Phiên đăng nhập không hợp lệ. Vui lòng đăng nhập lại.";
            return RedirectToAction(nameof(Index));
        }

        var account = await _db.Accounts
            .Include(a => a.Profile)
            .FirstOrDefaultAsync(a => a.Id == accountId && !a.IsDeleted, cancellationToken);

        if (account is null)
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            TempData["PartnerStoreError"] = "Tài khoản không tồn tại. Vui lòng đăng nhập lại.";
            return RedirectToAction(nameof(Index));
        }

        if (account.Profile is null)
        {
            account.Profile = new UserProfile
            {
                AccountId = account.Id,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _db.UserProfiles.Add(account.Profile);
        }

        var phoneNumber = string.IsNullOrWhiteSpace(req.PhoneNumber) ? null : req.PhoneNumber.Trim();
        if (phoneNumber is not null && phoneNumber.Length > 20) phoneNumber = phoneNumber[..20];

        var shopName = string.IsNullOrWhiteSpace(req.ShopName) ? null : req.ShopName.Trim();
        if (shopName is not null && shopName.Length > 200) shopName = shopName[..200];

        var shopDescription = string.IsNullOrWhiteSpace(req.ShopDescription) ? null : req.ShopDescription.Trim();

        var streetAddress = string.IsNullOrWhiteSpace(req.StreetAddress) ? null : req.StreetAddress.Trim();
        var wardName = string.IsNullOrWhiteSpace(req.WardName) ? null : req.WardName.Trim();
        var districtName = string.IsNullOrWhiteSpace(req.DistrictName) ? null : req.DistrictName.Trim();
        var provinceName = string.IsNullOrWhiteSpace(req.ProvinceName) ? null : req.ProvinceName.Trim();

        var address = "";
        if (!string.IsNullOrWhiteSpace(provinceName) && !string.IsNullOrWhiteSpace(districtName) && !string.IsNullOrWhiteSpace(wardName))
        {
            address = $"{streetAddress}, {wardName}, {districtName}, {provinceName}";
        }
        else
        {
            address = streetAddress ?? "";
        }
        
        if (address.Length > 500) address = address[..500];

        var shopLogoUrl = string.IsNullOrWhiteSpace(req.ShopLogoUrl) ? null : req.ShopLogoUrl.Trim();
        if (req.ShopLogoFile is not null && req.ShopLogoFile.Length > 0)
        {
            var savedUrl = await TrySaveImageAsync(req.ShopLogoFile, "shops/logos", cancellationToken);
            if (savedUrl is not null)
            {
                shopLogoUrl = savedUrl;
            }
        }
        else if (shopLogoUrl is not null && shopLogoUrl.Length > 500)
        {
            shopLogoUrl = shopLogoUrl[..500];
        }

        var shopCoverUrl = string.IsNullOrWhiteSpace(req.ShopCoverUrl) ? null : req.ShopCoverUrl.Trim();
        if (req.ShopCoverFile is not null && req.ShopCoverFile.Length > 0)
        {
            var savedUrl = await TrySaveImageAsync(req.ShopCoverFile, "shops/covers", cancellationToken);
            if (savedUrl is not null)
            {
                shopCoverUrl = savedUrl;
            }
        }
        else if (shopCoverUrl is not null && shopCoverUrl.Length > 500)
        {
            shopCoverUrl = shopCoverUrl[..500];
        }

        if (string.IsNullOrWhiteSpace(shopName))
        {
            TempData["PartnerStoreError"] = "Tên cửa hàng không được để trống.";
            return RedirectToAction(nameof(Index));
        }

        account.Profile.PhoneNumber = phoneNumber;
        account.Profile.ShopName = shopName;
        account.Profile.ShopDescription = shopDescription;
        account.Profile.Address = address;
        account.Profile.ShopLogoUrl = shopLogoUrl;
        account.Profile.AvatarUrl = shopCoverUrl;

        if (string.IsNullOrWhiteSpace(account.Profile.ShopSlug))
        {
            account.Profile.ShopSlug = await GenerateUniqueShopSlugAsync(shopName, cancellationToken);
        }

        account.Profile.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        TempData["PartnerStoreSuccess"] = "Đã cập nhật thông tin cửa hàng.";
        return RedirectToAction(nameof(Index));
    }

    private async Task<string> GenerateUniqueShopSlugAsync(string input, CancellationToken cancellationToken)
    {
        var baseSlug = (input ?? string.Empty).Trim().ToLowerInvariant();
        baseSlug = string.Join('-', baseSlug.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (string.IsNullOrWhiteSpace(baseSlug)) baseSlug = "doi-tac";
        if (baseSlug.Length > 180) baseSlug = baseSlug[..180];

        var candidate = baseSlug;
        var i = 1;

        while (await _db.UserProfiles.AsNoTracking().AnyAsync(p => p.ShopSlug == candidate, cancellationToken))
        {
            candidate = $"{baseSlug}-{i}";
            i++;
            if (candidate.Length > 200)
            {
                candidate = candidate[..200];
            }
        }

        return candidate;
    }

    private async Task<string?> TrySaveImageAsync(IFormFile file, string folder, CancellationToken cancellationToken)
    {
        const long maxBytes = 5 * 1024 * 1024; // 5MB
        if (file.Length <= 0 || file.Length > maxBytes)
        {
            return null;
        }

        var ext = Path.GetExtension(file.FileName)?.ToLowerInvariant() ?? string.Empty;
        if (ext is not ".jpg" and not ".jpeg" and not ".png" and not ".webp")
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(file.ContentType) && !file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var webRoot = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
        var dir = Path.Combine(webRoot, "uploads", folder.Replace("/", "\\"));
        Directory.CreateDirectory(dir);

        var fileName = $"{Guid.NewGuid():N}{ext}";
        var fullPath = Path.Combine(dir, fileName);

        await using (var fs = System.IO.File.Create(fullPath))
        {
            await file.CopyToAsync(fs, cancellationToken);
        }

        return $"/uploads/{folder}/{fileName}";
    }
}
