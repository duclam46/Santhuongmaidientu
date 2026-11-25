using System.Security.Claims;
using DOAN_BANHANG.Helpers;
using DOAN_BANHANG.Models;
using DOAN_BANHANG.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;

namespace DOAN_BANHANG.Areas.Admin.Controllers;

[Area("Admin")]
[Route("Admin/[controller]/[action]")]
[Authorize(Roles = "Admin")]
public class PartnerApplicationsController : Controller
{
    private readonly DataContext _db;

    public PartnerApplicationsController(DataContext db)
    {
        _db = db;
    }

    private bool TryGetSignedInAccount(out int accountId, out string? role)
    {
        role = null;
        accountId = 0;

        if (!(User?.Identity?.IsAuthenticated ?? false)) return false;

        var idRaw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(idRaw, out accountId) || accountId <= 0) return false;

        role = User.FindFirstValue(ClaimTypes.Role);
        return true;
    }

    private bool IsAdminRole(string? role)
        => string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase);

    [HttpGet]
    [HttpGet("/Admin/PartnerApplications")]
    public async Task<IActionResult> Index([FromQuery] AdminPartnerApplicationListQueryVm query, CancellationToken cancellationToken)
    {
        if (!TryGetSignedInAccount(out _, out var role))
        {
            return View(new AdminPartnerApplicationListVm { Query = query });
        }

        if (!IsAdminRole(role))
        {
            TempData["AdminToast"] = "Bạn không có quyền xét duyệt phiếu đăng ký đối tác.";
            return View(new AdminPartnerApplicationListVm { Query = query });
        }

        var q = _db.PartnerApplications.AsNoTracking().AsQueryable();
        var allApplicationsCount = await q.CountAsync(cancellationToken);
        var pendingItems = await q.CountAsync(x => x.Status == "Pending", cancellationToken);
        var approvedItems = await q.CountAsync(x => x.Status == "Approved", cancellationToken);
        var rejectedItems = await q.CountAsync(x => x.Status == "Rejected", cancellationToken);
        var pendingMallItems = await q.CountAsync(x => x.Status == "Pending" && x.RequestedShopTier == "Mall", cancellationToken);

        var status = (query.Status ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(status))
        {
            q = q.Where(x => x.Status == status);
        }

        var tier = (query.Tier ?? string.Empty).Trim();
        if (string.Equals(tier, "Standard", StringComparison.OrdinalIgnoreCase))
        {
            q = q.Where(x => x.RequestedShopTier == "Standard");
        }
        else if (string.Equals(tier, "Mall", StringComparison.OrdinalIgnoreCase))
        {
            q = q.Where(x => x.RequestedShopTier == "Mall");
        }
        else if (string.Equals(tier, "MallUpgrade", StringComparison.OrdinalIgnoreCase))
        {
            q = q.Where(x => x.ApplicationType == "MallUpgrade");
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            q = q.Where(x => x.Email.Contains(term)
                || (x.ShopName != null && x.ShopName.Contains(term))
                || (x.FullName != null && x.FullName.Contains(term))
                || (x.BusinessName != null && x.BusinessName.Contains(term))
                || (x.BusinessTaxCode != null && x.BusinessTaxCode.Contains(term)));
        }

        var totalItems = await q.CountAsync(cancellationToken);

        var page = query.Page <= 0 ? 1 : query.Page;
        var pageSize = query.PageSize <= 0 ? 20 : Math.Min(query.PageSize, 50);
        var skip = (page - 1) * pageSize;

        var items = await q
            .OrderBy(x => x.Status == "Pending" ? 0 : 1)
            .ThenByDescending(x => x.RequestedShopTier == "Mall")
            .ThenByDescending(x => x.CreatedAt)
            .Skip(skip)
            .Take(pageSize)
            .Select(x => new AdminPartnerApplicationListItemVm
            {
                Id = x.Id,
                Status = x.Status,
                RequestedShopTier = x.RequestedShopTier ?? "Standard",
                ApplicationType = x.ApplicationType ?? "NewRegistration",
                Email = x.Email,
                FullName = x.FullName,
                PhoneNumber = x.PhoneNumber,
                ShopName = x.ShopName,
                BusinessName = x.BusinessName,
                BusinessTaxCode = x.BusinessTaxCode,
                BrandName = x.BrandName,
                CreatedAt = x.CreatedAt
            })
            .ToListAsync(cancellationToken);

        return View(new AdminPartnerApplicationListVm
        {
            Query = query,
            AllApplicationsCount = allApplicationsCount,
            TotalItems = totalItems,
            PendingItems = pendingItems,
            ApprovedItems = approvedItems,
            RejectedItems = rejectedItems,
            PendingMallItems = pendingMallItems,
            Items = items
        });
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken)
    {
        if (!TryGetSignedInAccount(out _, out var role))
        {
            TempData["AdminToast"] = "Vui lòng đăng nhập để xem phiếu đăng ký.";
            return RedirectToAction(nameof(Index));
        }

        if (!IsAdminRole(role))
        {
            TempData["AdminToast"] = "Bạn không có quyền xem phiếu đăng ký.";
            return RedirectToAction(nameof(Index));
        }

        var app = await _db.PartnerApplications
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (app is null) return NotFound();

        var shopProfile = await _db.UserProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.AccountId == app.AccountId, cancellationToken);

        var totalOrders = await _db.Orders
            .AsNoTracking()
            .CountAsync(o => o.ShopId == app.AccountId, cancellationToken);

        var completedOrders = await _db.Orders
            .AsNoTracking()
            .CountAsync(o => o.ShopId == app.AccountId && (o.OrderStatus == "Completed" || o.OrderStatus == "Delivered"), cancellationToken);

        var cancelledOrders = await _db.Orders
            .AsNoTracking()
            .CountAsync(o => o.ShopId == app.AccountId && o.OrderStatus == "Cancelled", cancellationToken);

        decimal cancelRate = totalOrders > 0
            ? Math.Round((decimal)cancelledOrders * 100m / totalOrders, 1)
            : 0m;

        var activeProducts = await _db.Products
            .AsNoTracking()
            .Where(p => p.ShopId == app.AccountId && p.Status == "Active" && !p.IsDeleted)
            .ToListAsync(cancellationToken);

        decimal avgRating = activeProducts.Count > 0
            ? Math.Round(activeProducts.Average(p => p.AverageRating), 1)
            : 5.0m;

        return View(new AdminPartnerApplicationDetailsVm
        {
            Id = app.Id,
            AccountId = app.AccountId,
            Status = app.Status,
            RequestedShopTier = string.IsNullOrWhiteSpace(app.RequestedShopTier) ? "Standard" : app.RequestedShopTier,
            ApplicationType = string.IsNullOrWhiteSpace(app.ApplicationType) ? "NewRegistration" : app.ApplicationType,
            FullName = app.FullName,
            Email = app.Email,
            PhoneNumber = app.PhoneNumber,
            ShopName = app.ShopName,
            ShopDescription = app.ShopDescription,
            BusinessName = app.BusinessName ?? shopProfile?.BusinessName,
            BusinessTaxCode = app.BusinessTaxCode ?? shopProfile?.BusinessTaxCode,
            BrandName = app.BrandName ?? shopProfile?.BrandName,
            BusinessLicenseUrl = app.BusinessLicenseUrl ?? shopProfile?.BusinessLicenseUrl,
            BrandAuthorizationUrl = app.BrandAuthorizationUrl ?? shopProfile?.BrandAuthorizationUrl,
            CurrentIsMall = shopProfile?.IsMall ?? false,
            CompletedOrderCount = completedOrders,
            CancelRatePercent = cancelRate,
            AverageRating = avgRating,
            ActiveProductCount = activeProducts.Count,
            Address = app.Address,
            ProvinceId = app.ProvinceId,
            ProvinceName = app.ProvinceName,
            DistrictId = app.DistrictId,
            DistrictName = app.DistrictName,
            WardCode = app.WardCode,
            WardName = app.WardName,
            StreetAddress = app.StreetAddress,
            RejectionReason = app.RejectionReason,
            CreatedAt = app.CreatedAt,
            DecidedAt = app.DecidedAt,
            DecidedByAccountId = app.DecidedByAccountId
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(AdminPartnerApplicationDecisionVm model, CancellationToken cancellationToken)
    {
        if (!TryGetSignedInAccount(out var adminAccountId, out var role))
        {
            TempData["AdminToast"] = "Vui lòng đăng nhập để xét duyệt.";
            return RedirectToAction(nameof(Details), new { id = model.Id });
        }

        if (!IsAdminRole(role))
        {
            TempData["AdminToast"] = "Bạn không có quyền duyệt phiếu đăng ký.";
            return RedirectToAction(nameof(Details), new { id = model.Id });
        }

        var app = await _db.PartnerApplications
            .FirstOrDefaultAsync(x => x.Id == model.Id, cancellationToken);

        if (app is null) return NotFound();

        if (!string.Equals(app.Status, "Pending", StringComparison.OrdinalIgnoreCase))
        {
            TempData["AdminToast"] = "Phiếu không còn ở trạng thái chờ duyệt.";
            return RedirectToAction(nameof(Details), new { id = app.Id });
        }

        var account = await _db.Accounts
            .Include(a => a.Profile)
            .FirstOrDefaultAsync(a => a.Id == app.AccountId && !a.IsDeleted, cancellationToken);

        if (account is null)
        {
            TempData["AdminToast"] = "Không tìm thấy tài khoản của phiếu đăng ký.";
            return RedirectToAction(nameof(Details), new { id = app.Id });
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

        var isMallUpgradeOnly = string.Equals(app.ApplicationType, "MallUpgrade", StringComparison.OrdinalIgnoreCase);

        if (!isMallUpgradeOnly && app.ProvinceId > 0 && app.DistrictId > 0 && !string.IsNullOrWhiteSpace(app.WardCode))
        {
            if (PartnerShopAddressHelper.TryNormalizeShopAddress(
                app.FullName,
                app.PhoneNumber,
                app.ProvinceId,
                app.ProvinceName,
                app.DistrictId,
                app.DistrictName,
                app.WardCode,
                app.WardName,
                app.StreetAddress,
                out _,
                out var normalized))
            {
                var existingShopAddr = await _db.UserAddresses
                    .Where(a => a.AccountId == account.Id && a.AddressType == AddressType.Shop)
                    .OrderByDescending(a => a.IsDefault)
                    .ThenByDescending(a => a.UpdatedAt)
                    .FirstOrDefaultAsync(cancellationToken);

                var nowAddr = DateTime.UtcNow;
                if (existingShopAddr is null)
                {
                    existingShopAddr = new UserAddress
                    {
                        AccountId = account.Id,
                        AddressType = AddressType.Shop,
                        IsDefault = true,
                        CreatedAt = nowAddr,
                        UpdatedAt = nowAddr
                    };
                    _db.UserAddresses.Add(existingShopAddr);
                }

                existingShopAddr.FullName = normalized.FullName;
                existingShopAddr.PhoneNumber = normalized.PhoneNumber;
                existingShopAddr.ProvinceId = normalized.ProvinceId;
                existingShopAddr.ProvinceName = normalized.ProvinceName;
                existingShopAddr.DistrictId = normalized.DistrictId;
                existingShopAddr.DistrictName = normalized.DistrictName;
                existingShopAddr.WardCode = normalized.WardCode;
                existingShopAddr.WardName = normalized.WardName;
                existingShopAddr.StreetAddress = normalized.StreetAddress;
                existingShopAddr.AddressType = AddressType.Shop;
                existingShopAddr.IsDefault = true;
                existingShopAddr.UpdatedAt = nowAddr;
            }
        }

        var nowUtc = DateTime.UtcNow;

        account.Role = "Shop";
        account.Status = "Active";

        if (!string.IsNullOrWhiteSpace(app.FullName)) account.Profile.FullName = app.FullName;
        if (!string.IsNullOrWhiteSpace(app.PhoneNumber)) account.Profile.PhoneNumber = app.PhoneNumber;
        if (!string.IsNullOrWhiteSpace(app.ShopName)) account.Profile.ShopName = app.ShopName;
        if (!string.IsNullOrWhiteSpace(app.ShopDescription)) account.Profile.ShopDescription = app.ShopDescription;
        if (!string.IsNullOrWhiteSpace(app.Address)) account.Profile.Address = app.Address;

        var targetTier = !string.IsNullOrWhiteSpace(model.ApprovedTier)
            ? model.ApprovedTier.Trim()
            : (app.RequestedShopTier ?? "Standard");

        var approveAsMall = string.Equals(targetTier, "Mall", StringComparison.OrdinalIgnoreCase);

        if (approveAsMall)
        {
            account.Profile.IsMall = true;
            account.Profile.ShopTier = "Mall";
            account.Profile.MallStatus = "Approved";
            account.Profile.MallApprovedAt = nowUtc;
            account.Profile.MallRejectionReason = null;
            if (!string.IsNullOrWhiteSpace(app.BusinessName)) account.Profile.BusinessName = app.BusinessName;
            if (!string.IsNullOrWhiteSpace(app.BusinessTaxCode)) account.Profile.BusinessTaxCode = app.BusinessTaxCode;
            if (!string.IsNullOrWhiteSpace(app.BrandName)) account.Profile.BrandName = app.BrandName;
            if (!string.IsNullOrWhiteSpace(app.BusinessLicenseUrl)) account.Profile.BusinessLicenseUrl = app.BusinessLicenseUrl;
            if (!string.IsNullOrWhiteSpace(app.BrandAuthorizationUrl)) account.Profile.BrandAuthorizationUrl = app.BrandAuthorizationUrl;
        }
        else
        {
            account.Profile.IsMall = false;
            account.Profile.ShopTier = "Standard";
            if (string.Equals(app.RequestedShopTier, "Mall", StringComparison.OrdinalIgnoreCase))
            {
                account.Profile.MallStatus = "Rejected";
                account.Profile.MallRejectionReason = "Được duyệt làm Shop Thường trước (hồ sơ Mall cần bổ sung thêm).";
            }
        }

        account.Profile.UpdatedAt = nowUtc;

        if (string.IsNullOrWhiteSpace(account.Profile.ShopSlug) && !string.IsNullOrWhiteSpace(account.Profile.ShopName))
        {
            account.Profile.ShopSlug = await ShopSlugHelper.GenerateUniqueShopSlugAsync(_db, account.Profile.ShopName, cancellationToken);
        }

        app.Status = "Approved";
        app.RejectionReason = null;
        app.DecidedAt = nowUtc;
        app.DecidedByAccountId = adminAccountId;
        app.UpdatedAt = nowUtc;

        await _db.SaveChangesAsync(cancellationToken);

        TempData["AdminToast"] = approveAsMall
            ? "Đã duyệt hồ sơ và cấp danh hiệu chính hãng Sellzy Mall (+2% biểu phí Mall, 0% thuế khấu trừ cá nhân)!"
            : "Đã duyệt hồ sơ và kích hoạt gian hàng Shop Thường!";
        return RedirectToAction(nameof(Details), new { id = app.Id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reject(AdminPartnerApplicationDecisionVm model, CancellationToken cancellationToken)
    {
        if (!TryGetSignedInAccount(out var adminAccountId, out var role))
        {
            TempData["AdminToast"] = "Vui lòng đăng nhập để xét duyệt.";
            return RedirectToAction(nameof(Details), new { id = model.Id });
        }

        if (!IsAdminRole(role))
        {
            TempData["AdminToast"] = "Bạn không có quyền từ chối phiếu đăng ký.";
            return RedirectToAction(nameof(Details), new { id = model.Id });
        }

        var app = await _db.PartnerApplications
            .FirstOrDefaultAsync(x => x.Id == model.Id, cancellationToken);

        if (app is null) return NotFound();

        if (!string.Equals(app.Status, "Pending", StringComparison.OrdinalIgnoreCase))
        {
            TempData["AdminToast"] = "Phiếu không còn ở trạng thái chờ duyệt.";
            return RedirectToAction(nameof(Details), new { id = app.Id });
        }

        var reason = string.IsNullOrWhiteSpace(model.Reason) ? "Hồ sơ chưa đáp ứng đủ điều kiện pháp lý hoặc chỉ số vận hành." : model.Reason.Trim();
        if (reason.Length > 500) reason = reason[..500];

        var nowUtc = DateTime.UtcNow;
        app.Status = "Rejected";
        app.RejectionReason = reason;
        app.DecidedAt = nowUtc;
        app.DecidedByAccountId = adminAccountId;
        app.UpdatedAt = nowUtc;

        var profile = await _db.UserProfiles.FirstOrDefaultAsync(p => p.AccountId == app.AccountId, cancellationToken);
        if (profile != null && string.Equals(app.RequestedShopTier, "Mall", StringComparison.OrdinalIgnoreCase))
        {
            profile.MallStatus = "Rejected";
            profile.MallRejectionReason = reason;
            profile.UpdatedAt = nowUtc;
        }

        await _db.SaveChangesAsync(cancellationToken);

        TempData["AdminToast"] = "Đã từ chối hồ sơ và gửi lý do phản hồi cho nhà bán hàng.";
        return RedirectToAction(nameof(Details), new { id = app.Id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RevokeMall(AdminPartnerApplicationDecisionVm model, CancellationToken cancellationToken)
    {
        if (!TryGetSignedInAccount(out _, out var role) || !IsAdminRole(role))
        {
            TempData["AdminToast"] = "Bạn không có quyền thực hiện thao tác này.";
            return RedirectToAction(nameof(Details), new { id = model.Id });
        }

        var app = await _db.PartnerApplications.FirstOrDefaultAsync(x => x.Id == model.Id, cancellationToken);
        if (app is null) return NotFound();

        var profile = await _db.UserProfiles.FirstOrDefaultAsync(p => p.AccountId == app.AccountId, cancellationToken);
        if (profile != null)
        {
            profile.IsMall = false;
            profile.ShopTier = "Standard";
            profile.MallStatus = "Rejected";
            profile.MallRejectionReason = string.IsNullOrWhiteSpace(model.Reason)
                ? "Đã hạ hạng xuống Shop Thường do không duy trì đủ tiêu chuẩn Sellzy Mall."
                : model.Reason.Trim();
            profile.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
        }

        TempData["AdminToast"] = "Đã thu hồi danh hiệu Sellzy Mall và chuyển gian hàng về Shop Thường.";
        return RedirectToAction(nameof(Details), new { id = model.Id });
    }
}
