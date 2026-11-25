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
[Authorize(Roles = "Shop")]
public class PartnerStoresController : Controller
{
    private readonly DataContext _db;

    public PartnerStoresController(DataContext db)
    {
        _db = db;
    }

    private async Task<(Account? Account, PartnerStoreManageVm? Vm)> BuildStoreVmAsync(CancellationToken cancellationToken)
    {
        if (!(User?.Identity?.IsAuthenticated ?? false)) return (null, null);

        var idRaw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(idRaw, out var accountId) || accountId <= 0) return (null, null);

        var account = await _db.Accounts
            .Include(a => a.Profile)
            .FirstOrDefaultAsync(a => a.Id == accountId && !a.IsDeleted, cancellationToken);

        if (account is null) return (null, null);

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

        var totalOrders = await _db.Orders
            .AsNoTracking()
            .CountAsync(o => o.ShopId == account.Id, cancellationToken);

        var completedOrders = await _db.Orders
            .AsNoTracking()
            .CountAsync(o => o.ShopId == account.Id && (o.OrderStatus == "Completed" || o.OrderStatus == "Delivered"), cancellationToken);

        var cancelledOrders = await _db.Orders
            .AsNoTracking()
            .CountAsync(o => o.ShopId == account.Id && o.OrderStatus == "Cancelled", cancellationToken);

        decimal cancelRate = totalOrders > 0
            ? Math.Round((decimal)cancelledOrders * 100m / totalOrders, 1)
            : 0m;

        var activeProducts = await _db.Products
            .AsNoTracking()
            .Where(p => p.ShopId == account.Id && p.Status == "Active" && !p.IsDeleted)
            .ToListAsync(cancellationToken);

        decimal avgRating = activeProducts.Count > 0
            ? Math.Round(activeProducts.Average(p => p.AverageRating), 1)
            : 5.0m;

        var hasPendingMallApp = await _db.PartnerApplications
            .AsNoTracking()
            .AnyAsync(a => a.AccountId == account.Id && a.Status == "Pending" && a.RequestedShopTier == "Mall", cancellationToken);

        var mallStatus = profile.IsMall
            ? "Approved"
            : (hasPendingMallApp ? "Pending" : (string.IsNullOrWhiteSpace(profile.MallStatus) ? "None" : profile.MallStatus));

        var vm = new PartnerStoreManageVm
        {
            Email = account.Email,
            PhoneNumber = profile.PhoneNumber,
            ShopName = profile.ShopName,
            ShopSlug = profile.ShopSlug,
            ShopDescription = profile.ShopDescription,
            AutoReplyMessage = profile.AutoReplyMessage,
            Address = profile.Address,
            StreetAddress = street,
            WardName = ward,
            DistrictName = district,
            ProvinceName = province,
            ShopLogoUrl = profile.ShopLogoUrl,
            ShopCoverUrl = profile.AvatarUrl,
            IsMall = profile.IsMall,
            ShopTier = profile.IsMall ? "Mall" : (string.IsNullOrWhiteSpace(profile.ShopTier) ? "Standard" : profile.ShopTier),
            MallStatus = mallStatus,
            BusinessName = profile.BusinessName,
            BusinessTaxCode = profile.BusinessTaxCode,
            BrandName = profile.BrandName,
            BusinessLicenseUrl = profile.BusinessLicenseUrl,
            BrandAuthorizationUrl = profile.BrandAuthorizationUrl,
            MallRejectionReason = profile.MallRejectionReason,
            MallApprovedAt = profile.MallApprovedAt,
            CompletedOrderCount = completedOrders,
            CancelRatePercent = cancelRate,
            AverageRating = avgRating,
            ActiveProductCount = activeProducts.Count
        };

