using System.Security.Claims;
using DOAN_BANHANG.Models;
using DOAN_BANHANG.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DOAN_BANHANG.Areas.Admin.Controllers;

[Area("Admin")]
[Route("Admin/[controller]/[action]")]
[Authorize(Roles = "Admin")]
public class PayoutsController : Controller
{
    private readonly DataContext _db;
    private readonly WalletService _walletService;
    private readonly AuditLogService _audit;

    public PayoutsController(DataContext db, WalletService walletService, AuditLogService audit)
    {
        _db = db;
        _walletService = walletService;
        _audit = audit;
    }

    [HttpGet]
    [HttpGet("/Admin/Payouts")]
    public async Task<IActionResult> Index(string? status = null, string? search = null, CancellationToken ct = default)
    {
        var baseQ = _db.PayoutRequests
            .AsNoTracking()
            .Include(p => p.Shop)
                .ThenInclude(s => s!.Profile)
            .AsQueryable();

        ViewBag.PendingCount = await baseQ.CountAsync(p => p.Status == "Pending", ct);
        ViewBag.PendingAmount = await baseQ.Where(p => p.Status == "Pending").Select(p => (decimal?)p.Amount).SumAsync(ct) ?? 0m;
        ViewBag.ApprovedAmount = await baseQ.Where(p => p.Status == "Approved").Select(p => (decimal?)p.Amount).SumAsync(ct) ?? 0m;

        if (!string.IsNullOrWhiteSpace(status))
        {
            baseQ = baseQ.Where(p => p.Status == status.Trim());
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            baseQ = baseQ.Where(p => p.RequestCode.Contains(term)
                                  || p.AccountNumber.Contains(term)
                                  || p.AccountHolder.Contains(term)
                                  || (p.Shop != null && p.Shop.Profile != null && p.Shop.Profile.ShopName != null && p.Shop.Profile.ShopName.Contains(term)));
        }

        var items = await baseQ
            .OrderBy(p => p.Status == "Pending" ? 0 : 1)
            .ThenByDescending(p => p.CreatedAt)
            .Take(100)
            .ToListAsync(ct);

        ViewBag.SelectedStatus = status ?? "";
        ViewBag.Search = search ?? "";

        return View(items);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(int id, string? transactionRef, string? adminNote, CancellationToken ct = default)
    {
        var req = await _db.PayoutRequests.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (req == null || req.Status != "Pending")
        {
            TempData["AdminToastError"] = "Yêu cầu rút tiền không tồn tại hoặc đã được xử lý.";
            return RedirectToAction(nameof(Index));
        }

        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var adminId);
        req.Status = "Approved";
        req.TransactionRef = string.IsNullOrWhiteSpace(transactionRef) ? $"BANK-{DateTime.UtcNow:yyyyMMddHHmm}" : transactionRef.Trim();
        req.AdminNote = string.IsNullOrWhiteSpace(adminNote) ? "Đã đối soát & chuyển khoản ngân hàng thành công." : adminNote.Trim();
        req.ProcessedByAdminId = adminId > 0 ? adminId : null;
        req.ProcessedAt = DateTime.UtcNow;

        _db.SystemNotifications.Add(new SystemNotification
        {
            Title = $"✅ Lệnh rút tiền #{req.RequestCode} đã được giải ngân!",
            Content = $"Kế toán Sàn Sellzy đã phê duyệt và chuyển khoản {req.Amount:N0}₫ về {req.BankName} ({req.AccountNumber}). Mã tham chiếu NH: {req.TransactionRef}.",
            TargetType = "Account",
            TargetAccountId = req.ShopId,
            SentByAdminId = adminId > 0 ? adminId : req.ShopId,
            CreatedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(HttpContext, "ApprovePayout", "PayoutRequest", req.RequestCode,
            $"Duyệt giải ngân {req.Amount:N0}₫ cho Shop #{req.ShopId} ({req.BankName} - {req.AccountNumber}), Mã GD: {req.TransactionRef}");

        TempData["AdminToast"] = $"Đã phê duyệt giải ngân lệnh #{req.RequestCode} ({req.Amount:N0}₫) thành công!";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reject(int id, string? adminNote, CancellationToken ct = default)
    {
        var req = await _db.PayoutRequests.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (req == null || req.Status != "Pending")
        {
            TempData["AdminToastError"] = "Yêu cầu rút tiền không tồn tại hoặc đã được xử lý.";
            return RedirectToAction(nameof(Index));
        }

        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var adminId);
        var reason = string.IsNullOrWhiteSpace(adminNote) ? "Thông tin tài khoản ngân hàng chưa trùng khớp. Đã hoàn tiền về Ví Gian hàng." : adminNote.Trim();

        req.Status = "Rejected";
        req.AdminNote = reason;
        req.ProcessedByAdminId = adminId > 0 ? adminId : null;
        req.ProcessedAt = DateTime.UtcNow;

        var wallet = await _walletService.GetOrCreateWalletAsync(req.ShopId, ct);
        wallet.Balance += req.Amount;
        wallet.UpdatedAt = DateTime.UtcNow;

        _db.WalletTransactions.Add(new WalletTransaction
        {
            WalletId = wallet.WalletId,
            Amount = req.Amount,
            TransactionType = "REFUND",
            ReferenceId = $"REFUND-{req.RequestCode}",
            Description = $"Hoàn lại số dư từ lệnh rút #{req.RequestCode} bị từ chối ({reason})",
            BalanceAfter = wallet.Balance,
            CreatedAt = DateTime.UtcNow
        });

        _db.SystemNotifications.Add(new SystemNotification
        {
            Title = $"⚠️ Lệnh rút tiền #{req.RequestCode} bị từ chối (Đã hoàn Ví)",
            Content = $"Yêu cầu rút {req.Amount:N0}₫ đã được hoàn lại đầy đủ vào Số dư Ví Gian hàng. Lý do: {reason}",
            TargetType = "Account",
            TargetAccountId = req.ShopId,
            SentByAdminId = adminId > 0 ? adminId : req.ShopId,
            CreatedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(HttpContext, "RejectPayout", "PayoutRequest", req.RequestCode,
            $"Từ chối lệnh rút {req.Amount:N0}₫ của Shop #{req.ShopId} & hoàn tiền về Ví. Lý do: {reason}");

        TempData["AdminToast"] = $"Đã từ chối lệnh #{req.RequestCode} và hoàn trả {req.Amount:N0}₫ vào Ví của Shop.";
        return RedirectToAction(nameof(Index));
    }
}
