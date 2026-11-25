using DOAN_BANHANG.Models;
using DOAN_BANHANG.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DOAN_BANHANG.Areas.Admin.Controllers;

[Area("Admin")]
[Route("Admin/[controller]/[action]")]
[Authorize(Roles = "Admin")]
public class ModerationController : Controller
{
    private readonly DataContext _db;
    private readonly AuditLogService _audit;

    public ModerationController(DataContext db, AuditLogService audit)
    {
        _db = db;
        _audit = audit;
    }

    [HttpGet]
    [HttpGet("/Admin/Moderation")]
    public async Task<IActionResult> Index(string tab = "products", string? q = null, string? status = null, CancellationToken ct = default)
    {
        var prodQuery = _db.Products
            .AsNoTracking()
            .Where(p => !p.IsDeleted)
            .Include(p => p.Category)
            .Include(p => p.Images)
            .Include(p => p.ShopAccount)
                .ThenInclude(s => s!.Profile)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(q) && tab == "products")
        {
            var term = q.Trim();
            prodQuery = prodQuery.Where(p => p.Name.Contains(term)
                || (p.ShopAccount != null && p.ShopAccount.Profile != null && p.ShopAccount.Profile.ShopName != null && p.ShopAccount.Profile.ShopName.Contains(term)));
        }
        if (!string.IsNullOrWhiteSpace(status) && tab == "products")
        {
            prodQuery = prodQuery.Where(p => p.Status == status.Trim());
        }

        var products = await prodQuery
            .OrderByDescending(p => p.Status == "Hidden")
            .ThenByDescending(p => p.CreatedAt)
            .Take(60)
            .ToListAsync(ct);

        var revQuery = _db.ProductReviews
            .AsNoTracking()
            .Include(r => r.Product)
            .Include(r => r.Account)
                .ThenInclude(a => a!.Profile)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(q) && tab == "reviews")
        {
            var term = q.Trim();
            revQuery = revQuery.Where(r => (r.Comment != null && r.Comment.Contains(term))
                || (r.Product != null && r.Product.Name.Contains(term)));
        }
        if (status == "Hidden" && tab == "reviews")
        {
            revQuery = revQuery.Where(r => r.IsHidden);
        }
        else if (status == "LowRating" && tab == "reviews")
        {
            revQuery = revQuery.Where(r => r.Rating <= 2);
        }

        var reviews = await revQuery
            .OrderByDescending(r => r.IsHidden)
            .ThenByDescending(r => r.CreatedAt)
            .Take(60)
            .ToListAsync(ct);

        ViewBag.ActiveTab = tab;
        ViewBag.Query = q ?? "";
        ViewBag.Status = status ?? "";
        ViewBag.Products = products;
        ViewBag.Reviews = reviews;

        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleProductStatus(int productId, string? reason, CancellationToken ct = default)
    {
        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == productId && !p.IsDeleted, ct);
        if (product == null) return NotFound();

        bool wasHidden = product.Status == "Hidden";
        product.Status = wasHidden ? "Active" : "Hidden";
        product.UpdatedAt = DateTime.UtcNow;

        var cleanReason = string.IsNullOrWhiteSpace(reason)
            ? (wasHidden ? "Sản phẩm đã được mở hiển thị trở lại trên Sàn." : "Vi phạm tiêu chuẩn đăng bán sản phẩm của Sàn Sellzy.")
            : reason.Trim();

        _db.SystemNotifications.Add(new SystemNotification
        {
            Title = wasHidden
                ? $"✅ Sản phẩm \"{product.Name}\" đã được khôi phục hiển thị"
                : $"🚫 Sản phẩm \"{product.Name}\" đã bị tạm ẩn bởi Quản trị Sàn",
            Content = $"Ghi chú kiểm duyệt: {cleanReason}",
            TargetType = "Account",
            TargetAccountId = product.ShopId,
            SentByAdminId = product.ShopId,
            CreatedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(HttpContext, wasHidden ? "RestoreProduct" : "HideViolatingProduct", "Product", product.Id.ToString(),
            $"{product.Name} (Shop #{product.ShopId}) -> Status={product.Status}. Lý do: {cleanReason}");

        TempData["AdminToast"] = wasHidden
            ? $"Đã khôi phục hiển thị sản phẩm '{product.Name}'."
            : $"Đã tạm khóa hiển thị sản phẩm '{product.Name}' và gửi cảnh báo cho Gian hàng.";

        return RedirectToAction(nameof(Index), new { tab = "products" });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleReviewVisibility(int reviewId, string? note, CancellationToken ct = default)
    {
        var review = await _db.ProductReviews.FirstOrDefaultAsync(r => r.Id == reviewId, ct);
        if (review == null) return NotFound();

        review.IsHidden = !review.IsHidden;
        review.ModerationNote = string.IsNullOrWhiteSpace(note)
            ? (review.IsHidden ? "Ẩn do spam / ngôn từ không phù hợp" : null)
            : note.Trim();
        review.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == review.ProductId, ct);
        if (product != null)
        {
            var visibleRatings = await _db.ProductReviews
                .Where(r => r.ProductId == product.Id && !r.IsHidden)
                .Select(r => r.Rating)
                .ToListAsync(ct);

            product.ReviewCount = visibleRatings.Count;
            product.AverageRating = visibleRatings.Count > 0 ? Math.Round((decimal)visibleRatings.Average(), 2) : 0m;
            await _db.SaveChangesAsync(ct);
        }

        await _audit.LogAsync(HttpContext, review.IsHidden ? "HideReview" : "RestoreReview", "ProductReview", review.Id.ToString(),
            $"Review #{review.Id} trên SP #{review.ProductId}. Ghi chú: {review.ModerationNote}");

        TempData["AdminToast"] = review.IsHidden
            ? "Đã ẩn đánh giá vi phạm và tính lại điểm sao trung bình của sản phẩm."
            : "Đã hiển thị lại đánh giá và cập nhật điểm sao sản phẩm.";

        return RedirectToAction(nameof(Index), new { tab = "reviews" });
    }
}
