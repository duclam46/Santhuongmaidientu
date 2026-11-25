using System.Security.Claims;
using System.Text.Json;
using DOAN_BANHANG.Models;
using DOAN_BANHANG.Services;
using DOAN_BANHANG.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DOAN_BANHANG.Controllers;

[Authorize]
public class DisputesController : Controller
{
    private readonly DataContext _db;
    private readonly DisputeAiService _aiService;
    private readonly IWebHostEnvironment _env;

    public DisputesController(DataContext db, DisputeAiService aiService, IWebHostEnvironment env)
    {
        _db = db;
        _aiService = aiService;
        _env = env;
    }

    private bool TryGetCurrentUserId(out int userId)
    {
        userId = 0;
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(raw, out userId) && userId > 0;
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

            var fileName = $"disp_{Guid.NewGuid():N}{ext}";
            var filePath = Path.Combine(uploadDir, fileName);
            await using var stream = new FileStream(filePath, FileMode.Create);
            await file.CopyToAsync(stream);
            urls.Add($"/uploads/disputes/{fileName}");
        }

        return urls;
    }

    [HttpGet("/my-disputes")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        if (!TryGetCurrentUserId(out var userId)) return RedirectToAction("Login", "Auth");

        var items = await _db.OrderDisputes
            .AsNoTracking()
            .Include(d => d.Order)
            .Include(d => d.Shop).ThenInclude(s => s!.Profile)
            .Where(d => d.CustomerId == userId)
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync(ct);

        return View(items);
    }

    [HttpGet("/my-disputes/create/{orderId:int}")]
    public async Task<IActionResult> Create(int orderId, CancellationToken ct)
    {
        if (!TryGetCurrentUserId(out var userId)) return RedirectToAction("Login", "Auth");

        var existing = await _db.OrderDisputes.AsNoTracking()
            .FirstOrDefaultAsync(d => d.OrderId == orderId && d.CustomerId == userId, ct);
        if (existing != null)
        {
            return RedirectToAction(nameof(Details), new { id = existing.Id });
        }

        var order = await _db.Orders.AsNoTracking()
            .Include(o => o.Items)
            .Include(o => o.Shop).ThenInclude(s => s!.Profile)
            .FirstOrDefaultAsync(o => o.Id == orderId && o.CustomerId == userId, ct);

        if (order == null) return NotFound();

        var vm = new CreateDisputeVm
        {
            OrderId = order.Id,
            OrderCode = order.OrderCode,
            OrderFinalAmount = order.FinalAmount,
            OrderSubTotal = order.SubTotal,
            OrderStatus = order.OrderStatus,
            ShopName = order.Shop?.Profile?.ShopName ?? order.Shop?.Profile?.FullName ?? $"Shop #{order.ShopId}",
            IsShopMall = order.Shop?.Profile?.IsMall ?? false,
            TrackingCode = order.TrackingCode,
            RequestedAmount = order.FinalAmount,
            OrderItems = order.Items.Select(i => new DisputeOrderItemSummaryVm
            {
                ProductName = i.ProductName,
                VariantDescription = i.VariantDescription,
                Quantity = i.Quantity,
                UnitPrice = i.UnitPrice,
                LineTotal = i.LineTotal
            }).ToList()
        };

        return View(vm);
    }

    [HttpPost("/my-disputes/create/{orderId:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(int orderId, CreateDisputeVm model, List<IFormFile>? evidenceFiles, CancellationToken ct)
    {
        if (!TryGetCurrentUserId(out var userId)) return RedirectToAction("Login", "Auth");

        var order = await _db.Orders
            .Include(o => o.Items)
            .Include(o => o.Shop).ThenInclude(s => s!.Profile)
            .FirstOrDefaultAsync(o => o.Id == orderId && o.CustomerId == userId, ct);

        if (order == null) return NotFound();

        var existing = await _db.OrderDisputes
            .FirstOrDefaultAsync(d => d.OrderId == orderId && d.CustomerId == userId, ct);
        if (existing != null)
        {
            return RedirectToAction(nameof(Details), new { id = existing.Id });
        }

        if (string.IsNullOrWhiteSpace(model.ReasonTitle) || string.IsNullOrWhiteSpace(model.Description))
        {
            ModelState.AddModelError("", "Vui lòng chọn lý do và nhập mô tả chi tiết về sự cố gặp phải.");
            model.OrderId = order.Id;
            model.OrderCode = order.OrderCode;
            model.OrderFinalAmount = order.FinalAmount;
            model.OrderSubTotal = order.SubTotal;
            model.ShopName = order.Shop?.Profile?.ShopName ?? $"Shop #{order.ShopId}";
            model.IsShopMall = order.Shop?.Profile?.IsMall ?? false;
            model.TrackingCode = order.TrackingCode;
            model.OrderItems = order.Items.Select(i => new DisputeOrderItemSummaryVm
            {
                ProductName = i.ProductName,
                VariantDescription = i.VariantDescription,
                Quantity = i.Quantity,
                UnitPrice = i.UnitPrice,
                LineTotal = i.LineTotal
            }).ToList();
            return View(model);
        }

        var uploadedUrls = await SaveEvidenceFilesAsync(evidenceFiles);
        var requestedAmt = model.RequestedAmount <= 0 || model.RequestedAmount > order.FinalAmount
            ? order.FinalAmount
            : model.RequestedAmount;

        // Khiếu nại vận chuyển GHN được đẩy thẳng lên Admin Sàn theo dõi luôn
        var initialStatus = model.Category == "Shipping" ? "EscalatedToAdmin" : "Open";

        var dispute = new OrderDispute
        {
            DisputeCode = $"KN-{DateTime.Now:yyyyMMdd}-{Random.Shared.Next(1000, 9999)}",
            OrderId = order.Id,
            CustomerId = userId,
            ShopId = order.ShopId,
            Category = model.Category,
            ReasonTitle = model.ReasonTitle.Trim(),
            Description = model.Description.Trim(),
            RequestedResolution = model.RequestedResolution,
            RequestedAmount = requestedAmt,
            EvidenceUrlsJson = uploadedUrls.Count > 0 ? JsonSerializer.Serialize(uploadedUrls) : null,
            Status = initialStatus,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.OrderDisputes.Add(dispute);
        await _db.SaveChangesAsync(ct);

        _db.OrderDisputeMessages.Add(new OrderDisputeMessage
        {
            DisputeId = dispute.Id,
            SenderAccountId = userId,
            SenderRole = "Customer",
            Content = $"[Khởi tạo khiếu nại - {dispute.ReasonTitle}] {dispute.Description}",
            AttachmentUrl = uploadedUrls.FirstOrDefault(),
            CreatedAt = DateTime.UtcNow
        });

        _db.SystemNotifications.Add(new SystemNotification
        {
            Title = $"Khiếu nại mới #{dispute.DisputeCode} cho đơn #{order.OrderCode}",
            Content = $"Khách hàng đã mở hồ sơ khiếu nại #{dispute.DisputeCode} cho đơn hàng #{order.OrderCode} với lý do: {dispute.ReasonTitle} (Yêu cầu hoàn: {dispute.RequestedAmount:N0}₫). Vui lòng kiểm tra và phản hồi trong Kênh Người Bán.",
            TargetType = "Account",
            TargetAccountId = order.ShopId,
            SentByAdminId = userId,
            CreatedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync(ct);

        TempData["DisputeToast"] = initialStatus == "EscalatedToAdmin"
            ? $"Đã gửi khiếu nại vận chuyển #{dispute.DisputeCode}. Bộ phận Trọng tài Sàn Sellzy đã tiếp nhận xử lý!"
            : $"Đã gửi khiếu nại #{dispute.DisputeCode}. Gian hàng sẽ phản hồi trong vòng 48 giờ.";

        return RedirectToAction(nameof(Details), new { id = dispute.Id });
    }

    [HttpGet("/my-disputes/details/{id:int}")]
    public async Task<IActionResult> Details(int id, CancellationToken ct)
    {
        if (!TryGetCurrentUserId(out var userId)) return RedirectToAction("Login", "Auth");

        var dispute = await _db.OrderDisputes
            .AsNoTracking()
            .Include(d => d.Order).ThenInclude(o => o!.Items)
            .Include(d => d.Shop).ThenInclude(s => s!.Profile)
            .Include(d => d.Customer).ThenInclude(c => c!.Profile)
            .Include(d => d.Messages.OrderBy(m => m.CreatedAt)).ThenInclude(m => m.SenderAccount).ThenInclude(a => a!.Profile)
            .FirstOrDefaultAsync(d => d.Id == id && d.CustomerId == userId, ct);

        if (dispute == null) return NotFound();

        var trackingLogs = await _db.OrderTrackingLogs.AsNoTracking()
            .Where(l => l.OrderId == dispute.OrderId)
            .OrderByDescending(l => l.CreatedAt)
            .ToListAsync(ct);

        var ai = await _aiService.EvaluateDisputeAsync(dispute, ct);

        var vm = new DisputeDetailsVm
        {
            Dispute = dispute,
            CustomerEvidenceUrls = DisputeAiService.ParseUrls(dispute.EvidenceUrlsJson),
            ShopEvidenceUrls = DisputeAiService.ParseUrls(dispute.ShopEvidenceUrlsJson),
            TrackingLogs = trackingLogs,
            AiAssessment = ai,
            IsShopMall = dispute.Shop?.Profile?.IsMall ?? false,
            ShopName = dispute.Shop?.Profile?.ShopName ?? dispute.Shop?.Profile?.FullName ?? $"Shop #{dispute.ShopId}",
            CustomerName = dispute.Customer?.Profile?.FullName ?? dispute.Customer?.Email ?? "Khách hàng",
            CustomerEmail = dispute.Customer?.Email ?? string.Empty
        };

        return View(vm);
    }

    [HttpPost("/my-disputes/message")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PostMessage(int disputeId, string content, IFormFile? attachment, CancellationToken ct)
    {
        if (!TryGetCurrentUserId(out var userId)) return RedirectToAction("Login", "Auth");

        var dispute = await _db.OrderDisputes.FirstOrDefaultAsync(d => d.Id == disputeId && d.CustomerId == userId, ct);
        if (dispute == null) return NotFound();

        if (!string.IsNullOrWhiteSpace(content))
        {
            var urls = attachment != null ? await SaveEvidenceFilesAsync(new List<IFormFile> { attachment }) : new List<string>();
            _db.OrderDisputeMessages.Add(new OrderDisputeMessage
            {
                DisputeId = dispute.Id,
                SenderAccountId = userId,
                SenderRole = "Customer",
                Content = content.Trim(),
                AttachmentUrl = urls.FirstOrDefault(),
                CreatedAt = DateTime.UtcNow
            });
            dispute.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }

        return RedirectToAction(nameof(Details), new { id = disputeId });
    }

    [HttpPost("/my-disputes/escalate")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EscalateToAdmin(int disputeId, string? note, CancellationToken ct)
    {
        if (!TryGetCurrentUserId(out var userId)) return RedirectToAction("Login", "Auth");

        var dispute = await _db.OrderDisputes.FirstOrDefaultAsync(d => d.Id == disputeId && d.CustomerId == userId, ct);
        if (dispute == null) return NotFound();

        if (dispute.Status is "Open" or "ShopResponded")
        {
            dispute.Status = "EscalatedToAdmin";
            dispute.UpdatedAt = DateTime.UtcNow;

            _db.OrderDisputeMessages.Add(new OrderDisputeMessage
            {
                DisputeId = dispute.Id,
                SenderAccountId = userId,
                SenderRole = "Customer",
                Content = string.IsNullOrWhiteSpace(note)
                    ? "[Yêu cầu Sàn Sellzy phân xử] Khách hàng đề nghị Quản trị viên sàn đứng ra làm trọng tài giải quyết khiếu nại."
                    : $"[Yêu cầu Sàn Sellzy phân xử] {note.Trim()}",
                CreatedAt = DateTime.UtcNow
            });

            await _db.SaveChangesAsync(ct);
            TempData["DisputeToast"] = "Đã chuyển hồ sơ lên Bộ phận Trọng tài Sàn Sellzy để phân xử!";
        }

        return RedirectToAction(nameof(Details), new { id = disputeId });
    }

    [HttpPost("/my-disputes/close")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CloseByCustomer(int disputeId, CancellationToken ct)
    {
        if (!TryGetCurrentUserId(out var userId)) return RedirectToAction("Login", "Auth");

        var dispute = await _db.OrderDisputes.FirstOrDefaultAsync(d => d.Id == disputeId && d.CustomerId == userId, ct);
        if (dispute == null) return NotFound();

        if (dispute.Status is "Open" or "ShopResponded" or "EscalatedToAdmin")
        {
            dispute.Status = "Closed";
            dispute.UpdatedAt = DateTime.UtcNow;
            _db.OrderDisputeMessages.Add(new OrderDisputeMessage
            {
                DisputeId = dispute.Id,
                SenderAccountId = userId,
                SenderRole = "Customer",
                Content = "[Khách hàng chủ động rút khiếu nại]",
                CreatedAt = DateTime.UtcNow
            });
            await _db.SaveChangesAsync(ct);
            TempData["DisputeToast"] = "Bạn đã rút khiếu nại thành công.";
        }

        return RedirectToAction(nameof(Details), new { id = disputeId });
    }
}
