using System.Security.Claims;
using DOAN_BANHANG.Helpers;
using DOAN_BANHANG.Models;
using DOAN_BANHANG.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DOAN_BANHANG.Controllers;

[IgnoreAntiforgeryToken]
public class VouchersPublicController : Controller
{
    private readonly DataContext _db;

    public VouchersPublicController(DataContext db)
    {
        _db = db;
    }

    public sealed record SaveVoucherRequest(int VoucherId = 0, string? Code = null);
    public sealed record UnsaveVoucherRequest(int VoucherId);

    private int GetViewerId()
    {
        if (User?.Identity?.IsAuthenticated != true) return 0;
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(raw, out var id) && id > 0 ? id : 0;
    }

    private static VoucherHubCardVm MapCard(Voucher v, HashSet<int> savedIds, DateTime now)
    {
        int usedPct = 0;
        int remPct = 100;
        if (v.UsageLimit > 0)
        {
            usedPct = Math.Clamp((int)Math.Round(v.UsedCount * 100.0 / v.UsageLimit), 0, 100);
            remPct = Math.Clamp(100 - usedPct, 0, 100);
        }

        bool isExhausted = v.UsageLimit > 0 && v.UsedCount >= v.UsageLimit;
        bool isExpired = v.EndDate < now;
        bool isInactive = !v.IsActive;
        bool isDead = isExhausted || isExpired || isInactive;
        bool isExpiringSoon = !isDead && v.EndDate <= now.AddDays(3);

        string statusNote = isExpired ? "Đã hết hạn"
            : (isExhausted ? "Đã hết lượt dùng"
            : (isInactive ? "Đã tạm ngưng"
            : (isExpiringSoon ? "Sắp hết hạn" : "Sẵn sàng sử dụng")));

        var scope = (v.ScopeType ?? "All") switch
        {
            "MallOnly" => "Độc quyền Sellzy Mall",
            "ByCategory" => "Theo ngành hàng",
            "SpecificProducts" => "Sản phẩm chọn lọc",
            _ => v.ShopId.HasValue ? "Toàn Gian hàng" : "Toàn Sàn Sellzy"
        };

        return new VoucherHubCardVm
        {
            Id = v.Id,
            Code = v.Code,
            DiscountType = v.DiscountType,
            DiscountValue = v.DiscountValue,
            MinOrderValue = v.MinOrderValue,
            MaxDiscountValue = v.MaxDiscountValue,
            StartDate = v.StartDate,
            EndDate = v.EndDate,
            IsShopVoucher = v.ShopId.HasValue,
            ShopName = v.ShopId.HasValue
                ? (v.Shop?.Profile?.ShopName ?? v.Shop?.Profile?.FullName ?? "Gian hàng đối tác")
                : "Sellzy Official",
            ShopSlug = v.Shop?.Profile?.ShopSlug,
            ShopLogoUrl = ImageUrlHelper.NormalizeStaticFileUrl(v.Shop?.Profile?.ShopLogoUrl),
            IsMallShop = v.Shop?.Profile?.IsMall ?? false,
            ScopeType = v.ScopeType ?? "All",
            ScopeLabel = scope,
            TargetAudience = v.TargetAudience ?? "Public",
            IsSaved = savedIds.Contains(v.Id),
            UsageLimit = v.UsageLimit,
            UsedCount = v.UsedCount,
            UsedPercent = usedPct,
            RemainingPercent = remPct,
            IsExpiringSoon = isExpiringSoon,
            IsExpiredOrExhausted = isDead,
            StatusNote = statusNote
        };
    }

    // GET /vouchers — Trạm Săn Voucher (Giống trang Mã Giảm Giá của Shopee)
    [HttpGet("/vouchers")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var viewerId = GetViewerId();
        var now = DateTime.Now;

        var savedIds = viewerId > 0
            ? (await _db.SavedVouchers
                .AsNoTracking()
                .Where(s => s.AccountId == viewerId)
                .Select(s => s.VoucherId)
                .ToListAsync(ct)).ToHashSet()
            : new HashSet<int>();

        var activeVouchers = await _db.Vouchers
            .AsNoTracking()
            .Include(v => v.Shop)
                .ThenInclude(s => s!.Profile)
            .Where(v => v.IsActive
                     && v.StartDate <= now
                     && v.EndDate >= now
                     && v.TargetAudience != "PrivateChatOnly"
                     && (v.UsageLimit == 0 || v.UsedCount < v.UsageLimit))
            .OrderByDescending(v => v.DiscountValue)
            .ThenByDescending(v => v.CreatedAt)
            .ToListAsync(ct);

        var cards = activeVouchers.Select(v => MapCard(v, savedIds, now)).ToList();

        int activeSavedCount = viewerId > 0
            ? await _db.SavedVouchers
                .AsNoTracking()
                .CountAsync(s => s.AccountId == viewerId
                              && s.Voucher != null
                              && s.Voucher.IsActive
                              && s.Voucher.EndDate >= now
                              && (s.Voucher.UsageLimit == 0 || s.Voucher.UsedCount < s.Voucher.UsageLimit), ct)
            : 0;

        var vm = new VoucherHubVm
        {
            IsAuthenticated = viewerId > 0,
            SavedCount = activeSavedCount,
            FreeShippingVouchers = cards.Where(c => c.DiscountType == "FreeShipping").ToList(),
            PlatformVouchers = cards.Where(c => !c.IsShopVoucher && c.DiscountType != "FreeShipping").ToList(),
            ShopVouchers = cards.Where(c => c.IsShopVoucher && c.DiscountType != "FreeShipping").ToList()
        };

        return View(vm);
    }

