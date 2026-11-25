using System.Security.Claims;
using System.Text.Json;
using DOAN_BANHANG.Models;
using DOAN_BANHANG.Services;
using DOAN_BANHANG.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DOAN_BANHANG.Areas.Admin.Controllers;

[Area("Admin")]
[Route("Admin/[controller]/[action]")]
[Authorize(Roles = "Admin,Shop")]
public class DisputesController : Controller
{
    private readonly DataContext _db;
    private readonly DisputeAiService _aiService;
    private readonly WalletService _walletService;
    private readonly IWebHostEnvironment _env;

    public DisputesController(DataContext db, DisputeAiService aiService, WalletService walletService, IWebHostEnvironment env)
    {
        _db = db;
        _aiService = aiService;
        _walletService = walletService;
        _env = env;
    }

    private (int AccountId, bool IsAdmin, bool IsShop) GetActor()
    {
        var idRaw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        int.TryParse(idRaw, out var id);
        var isAdmin = User.IsInRole("Admin");
        var isShop = User.IsInRole("Shop");
        return (id, isAdmin, isShop);
    }

    private async Task<List<string>> SaveEvidenceFilesAsync(List<IFormFile>? files)
    {
        var urls = new List<string>();
        if (files == null || files.Count == 0) return urls;

        var uploadDir = Path.Combine(_env.WebRootPath, "uploads", "disputes");
        Directory.CreateDirectory(uploadDir);

        foreach (var file in files.Take(5))
        {
            if (file == null || file.Length == 0 || file.Length > 10 * 1024 * 1024) continue;
            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (ext is not (".jpg" or ".jpeg" or ".png" or ".webp" or ".mp4" or ".mov")) continue;

            var fileName = $"shop_disp_{Guid.NewGuid():N}{ext}";
            var filePath = Path.Combine(uploadDir, fileName);
            await using var stream = new FileStream(filePath, FileMode.Create);
            await file.CopyToAsync(stream);
            urls.Add($"/uploads/disputes/{fileName}");
        }

        return urls;
    }

    [HttpGet]
    [HttpGet("/Admin/Disputes")]
    public async Task<IActionResult> Index(string? status, string? category, string? search, CancellationToken ct)
    {
        var (accountId, isAdmin, _) = GetActor();

        var baseQuery = _db.OrderDisputes
            .AsNoTracking()
            .Include(d => d.Order)
            .Include(d => d.Customer).ThenInclude(c => c!.Profile)
            .Include(d => d.Shop).ThenInclude(s => s!.Profile)
            .AsQueryable();

        if (!isAdmin)
        {
            baseQuery = baseQuery.Where(d => d.ShopId == accountId);
        }

        var totalAll = await baseQuery.CountAsync(ct);
        var waitingAdmin = await baseQuery.CountAsync(d => d.Status == "EscalatedToAdmin", ct);
        var openOrShop = await baseQuery.CountAsync(d => d.Status == "Open" || d.Status == "ShopResponded", ct);
        var resolved = await baseQuery.CountAsync(d => d.Status.StartsWith("Resolved") || d.Status == "Closed", ct);
        var prodCount = await baseQuery.CountAsync(d => d.Category == "Product", ct);
        var shipCount = await baseQuery.CountAsync(d => d.Category == "Shipping", ct);

        var q = baseQuery;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (status == "WaitingAdmin") q = q.Where(d => d.Status == "EscalatedToAdmin");
            else if (status == "InProgress") q = q.Where(d => d.Status == "Open" || d.Status == "ShopResponded");
            else if (status == "Resolved") q = q.Where(d => d.Status.StartsWith("Resolved") || d.Status == "Closed");
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            q = q.Where(d => d.Category == category);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            q = q.Where(d => d.DisputeCode.Contains(term)
                || d.ReasonTitle.Contains(term)
                || (d.Order != null && d.Order.OrderCode.Contains(term))
                || (d.Customer != null && d.Customer.Email.Contains(term))
                || (d.Shop != null && d.Shop.Profile != null && d.Shop.Profile.ShopName != null && d.Shop.Profile.ShopName.Contains(term)));
        }