        return (account, vm);
    }

    [HttpGet]
    [HttpGet("/Admin/PartnerStores")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var (account, vm) = await BuildStoreVmAsync(cancellationToken);
        if (account is null || vm is null)
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            TempData["PartnerStoreError"] = "Phiên đăng nhập không hợp lệ. Vui lòng đăng nhập lại.";
            return RedirectToAction("Index", "Home", new { area = "Admin" });
        }

        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> MallUpgrade(CancellationToken cancellationToken)
    {
        var (account, vm) = await BuildStoreVmAsync(cancellationToken);
        if (account is null || vm is null)
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Index", "Home", new { area = "Admin" });
        }

        return View(vm);
    }

    public sealed record SubmitMallUpgradeRequest(
        string? BusinessName,
        string? BusinessTaxCode,
        string? BrandName,
        string? BusinessLicenseUrl,
        IFormFile? BusinessLicenseFile,
        string? BrandAuthorizationUrl,
        IFormFile? BrandAuthorizationFile);

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SubmitMallUpgrade([FromForm] SubmitMallUpgradeRequest req, CancellationToken cancellationToken)
    {
        var (account, vm) = await BuildStoreVmAsync(cancellationToken);
        if (account is null || vm is null || account.Profile is null)
        {
            return RedirectToAction(nameof(MallUpgrade));
        }

        if (account.Profile.IsMall)
        {
            TempData["PartnerStoreSuccess"] = "Gian hàng của bạn đã đạt danh hiệu chính hãng Sellzy Mall.";
            return RedirectToAction(nameof(MallUpgrade));
        }

        var hasPending = await _db.PartnerApplications
            .AsNoTracking()
            .AnyAsync(a => a.AccountId == account.Id && a.Status == "Pending" && a.RequestedShopTier == "Mall", cancellationToken);

        if (hasPending)
        {
            TempData["PartnerStoreError"] = "Bạn đã có một hồ sơ xin nâng cấp Sellzy Mall đang chờ Admin xét duyệt.";
            return RedirectToAction(nameof(MallUpgrade));
        }

        var businessName = req.BusinessName?.Trim();
        var businessTaxCode = req.BusinessTaxCode?.Trim();
        var brandName = req.BrandName?.Trim();

        if (string.IsNullOrWhiteSpace(businessName) || string.IsNullOrWhiteSpace(businessTaxCode) || string.IsNullOrWhiteSpace(brandName))
        {
            TempData["PartnerStoreError"] = "Vui lòng điền đầy đủ Tên công ty/Pháp nhân, Mã số thuế và Tên thương hiệu đăng ký.";
            return RedirectToAction(nameof(MallUpgrade));
        }

        var licenseUrl = string.IsNullOrWhiteSpace(req.BusinessLicenseUrl) ? account.Profile.BusinessLicenseUrl : req.BusinessLicenseUrl.Trim();
        if (req.BusinessLicenseFile is not null && req.BusinessLicenseFile.Length > 0)
        {
            var saved = await TrySaveDocumentAsync(req.BusinessLicenseFile, "partners/licenses", cancellationToken);
            if (!string.IsNullOrWhiteSpace(saved)) licenseUrl = saved;
        }

        var authUrl = string.IsNullOrWhiteSpace(req.BrandAuthorizationUrl) ? account.Profile.BrandAuthorizationUrl : req.BrandAuthorizationUrl.Trim();
        if (req.BrandAuthorizationFile is not null && req.BrandAuthorizationFile.Length > 0)
        {
            var saved = await TrySaveDocumentAsync(req.BrandAuthorizationFile, "partners/authorizations", cancellationToken);
            if (!string.IsNullOrWhiteSpace(saved)) authUrl = saved;
        }

        var shopAddr = await _db.UserAddresses
            .AsNoTracking()
            .Where(a => a.AccountId == account.Id && a.AddressType == AddressType.Shop)
            .OrderByDescending(a => a.IsDefault)
            .ThenByDescending(a => a.UpdatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        var nowUtc = DateTime.UtcNow;

        account.Profile.BusinessName = businessName;
        account.Profile.BusinessTaxCode = businessTaxCode;
        account.Profile.BrandName = brandName;
        account.Profile.BusinessLicenseUrl = licenseUrl;
        account.Profile.BrandAuthorizationUrl = authUrl;
        account.Profile.MallStatus = "Pending";
        account.Profile.MallRejectionReason = null;
        account.Profile.UpdatedAt = nowUtc;

        _db.PartnerApplications.Add(new PartnerApplication
        {
            AccountId = account.Id,
            FullName = account.Profile.FullName ?? account.Profile.ShopName,
            Email = account.Email,
            PhoneNumber = account.Profile.PhoneNumber,
            ShopName = account.Profile.ShopName,
            ShopDescription = account.Profile.ShopDescription,
            Address = account.Profile.Address,
            ProvinceId = shopAddr?.ProvinceId ?? 0,
            ProvinceName = shopAddr?.ProvinceName,
            DistrictId = shopAddr?.DistrictId ?? 0,
            DistrictName = shopAddr?.DistrictName,
            WardCode = shopAddr?.WardCode,
            WardName = shopAddr?.WardName,
            StreetAddress = shopAddr?.StreetAddress,
            RequestedShopTier = "Mall",
            ApplicationType = "MallUpgrade",
            BusinessName = businessName,
            BusinessTaxCode = businessTaxCode,
            BrandName = brandName,
            BusinessLicenseUrl = licenseUrl,
            BrandAuthorizationUrl = authUrl,
            Status = "Pending",
            CreatedAt = nowUtc,
            UpdatedAt = nowUtc
        });

        await _db.SaveChangesAsync(cancellationToken);

        TempData["PartnerStoreSuccess"] = "Đã gửi hồ sơ nâng cấp lên Sellzy Mall! Admin sẽ thẩm định chứng từ pháp lý và chỉ số vận hành của bạn.";
        return RedirectToAction(nameof(MallUpgrade));
    }

    public sealed record UpdateStoreProfileRequest(
        string? PhoneNumber,
        string? ShopName,
        string? ShopDescription,
        string? AutoReplyMessage,
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
        var autoReply = string.IsNullOrWhiteSpace(req.AutoReplyMessage) ? null : req.AutoReplyMessage.Trim();
        if (autoReply != null && autoReply.Length > 1000) autoReply = autoReply[..1000];
        account.Profile.AutoReplyMessage = autoReply;
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

    private async Task<string?> TrySaveDocumentAsync(IFormFile file, string folder, CancellationToken cancellationToken)
    {
        const long maxBytes = 8 * 1024 * 1024; // 8MB
        if (file.Length <= 0 || file.Length > maxBytes) return null;

        var ext = Path.GetExtension(file.FileName)?.ToLowerInvariant() ?? string.Empty;
        if (ext is not ".jpg" and not ".jpeg" and not ".png" and not ".webp" and not ".pdf") ext = ".png";

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