    // GET /my-vouchers — Kho Voucher của tôi (Giống Kho Voucher cá nhân của Shopee)
    [HttpGet("/my-vouchers")]
    public async Task<IActionResult> MyVouchers(CancellationToken ct)
    {
        var viewerId = GetViewerId();
        var now = DateTime.Now;

        var vm = new MyVouchersVm
        {
            IsAuthenticated = viewerId > 0
        };

        if (viewerId <= 0)
        {
            return View(vm);
        }

        var savedRecords = await _db.SavedVouchers
            .AsNoTracking()
            .Where(s => s.AccountId == viewerId && s.Voucher != null)
            .Include(s => s.Voucher!)
                .ThenInclude(v => v.Shop)
                    .ThenInclude(sh => sh!.Profile)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(ct);

        var savedIdSet = savedRecords.Select(s => s.VoucherId).ToHashSet();

        foreach (var rec in savedRecords)
        {
            if (rec.Voucher == null) continue;
            var card = MapCard(rec.Voucher, savedIdSet, now);
            if (card.IsExpiredOrExhausted)
                vm.ExpiredOrUsedVouchers.Add(card);
            else
                vm.UsableVouchers.Add(card);
        }

        return View(vm);
    }

    [HttpPost("/vouchers/save")]
    public async Task<IActionResult> Save([FromBody] SaveVoucherRequest? req, CancellationToken ct)
    {
        if (req == null || (req.VoucherId <= 0 && string.IsNullOrWhiteSpace(req.Code)))
            return BadRequest(new { ok = false, message = "Vui lòng nhập hoặc chọn mã giảm giá hợp lệ." });

        var now = DateTime.Now;
        Voucher? voucher = null;
        if (req.VoucherId > 0)
        {
            voucher = await _db.Vouchers.AsNoTracking()
                .FirstOrDefaultAsync(v => v.Id == req.VoucherId && v.IsActive && v.EndDate >= now, ct);
        }
        if (voucher == null && !string.IsNullOrWhiteSpace(req.Code))
        {
            var norm = req.Code.Trim().ToUpperInvariant();
            voucher = await _db.Vouchers.AsNoTracking()
                .FirstOrDefaultAsync(v => v.Code.ToUpper() == norm && v.IsActive && v.EndDate >= now, ct);
        }

        if (voucher == null)
            return NotFound(new { ok = false, message = "Mã giảm giá không tồn tại, đã hết hạn hoặc chưa mở." });

        if (voucher.UsageLimit > 0 && voucher.UsedCount >= voucher.UsageLimit)
            return BadRequest(new { ok = false, message = $"Mã {voucher.Code} đã hết lượt sử dụng." });

        Response.Cookies.Append("sellzy_claimed_voucher", voucher.Code, new CookieOptions
        {
            Path = "/",
            Expires = DateTimeOffset.UtcNow.AddDays(7),
            SameSite = SameSiteMode.Lax
        });

        var viewerId = GetViewerId();
        bool alreadySaved = false;
        int savedCount = 0;

        if (viewerId > 0)
        {
            alreadySaved = await _db.SavedVouchers
                .AnyAsync(s => s.AccountId == viewerId && s.VoucherId == voucher.Id, ct);
            if (!alreadySaved)
            {
                _db.SavedVouchers.Add(new SavedVoucher
                {
                    AccountId = viewerId,
                    VoucherId = voucher.Id,
                    CreatedAt = DateTime.UtcNow
                });
                await _db.SaveChangesAsync(ct);
            }

            savedCount = await _db.SavedVouchers
                .AsNoTracking()
                .CountAsync(s => s.AccountId == viewerId
                              && s.Voucher != null
                              && s.Voucher.IsActive
                              && s.Voucher.EndDate >= now
                              && (s.Voucher.UsageLimit == 0 || s.Voucher.UsedCount < s.Voucher.UsageLimit), ct);
        }

        return Ok(new
        {
            ok = true,
            saved = true,
            alreadySaved,
            voucherId = voucher.Id,
            code = voucher.Code,
            savedCount,
            persistedToAccount = viewerId > 0,
            message = alreadySaved
                ? $"Mã {voucher.Code} đã có sẵn trong Kho Voucher của bạn!"
                : $"Đã lưu mã {voucher.Code} vào Kho Voucher thành công!"
        });
    }

    [HttpPost("/vouchers/unsave")]
    public async Task<IActionResult> Unsave([FromBody] UnsaveVoucherRequest? req, CancellationToken ct)
    {
        var viewerId = GetViewerId();
        if (viewerId <= 0) return Unauthorized(new { ok = false, message = "Vui lòng đăng nhập." });
        if (req == null || req.VoucherId <= 0) return BadRequest(new { ok = false, message = "Yêu cầu không hợp lệ." });

        var existing = await _db.SavedVouchers
            .FirstOrDefaultAsync(s => s.AccountId == viewerId && s.VoucherId == req.VoucherId, ct);

        if (existing != null)
        {
            _db.SavedVouchers.Remove(existing);
            await _db.SaveChangesAsync(ct);
        }

        return Ok(new { ok = true, message = "Đã gỡ mã khỏi Kho Voucher." });
    }
}
