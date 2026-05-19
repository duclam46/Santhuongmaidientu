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
        var pendingItems = await q.CountAsync(x => x.Status == "Pending", cancellationToken);

        var status = (query.Status ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(status))
        {
            q = q.Where(x => x.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            q = q.Where(x => x.Email.Contains(term) || (x.ShopName != null && x.ShopName.Contains(term)) || (x.FullName != null && x.FullName.Contains(term)));
        }

        var totalItems = await q.CountAsync(cancellationToken);

        var page = query.Page <= 0 ? 1 : query.Page;
        var pageSize = query.PageSize <= 0 ? 20 : Math.Min(query.PageSize, 50);
        var skip = (page - 1) * pageSize;

        var items = await q
            .OrderBy(x => x.Status == "Pending" ? 0 : 1)
            .ThenByDescending(x => x.CreatedAt)
            .Skip(skip)
            .Take(pageSize)
            .Select(x => new AdminPartnerApplicationListItemVm
            {
                Id = x.Id,
                Status = x.Status,
                Email = x.Email,
                FullName = x.FullName,
                PhoneNumber = x.PhoneNumber,
                ShopName = x.ShopName,
                CreatedAt = x.CreatedAt
            })
            .ToListAsync(cancellationToken);

        return View(new AdminPartnerApplicationListVm
        {
            Query = query,
            TotalItems = totalItems,
            PendingItems = pendingItems,
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

        return View(new AdminPartnerApplicationDetailsVm
        {
            Id = app.Id,
            AccountId = app.AccountId,
            Status = app.Status,
            FullName = app.FullName,
            Email = app.Email,
            PhoneNumber = app.PhoneNumber,
            ShopName = app.ShopName,
            ShopDescription = app.ShopDescription,
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

        if (!PartnerShopAddressHelper.TryNormalizeShopAddress(
            app.FullName,
            app.PhoneNumber,
            app.ProvinceId,
            app.ProvinceName,
            app.DistrictId,
            app.DistrictName,
            app.WardCode,
            app.WardName,
            app.StreetAddress,
            out var addrError,
            out var normalized))
        {
            TempData["AdminToast"] = addrError;
            return RedirectToAction(nameof(Details), new { id = app.Id });
        }

        var nowUtc = DateTime.UtcNow;

        account.Role = "Shop";
        account.Status = "Active";

        account.Profile.FullName = app.FullName;
        account.Profile.PhoneNumber = app.PhoneNumber;
        account.Profile.ShopName = app.ShopName;
        account.Profile.ShopDescription = app.ShopDescription;
        account.Profile.Address = app.Address;
        account.Profile.UpdatedAt = nowUtc;

        if (string.IsNullOrWhiteSpace(account.Profile.ShopSlug) && !string.IsNullOrWhiteSpace(app.ShopName))
        {
            account.Profile.ShopSlug = await ShopSlugHelper.GenerateUniqueShopSlugAsync(_db, app.ShopName, cancellationToken);
        }

        var existingShopAddr = await _db.UserAddresses
            .Where(a => a.AccountId == account.Id && a.AddressType == AddressType.Shop)
            .OrderByDescending(a => a.IsDefault)
            .ThenByDescending(a => a.UpdatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (existingShopAddr is null)
        {
            existingShopAddr = new UserAddress
            {
                AccountId = account.Id,
                AddressType = AddressType.Shop,
                IsDefault = true,
                CreatedAt = nowUtc,
                UpdatedAt = nowUtc
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
        existingShopAddr.UpdatedAt = nowUtc;

        var otherDefaults = await _db.UserAddresses
            .Where(a => a.AccountId == account.Id && a.AddressType == AddressType.Shop && a.Id != existingShopAddr.Id && a.IsDefault)
            .ToListAsync(cancellationToken);

        foreach (var o in otherDefaults)
        {
            o.IsDefault = false;
            o.UpdatedAt = nowUtc;
        }

        app.Status = "Approved";
        app.RejectionReason = null;
        app.DecidedAt = nowUtc;
        app.DecidedByAccountId = adminAccountId;
        app.UpdatedAt = nowUtc;

        await _db.SaveChangesAsync(cancellationToken);

        TempData["AdminToast"] = "Đã duyệt phiếu và nâng cấp tài khoản thành nhà bán hàng.";
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

        var reason = string.IsNullOrWhiteSpace(model.Reason) ? null : model.Reason.Trim();
        if (reason is not null && reason.Length > 500) reason = reason[..500];

        var nowUtc = DateTime.UtcNow;
        app.Status = "Rejected";
        app.RejectionReason = reason;
        app.DecidedAt = nowUtc;
        app.DecidedByAccountId = adminAccountId;
        app.UpdatedAt = nowUtc;

        await _db.SaveChangesAsync(cancellationToken);

        TempData["AdminToast"] = "Đã từ chối phiếu đăng ký.";
        return RedirectToAction(nameof(Details), new { id = app.Id });
    }
}