        var items = await q
            .OrderBy(d => d.Status == "EscalatedToAdmin" ? 0 : (d.Status == "Open" ? 1 : (d.Status == "ShopResponded" ? 2 : 3)))
            .ThenByDescending(d => d.UpdatedAt)
            .ToListAsync(ct);

        return View(new AdminDisputeListVm
        {
            StatusFilter = status,
            CategoryFilter = category,
            Search = search,
            IsAdmin = isAdmin,
            TotalAll = totalAll,
            WaitingAdminCount = waitingAdmin,
            OpenOrShopCount = openOrShop,
            ResolvedCount = resolved,
            ProductDisputeCount = prodCount,
            ShippingDisputeCount = shipCount,
            Items = items
        });
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id, CancellationToken ct)
    {
        var (accountId, isAdmin, isShop) = GetActor();

        var dispute = await _db.OrderDisputes
            .AsNoTracking()
            .Include(d => d.Order).ThenInclude(o => o!.Items)
            .Include(d => d.Customer).ThenInclude(c => c!.Profile)
            .Include(d => d.Shop).ThenInclude(s => s!.Profile)
            .Include(d => d.Messages.OrderBy(m => m.CreatedAt)).ThenInclude(m => m.SenderAccount).ThenInclude(a => a!.Profile)
            .FirstOrDefaultAsync(d => d.Id == id, ct);

        if (dispute == null) return NotFound();
        if (!isAdmin && dispute.ShopId != accountId) return Forbid();

        var trackingLogs = await _db.OrderTrackingLogs.AsNoTracking()
            .Where(l => l.OrderId == dispute.OrderId)
            .OrderByDescending(l => l.CreatedAt)
            .ToListAsync(ct);

        var ai = await _aiService.EvaluateDisputeAsync(dispute, ct);

        return View(new DisputeDetailsVm
        {
            Dispute = dispute,
            CustomerEvidenceUrls = DisputeAiService.ParseUrls(dispute.EvidenceUrlsJson),
            ShopEvidenceUrls = DisputeAiService.ParseUrls(dispute.ShopEvidenceUrlsJson),
            TrackingLogs = trackingLogs,
            AiAssessment = ai,
            IsShopMall = dispute.Shop?.Profile?.IsMall ?? false,
            ShopName = dispute.Shop?.Profile?.ShopName ?? dispute.Shop?.Profile?.FullName ?? $"Shop #{dispute.ShopId}",
            CustomerName = dispute.Customer?.Profile?.FullName ?? dispute.Customer?.Email ?? "Khách hàng",
            CustomerEmail = dispute.Customer?.Email ?? string.Empty,
            IsAdminView = isAdmin,
            IsShopView = isShop && !isAdmin
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PostMessage(int disputeId, string content, IFormFile? attachment, CancellationToken ct)
    {
        var (accountId, isAdmin, _) = GetActor();
        var dispute = await _db.OrderDisputes.FirstOrDefaultAsync(d => d.Id == disputeId, ct);
        if (dispute == null) return NotFound();
        if (!isAdmin && dispute.ShopId != accountId) return Forbid();

        if (!string.IsNullOrWhiteSpace(content))
        {
            var urls = attachment != null ? await SaveEvidenceFilesAsync(new List<IFormFile> { attachment }) : new List<string>();
            _db.OrderDisputeMessages.Add(new OrderDisputeMessage
            {
                DisputeId = dispute.Id,
                SenderAccountId = accountId,
                SenderRole = isAdmin ? "Admin" : "Shop",
                Content = content.Trim(),
                AttachmentUrl = urls.FirstOrDefault(),
                CreatedAt = DateTime.UtcNow
            });
            dispute.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }

        return RedirectToAction(nameof(Details), new { id = disputeId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ShopRespond(int disputeId, string responseNote, List<IFormFile>? shopEvidenceFiles, bool acceptRefund = false, CancellationToken ct = default)
    {
        var (accountId, isAdmin, _) = GetActor();
        var dispute = await _db.OrderDisputes.Include(d => d.Order).FirstOrDefaultAsync(d => d.Id == disputeId, ct);
        if (dispute == null) return NotFound();
        if (!isAdmin && dispute.ShopId != accountId) return Forbid();

        var urls = await SaveEvidenceFilesAsync(shopEvidenceFiles);
        dispute.ShopResponseNote = string.IsNullOrWhiteSpace(responseNote) ? "Gian hàng đã tiếp nhận và phản hồi." : responseNote.Trim();
        if (urls.Count > 0) dispute.ShopEvidenceUrlsJson = JsonSerializer.Serialize(urls);
        dispute.ShopRespondedAt = DateTime.UtcNow;
        dispute.UpdatedAt = DateTime.UtcNow;

        if (acceptRefund)
        {
            dispute.Status = "ResolvedBuyerWinShopFault";
            dispute.ApprovedRefundAmount = dispute.RequestedAmount;
            dispute.ResolvedAt = DateTime.UtcNow;

            if (!dispute.IsRefundedToWallet && dispute.ApprovedRefundAmount > 0 && dispute.Order != null)
            {
                var refCode = $"DISPUTE-{dispute.DisputeCode}";
                var (ok, _) = await _walletService.RefundAsync(
                    dispute.CustomerId,
                    dispute.ApprovedRefundAmount,
                    refCode,
                    $"Hoàn tiền khiếu nại #{dispute.DisputeCode} (Đơn #{dispute.Order.OrderCode}) - Gian hàng chấp thuận",
                    ct);
                if (ok) dispute.IsRefundedToWallet = true;
                if (dispute.ApprovedRefundAmount >= dispute.Order.SubTotal)
                {
                    dispute.Order.OrderStatus = "Returned";
                    dispute.Order.UpdatedAt = DateTime.UtcNow;
                }
            }

            _db.OrderDisputeMessages.Add(new OrderDisputeMessage
            {
                DisputeId = dispute.Id,
                SenderAccountId = accountId,
                SenderRole = "Shop",
                Content = $"[Gian hàng chấp thuận hoàn tiền {dispute.ApprovedRefundAmount:N0}₫] {dispute.ShopResponseNote}",
                AttachmentUrl = urls.FirstOrDefault(),
                CreatedAt = DateTime.UtcNow
            });

            TempData["AdminToast"] = $"Đã chấp thuận khiếu nại và hoàn {dispute.ApprovedRefundAmount:N0}₫ vào Ví Sellzy của khách hàng!";
        }
        else
        {
            dispute.Status = "ShopResponded";
            _db.OrderDisputeMessages.Add(new OrderDisputeMessage
            {
                DisputeId = dispute.Id,
                SenderAccountId = accountId,
                SenderRole = "Shop",
                Content = $"[Gian hàng gửi giải trình đối chứng] {dispute.ShopResponseNote}",
                AttachmentUrl = urls.FirstOrDefault(),
                CreatedAt = DateTime.UtcNow
            });
            TempData["AdminToast"] = "Đã gửi phản hồi giải trình tới Khách hàng.";
        }

        await _db.SaveChangesAsync(ct);
        return RedirectToAction(nameof(Details), new { id = disputeId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> AdminResolve(int disputeId, string verdict, decimal refundAmount, string? decisionNote, CancellationToken ct)
    {
        var (adminId, _, _) = GetActor();
        var dispute = await _db.OrderDisputes.Include(d => d.Order).FirstOrDefaultAsync(d => d.Id == disputeId, ct);
        if (dispute == null) return NotFound();

        var note = string.IsNullOrWhiteSpace(decisionNote)
            ? "Quyết định phân xử cuối cùng từ Bộ phận Trọng tài Sàn Sellzy."
            : decisionNote.Trim();

        dispute.AdminDecisionNote = note;
        dispute.ResolvedByAdminId = adminId;
        dispute.ResolvedAt = DateTime.UtcNow;
        dispute.UpdatedAt = DateTime.UtcNow;

        if (verdict is "ResolvedBuyerWinShopFault" or "ResolvedBuyerWinShippingFault")
        {
            var finalRefund = Math.Clamp(refundAmount, 0m, dispute.Order?.FinalAmount ?? dispute.RequestedAmount);
            dispute.Status = verdict;
            dispute.ApprovedRefundAmount = finalRefund;

            if (!dispute.IsRefundedToWallet && finalRefund > 0 && dispute.Order != null)
            {
                var refCode = $"DISPUTE-{dispute.DisputeCode}";
                var reasonDesc = verdict == "ResolvedBuyerWinShippingFault"
                    ? $"Hoàn tiền bảo hiểm vận chuyển GHN — Khiếu nại #{dispute.DisputeCode} (Đơn #{dispute.Order.OrderCode})"
                    : $"Hoàn tiền phán quyết trọng tài Sàn — Khiếu nại #{dispute.DisputeCode} (Đơn #{dispute.Order.OrderCode})";

                var (ok, _) = await _walletService.RefundAsync(dispute.CustomerId, finalRefund, refCode, reasonDesc, ct);
                if (ok) dispute.IsRefundedToWallet = true;
                if (verdict == "ResolvedBuyerWinShopFault" && finalRefund >= dispute.Order.SubTotal)
                {
                    dispute.Order.OrderStatus = "Returned";
                    dispute.Order.UpdatedAt = DateTime.UtcNow;
                }
            }

            if (dispute.Order != null)
            {
                _db.OrderTrackingLogs.Add(new OrderTrackingLog
                {
                    OrderId = dispute.Order.Id,
                    Status = verdict == "ResolvedBuyerWinShippingFault" ? "ShippingDisputeResolved" : "DisputeRefunded",
                    Note = verdict == "ResolvedBuyerWinShippingFault"
                        ? $"[Biên bản sự cố GHN] Sàn hoàn {finalRefund:N0}₫ cho Khách hàng (Không trừ tiền Shop). Ghi chú: {note}"
                        : $"[Phán quyết Sàn] Hoàn {finalRefund:N0}₫ vào Ví Khách hàng (Lỗi gian hàng). Ghi chú: {note}",
                    CreatedAt = DateTime.UtcNow
                });
            }

            _db.OrderDisputeMessages.Add(new OrderDisputeMessage
            {
                DisputeId = dispute.Id,
                SenderAccountId = adminId,
                SenderRole = "Admin",
                Content = verdict == "ResolvedBuyerWinShippingFault"
                    ? $"[PHÁN QUYẾT TRỌNG TÀI SÀN] Chấp thuận khiếu nại do lỗi Vận chuyển GHN. Đã hoàn {finalRefund:N0}₫ vào Ví Khách hàng (Không khấu trừ Shop). {note}"
                    : $"[PHÁN QUYẾT TRỌNG TÀI SÀN] Chấp thuận khiếu nại (Lỗi Gian hàng). Đã hoàn {finalRefund:N0}₫ vào Ví Khách hàng. {note}",
                CreatedAt = DateTime.UtcNow
            });

            TempData["AdminToast"] = $"Đã ban hành phán quyết và hoàn {finalRefund:N0}₫ vào Ví Sellzy của Khách hàng!";
        }
        else
        {
            dispute.Status = "ResolvedShopWin";
            dispute.ApprovedRefundAmount = 0m;

            _db.OrderDisputeMessages.Add(new OrderDisputeMessage
            {
                DisputeId = dispute.Id,
                SenderAccountId = adminId,
                SenderRole = "Admin",
                Content = $"[PHÁN QUYẾT TRỌNG TÀI SÀN] Bác bỏ khiếu nại — Giữ nguyên quyền lợi của Gian hàng. {note}",
                CreatedAt = DateTime.UtcNow
            });

            TempData["AdminToast"] = "Đã ban hành phán quyết bác bỏ khiếu nại và đóng hồ sơ.";
        }

        await _db.SaveChangesAsync(ct);
        return RedirectToAction(nameof(Details), new { id = disputeId });
    }
}
