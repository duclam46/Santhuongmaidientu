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
[Authorize(Roles = "Admin")]
public class VouchersController : Controller
{
    private readonly DataContext _db;

    public VouchersController(DataContext db)
    {
        _db = db;
    }

    private string GenerateRandomVoucherCode()
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        var random = new Random();
        return new string(Enumerable.Repeat(chars, 8)
            .Select(s => s[random.Next(s.Length)]).ToArray());
    }

    private async Task<(Account? Account, bool IsAdmin)> GetCurrentUserAsync()
    {
        if (!(User?.Identity?.IsAuthenticated ?? false)) return (null, false);

        var idRaw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(idRaw, out var accountId) || accountId <= 0) return (null, false);

        var account = await _db.Accounts
            .AsNoTracking()
            .Include(a => a.Profile)
            .FirstOrDefaultAsync(a => a.Id == accountId && !a.IsDeleted);

        if (account is null) return (null, false);

        var isAdmin = account.Role == "Admin";
        return (account, isAdmin);
    }

    [HttpGet]
    public async Task<IActionResult> Index(VoucherAdminQueryVm query, CancellationToken cancellationToken)
    {
        var (account, isAdmin) = await GetCurrentUserAsync();
        if (account == null)
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Index", "Home", new { area = "Admin" });
        }

        var q = _db.Vouchers
            .Include(v => v.Shop)
                .ThenInclude(s => s.Profile)
            .AsNoTracking();

        // Tạm thời tắt phân quyền theo yêu cầu để test
        /*
        if (isAdmin)
        {
            q = q.Where(v => v.ShopId == null);
        }
        else if (account.Role == "Shop")
        {
            q = q.Where(v => v.ShopId == account.Id);
        }
        else
        {
            return Forbid();
        }
        */

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            q = q.Where(v => v.Code.Contains(term));
        }

        if (!string.IsNullOrWhiteSpace(query.Status) && query.Status != "All")
        {
            var now = DateTime.Now;
            if (query.Status == "Active")
                q = q.Where(v => v.IsActive && v.StartDate <= now && v.EndDate >= now && (v.UsageLimit == 0 || v.UsedCount < v.UsageLimit));
            else if (query.Status == "Inactive")
                q = q.Where(v => !v.IsActive);
            else if (query.Status == "Expired")
                q = q.Where(v => v.EndDate < now || (v.UsageLimit > 0 && v.UsedCount >= v.UsageLimit));
        }

        var totalItems = await q.CountAsync(cancellationToken);

        var page = query.Page <= 0 ? 1 : query.Page;
        var pageSize = query.PageSize <= 0 ? 10 : Math.Min(query.PageSize, 50);
        var skip = (page - 1) * pageSize;

        var items = await q
            .OrderByDescending(v => v.CreatedAt)
            .Skip(skip)
            .Take(pageSize)
            .Select(v => new VoucherAdminListItemVm
            {
                Id = v.Id,
                Code = v.Code,
                DiscountType = v.DiscountType,
                DiscountValue = v.DiscountValue,
                MinOrderValue = v.MinOrderValue,
                MaxDiscountValue = v.MaxDiscountValue,
                UsageLimit = v.UsageLimit,
                UsedCount = v.UsedCount,
                StartDate = v.StartDate,
                EndDate = v.EndDate,
                IsActive = v.IsActive,
                CreatedAt = v.CreatedAt,
                ShopId = v.ShopId,
                ShopName = v.Shop != null && v.Shop.Profile != null ? v.Shop.Profile.ShopName : null
            })
            .ToListAsync(cancellationToken);

        var vm = new VoucherAdminListVm
        {
            Query = query,
            Items = items,
            TotalItems = totalItems,
            IsAdmin = isAdmin
        };

        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> Create()
    {
        var (account, isAdmin) = await GetCurrentUserAsync();
        if (account == null) return RedirectToAction("Index", "Home", new { area = "Admin" });
        // if (account.Role != "Admin" && account.Role != "Shop") return Forbid();

        var vm = new VoucherAdminUpsertVm
        {
            Code = GenerateRandomVoucherCode(),
            StartDate = DateTime.Now,
            EndDate = DateTime.Now.AddDays(7),
            UsageLimit = 100
        };
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(VoucherAdminUpsertVm model, CancellationToken cancellationToken)
    {
        var (account, isAdmin) = await GetCurrentUserAsync();
        if (account == null) return RedirectToAction("Index", "Home", new { area = "Admin" });
        // if (account.Role != "Admin" && account.Role != "Shop") return Forbid();

        if (model.StartDate >= model.EndDate)
        {
            ModelState.AddModelError(nameof(model.EndDate), "Ngày kết thúc phải lớn hơn ngày bắt đầu.");
        }

        if (model.DiscountType == "Percent" && model.DiscountValue > 100)
        {
            ModelState.AddModelError(nameof(model.DiscountValue), "Khuyến mãi phần trăm không vượt quá 100%.");
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        // Validate unique code (case insensitive in logic)
        var code = model.Code.Trim().ToUpper();
        var exists = await _db.Vouchers.AnyAsync(v => v.Code == code, cancellationToken);
        if (exists)
        {
            ModelState.AddModelError(nameof(model.Code), "Mã Voucher đã tồn tại.");
            return View(model);
        }

        var voucher = new Voucher
        {
            ShopId = isAdmin ? null : account.Id,
            Code = code,
            DiscountType = model.DiscountType,
            DiscountValue = model.DiscountValue,
            MinOrderValue = model.MinOrderValue,
            MaxDiscountValue = model.MaxDiscountValue,
            UsageLimit = model.UsageLimit,
            UsedCount = 0,
            StartDate = model.StartDate!.Value,
            EndDate = model.EndDate!.Value,
            IsActive = model.IsActive,
            CreatedAt = DateTime.UtcNow
        };

        _db.Vouchers.Add(voucher);
        await _db.SaveChangesAsync(cancellationToken);

        TempData["AdminToast"] = "Thêm Voucher thành công!";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        var (account, isAdmin) = await GetCurrentUserAsync();
        if (account == null) return RedirectToAction("Index", "Home", new { area = "Admin" });
        // if (account.Role != "Admin" && account.Role != "Shop") return Forbid();

        var voucher = await _db.Vouchers.FirstOrDefaultAsync(v => v.Id == id, cancellationToken);
        if (voucher == null) return NotFound();

        // if (isAdmin && voucher.ShopId != null) return Forbid(); // Admin only edits System Vouchers
        // if (!isAdmin && voucher.ShopId != account.Id) return Forbid(); // Shop only edits its own

        var vm = new VoucherAdminUpsertVm
        {
            Id = voucher.Id,
            Code = voucher.Code,
            DiscountType = voucher.DiscountType,
            DiscountValue = voucher.DiscountValue,
            MinOrderValue = voucher.MinOrderValue,
            MaxDiscountValue = voucher.MaxDiscountValue,
            UsageLimit = voucher.UsageLimit,
            StartDate = voucher.StartDate,
            EndDate = voucher.EndDate,
            IsActive = voucher.IsActive
        };

        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(VoucherAdminUpsertVm model, CancellationToken cancellationToken)
    {
        var (account, isAdmin) = await GetCurrentUserAsync();
        if (account == null) return RedirectToAction("Index", "Home", new { area = "Admin" });
        // if (account.Role != "Admin" && account.Role != "Shop") return Forbid();

        if (model.StartDate >= model.EndDate)
        {
            ModelState.AddModelError(nameof(model.EndDate), "Ngày kết thúc phải lớn hơn ngày bắt đầu.");
        }

        if (model.DiscountType == "Percent" && model.DiscountValue > 100)
        {
            ModelState.AddModelError(nameof(model.DiscountValue), "Khuyến mãi phần trăm không vượt quá 100%.");
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var voucher = await _db.Vouchers.FirstOrDefaultAsync(v => v.Id == model.Id, cancellationToken);
        if (voucher == null) return NotFound();

        // if (isAdmin && voucher.ShopId != null) return Forbid();
        // if (!isAdmin && voucher.ShopId != account.Id) return Forbid();

        var code = model.Code.Trim().ToUpper();
        if (code != voucher.Code)
        {
            var exists = await _db.Vouchers.AnyAsync(v => v.Code == code, cancellationToken);
            if (exists)
            {
                ModelState.AddModelError(nameof(model.Code), "Mã Voucher đã tồn tại.");
                return View(model);
            }
        }

        voucher.Code = code;
        voucher.DiscountType = model.DiscountType;
        voucher.DiscountValue = model.DiscountValue;
        voucher.MinOrderValue = model.MinOrderValue;
        voucher.MaxDiscountValue = model.MaxDiscountValue;
        
        // Cannot reduce usage limit below used count (optional business rule, here we allow but warn if needed, let's keep it simple)
        if (model.UsageLimit > 0 && model.UsageLimit < voucher.UsedCount)
        {
            ModelState.AddModelError(nameof(model.UsageLimit), $"Số lượng giới hạn không được nhỏ hơn số lượng đã sử dụng ({voucher.UsedCount}).");
            return View(model);
        }

        voucher.UsageLimit = model.UsageLimit;
        voucher.StartDate = model.StartDate!.Value;
        voucher.EndDate = model.EndDate!.Value;
        voucher.IsActive = model.IsActive;

        await _db.SaveChangesAsync(cancellationToken);

        TempData["AdminToast"] = "Cập nhật Voucher thành công!";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var (account, isAdmin) = await GetCurrentUserAsync();
        if (account == null) return Unauthorized();
        // if (account.Role != "Admin" && account.Role != "Shop") return Forbid();

        var voucher = await _db.Vouchers.FirstOrDefaultAsync(v => v.Id == id, cancellationToken);
        if (voucher == null) return NotFound();

        // if (isAdmin && voucher.ShopId != null) return Forbid();
        // if (!isAdmin && voucher.ShopId != account.Id) return Forbid();

        if (voucher.UsedCount > 0)
        {
            // Already used, maybe just deactivate instead of delete
            voucher.IsActive = false;
            TempData["AdminToast"] = "Voucher đã được sử dụng nên chỉ chuyển sang trạng thái ngưng kích hoạt.";
        }
        else
        {
            _db.Vouchers.Remove(voucher);
            TempData["AdminToast"] = "Đã xóa Voucher thành công!";
        }

        await _db.SaveChangesAsync(cancellationToken);
        return RedirectToAction(nameof(Index));
    }
}
