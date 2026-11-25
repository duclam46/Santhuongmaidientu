using System.Security.Claims;
using DOAN_BANHANG.Helpers;
using DOAN_BANHANG.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DOAN_BANHANG.Areas.Admin.Controllers;

[Area("Admin")]
[Route("Admin/[controller]/[action]")]
[Authorize(Roles = "Shop,Admin")]
public class ReviewsController : Controller
{
    private readonly DataContext _db;

    public ReviewsController(DataContext db)
    {
        _db = db;
    }

    private async Task<(int ShopId, string ShopName, bool IsAdmin)> ResolveShopContextAsync(int? queryShopId)
    {
        var role = User.FindFirstValue(ClaimTypes.Role);
        var idStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        int.TryParse(idStr, out int currentUserId);

        bool isAdmin = string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase);
        int targetShopId = currentUserId;

        if (isAdmin)
        {
            if (queryShopId.HasValue && queryShopId.Value > 0)
            {
                targetShopId = queryShopId.Value;
            }
            else
            {
                var firstShop = await _db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Role == "Shop" && !a.IsDeleted);
                targetShopId = firstShop?.Id ?? currentUserId;
            }
        }

        var shopAcc = await _db.Accounts
            .AsNoTracking()
            .Include(a => a.Profile)
            .FirstOrDefaultAsync(a => a.Id == targetShopId);

        string shopName = shopAcc?.Profile?.ShopName ?? shopAcc?.Profile?.FullName ?? "Gian hàng Sellzy";
        return (targetShopId, shopName, isAdmin);
    }

    [HttpGet]
    [HttpGet("/Admin/Reviews")]
    public async Task<IActionResult> Index(string? filter = "all", int? rating = null, string? search = null, int? shopId = null)
    {
        var (targetShopId, shopName, isAdmin) = await ResolveShopContextAsync(shopId);

        var query = _db.ProductReviews
            .AsNoTracking()
            .Include(r => r.Product)
                .ThenInclude(p => p!.Images)
            .Include(r => r.Account)
                .ThenInclude(a => a!.Profile)
            .Where(r => r.Product != null && !r.Product.IsDeleted && (isAdmin && !shopId.HasValue ? true : r.Product.ShopId == targetShopId));

        var allReviews = await query
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();

        int totalCount = allReviews.Count;
        double avgRating = totalCount > 0 ? Math.Round(allReviews.Average(r => r.Rating), 1) : 5.0;
        int positiveCount = allReviews.Count(r => r.Rating >= 4);
        int neutralCount = allReviews.Count(r => r.Rating == 3);
        int negativeCount = allReviews.Count(r => r.Rating <= 2);
        int unrepliedCount = allReviews.Count(r => string.IsNullOrWhiteSpace(r.SellerReply));
        int repliedCount = totalCount - unrepliedCount;
        int replyRatePct = totalCount > 0 ? (int)Math.Round(repliedCount * 100.0 / totalCount) : 100;

        var filtered = allReviews.AsEnumerable();
        var normFilter = (filter ?? "all").Trim().ToLowerInvariant();
        if (normFilter == "unreplied")
            filtered = filtered.Where(r => string.IsNullOrWhiteSpace(r.SellerReply));
        else if (normFilter == "replied")
            filtered = filtered.Where(r => !string.IsNullOrWhiteSpace(r.SellerReply));
        else if (normFilter == "negative")
            filtered = filtered.Where(r => r.Rating <= 3);
        else if (normFilter == "positive")
            filtered = filtered.Where(r => r.Rating >= 4);

        if (rating.HasValue && rating.Value >= 1 && rating.Value <= 5)
            filtered = filtered.Where(r => r.Rating == rating.Value);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            filtered = filtered.Where(r =>
                (r.Comment != null && r.Comment.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                (r.Product?.Name != null && r.Product.Name.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                (r.Account?.Profile?.FullName != null && r.Account.Profile.FullName.Contains(term, StringComparison.OrdinalIgnoreCase)));
        }

        var items = filtered.Select(r =>
        {
            var custName = r.Account?.Profile?.FullName ?? r.Account?.Email ?? "Khách hàng";
            var prodName = r.Product?.Name ?? "Sản phẩm";
            var prodImg = ImageUrlHelper.NormalizeStaticFileUrl(
                r.Product?.Images.FirstOrDefault(i => i.IsPrimary)?.ImageUrl
                ?? r.Product?.Images.FirstOrDefault()?.ImageUrl);

            string sentimentLabel;
            string sentimentBadge;
            string suggest1;
            string suggest2;

            if (r.Rating >= 4)
            {
                sentimentLabel = "Tích cực • Hài lòng cao";
                sentimentBadge = "bg-success-subtle text-success-emphasis border-success-subtle";
                suggest1 = $"Cảm ơn {custName} đã tin tưởng lựa chọn \"{prodName}\" tại {shopName}! Sự hài lòng của bạn là động lực lớn để Shop tiếp tục nâng cao chất lượng sản phẩm và dịch vụ. Hẹn gặp lại bạn ở các đơn hàng tiếp theo nhé!";
                suggest2 = $"{shopName} chân thành cảm ơn đánh giá {r.Rating} sao từ {custName}! Đừng quên theo dõi gian hàng để nhận ngay mã Voucher ưu đãi riêng cho lần mua sắm tới nhé ạ.";
            }
            else if (r.Rating == 3)
            {
                sentimentLabel = "Trung lập • Cần ghi nhận góp ý";
                sentimentBadge = "bg-warning-subtle text-warning-emphasis border-warning-subtle";
                suggest1 = $"Chào {custName}, {shopName} cảm ơn bạn đã dành thời gian đánh giá sản phẩm \"{prodName}\". Shop rất mong nhận thêm chia sẻ chi tiết qua mục Tin nhắn để cải thiện trải nghiệm tốt hơn và gửi tặng bạn mã ưu đãi bù đắp cho đơn sau ạ!";
                suggest2 = $"Cảm ơn góp ý thẳng thắn của {custName}. {shopName} đã ghi nhận phản hồi để nâng cấp quy trình đóng gói & kiểm tra sản phẩm \"{prodName}\" kỹ lưỡng hơn.";
            }
            else
            {
                sentimentLabel = "Tiêu cực • Cần xử lý ưu tiên";
                sentimentBadge = "bg-danger-subtle text-danger-emphasis border-danger-subtle";
                suggest1 = $"Chào {custName}, {shopName} thành thật xin lỗi vì trải nghiệm chưa trọn vẹn với sản phẩm \"{prodName}\". Bạn vui lòng kiểm tra hộp thư Sellzy Chat để bộ phận CSKH của Shop hỗ trợ đổi mới hoặc hoàn tiền nhanh nhất cho bạn ngay hôm nay ạ!";
                suggest2 = $"{shopName} rất tiếc về sự cố phát sinh trên đơn hàng của {custName}. Shop cam kết bảo vệ 100% quyền lợi khách hàng và đã chủ động liên hệ để xử lý thỏa đáng cho bạn.";
            }

            return new SellerReviewItemVm
            {
                Id = r.Id,
                ProductId = r.ProductId,
                ProductName = prodName,
                ProductImageUrl = prodImg ?? "",
                CustomerName = custName,
                CustomerEmail = r.Account?.Email ?? "",
                Rating = r.Rating,
                Comment = r.Comment ?? "(Khách hàng chấm sao không để lại bình luận)",
                SellerReply = r.SellerReply,
                RepliedAt = r.RepliedAt,
                CreatedAt = r.CreatedAt,
                AiSentimentLabel = sentimentLabel,
                AiSentimentBadgeClass = sentimentBadge,
                AiSuggestedReply1 = suggest1,
                AiSuggestedReply2 = suggest2
            };
        }).ToList();

        var vm = new SellerReviewDashboardVm
        {
            ShopId = targetShopId,
            ShopName = shopName,
            Filter = normFilter,
            RatingFilter = rating,
            Search = search ?? "",
            TotalReviews = totalCount,
            AverageRating = avgRating,
            PositiveCount = positiveCount,
            NeutralCount = neutralCount,
            NegativeCount = negativeCount,
            UnrepliedCount = unrepliedCount,
            ReplyRatePercent = replyRatePct,
            Reviews = items
        };

        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reply(int reviewId, string replyText, string? returnFilter = "all")
    {
        if (string.IsNullOrWhiteSpace(replyText))
        {
            TempData["AdminToast"] = "Vui lòng nhập nội dung phản hồi cho khách hàng.";
            return RedirectToAction(nameof(Index), new { filter = returnFilter });
        }

        var role = User.FindFirstValue(ClaimTypes.Role);
        var idStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        int.TryParse(idStr, out int currentUserId);
        bool isAdmin = string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase);

        var review = await _db.ProductReviews
            .Include(r => r.Product)
            .FirstOrDefaultAsync(r => r.Id == reviewId);

        if (review == null) return NotFound();
        if (!isAdmin && review.Product?.ShopId != currentUserId) return Forbid();

        review.SellerReply = replyText.Trim();
        review.RepliedAt = DateTime.UtcNow;
        review.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        TempData["AdminToast"] = "Đã lưu và hiển thị phản hồi của Gian hàng trên trang sản phẩm!";
        return RedirectToAction(nameof(Index), new { filter = returnFilter });
    }
}

public class SellerReviewDashboardVm
{
    public int ShopId { get; set; }
    public string ShopName { get; set; } = string.Empty;
    public string Filter { get; set; } = "all";
    public int? RatingFilter { get; set; }
    public string Search { get; set; } = string.Empty;
    public int TotalReviews { get; set; }
    public double AverageRating { get; set; }
    public int PositiveCount { get; set; }
    public int NeutralCount { get; set; }
    public int NegativeCount { get; set; }
    public int UnrepliedCount { get; set; }
    public int ReplyRatePercent { get; set; }
    public List<SellerReviewItemVm> Reviews { get; set; } = new();
}

public class SellerReviewItemVm
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string ProductImageUrl { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;
    public int Rating { get; set; }
    public string Comment { get; set; } = string.Empty;
    public string? SellerReply { get; set; }
    public DateTime? RepliedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public string AiSentimentLabel { get; set; } = string.Empty;
    public string AiSentimentBadgeClass { get; set; } = string.Empty;
    public string AiSuggestedReply1 { get; set; } = string.Empty;
    public string AiSuggestedReply2 { get; set; } = string.Empty;
}
