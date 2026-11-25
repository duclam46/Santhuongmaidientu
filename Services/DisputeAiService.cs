using System.Text.Json;
using DOAN_BANHANG.Models;
using DOAN_BANHANG.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace DOAN_BANHANG.Services;

public class DisputeAiService
{
    private readonly DataContext _db;

    public DisputeAiService(DataContext db)
    {
        _db = db;
    }

    public async Task<DisputeAiAssessmentVm> EvaluateDisputeAsync(OrderDispute dispute, CancellationToken ct = default)
    {
        var findings = new List<string>();

        // 1. Lịch sử & Uy tín Khách hàng
        var buyerOrders = await _db.Orders.AsNoTracking()
            .Where(o => o.CustomerId == dispute.CustomerId)
            .Select(o => new { o.OrderStatus })
            .ToListAsync(ct);

        var buyerCompleted = buyerOrders.Count(o => o.OrderStatus == "Completed" || o.OrderStatus == "Delivered");
        var buyerCancelled = buyerOrders.Count(o => o.OrderStatus == "Cancelled" || o.OrderStatus == "Returned");
        var buyerPriorDisputes = await _db.OrderDisputes.AsNoTracking()
            .CountAsync(d => d.CustomerId == dispute.CustomerId && d.Id != dispute.Id, ct);

        int buyerTrust = 80;
        if (buyerCompleted >= 3) buyerTrust += 10;
        if (buyerOrders.Count > 0 && (double)buyerCancelled / buyerOrders.Count > 0.35) buyerTrust -= 18;
        if (buyerPriorDisputes >= 3) buyerTrust -= 15;
        buyerTrust = Math.Clamp(buyerTrust, 30, 98);

        findings.Add($"Khách hàng có {buyerCompleted} đơn hoàn tất / {buyerOrders.Count} tổng đơn và {buyerPriorDisputes} khiếu nại trước đó (Điểm uy tín: {buyerTrust}/100).");

        // 2. Uy tín & Lịch sử Gian hàng
        var shopProfile = await _db.UserProfiles.AsNoTracking()
            .FirstOrDefaultAsync(p => p.AccountId == dispute.ShopId, ct);
        var isMall = shopProfile?.IsMall ?? false;

        var shopOrders = await _db.Orders.AsNoTracking()
            .Where(o => o.ShopId == dispute.ShopId)
            .Select(o => new { o.OrderStatus })
            .ToListAsync(ct);
        var shopCancelled = shopOrders.Count(o => o.OrderStatus == "Cancelled" || o.OrderStatus == "Returned");
        var shopPriorFaults = await _db.OrderDisputes.AsNoTracking()
            .CountAsync(d => d.ShopId == dispute.ShopId && d.Status == "ResolvedBuyerWinShopFault" && d.Id != dispute.Id, ct);

        int shopTrust = isMall ? 88 : 78;
        if (shopOrders.Count > 0 && (double)shopCancelled / shopOrders.Count > 0.15) shopTrust -= 12;
        if (shopPriorFaults >= 2) shopTrust -= 18;
        shopTrust = Math.Clamp(shopTrust, 35, 98);

        findings.Add($"Gian hàng thuộc hạng {(isMall ? "Sellzy Mall (Chính hãng)" : "Shop Thường")}, có {shopPriorFaults} lần bị xử thua khiếu nại trước đó (Điểm uy tín: {shopTrust}/100).");

        // 3. Phân tích bằng chứng đính kèm
        var custEvidenceCount = ParseUrls(dispute.EvidenceUrlsJson).Count;
        var shopEvidenceCount = ParseUrls(dispute.ShopEvidenceUrlsJson).Count;

        if (custEvidenceCount > 0)
            findings.Add($"Khách hàng đã cung cấp {custEvidenceCount} tệp ảnh/video làm bằng chứng đối chiếu.");
        else
            findings.Add("Cảnh báo: Khách hàng chưa đính kèm ảnh/video bằng chứng thực tế.");

        if (!string.IsNullOrWhiteSpace(dispute.ShopResponseNote))
            findings.Add($"Gian hàng đã gửi giải trình phản hồi kèm {shopEvidenceCount} tệp bằng chứng đối chứng.");
        else
            findings.Add("Gian hàng chưa phản hồi giải trình cho hồ sơ khiếu nại này.");

        // 4. Phân tích Hành trình Vận chuyển GHN
        var trackingLogs = await _db.OrderTrackingLogs.AsNoTracking()
            .Where(l => l.OrderId == dispute.OrderId)
            .OrderBy(l => l.CreatedAt)
            .ToListAsync(ct);

        bool shippingDelayed = false;
        if (trackingLogs.Count >= 2)
        {
            var transitDays = (trackingLogs.Last().CreatedAt - trackingLogs.First().CreatedAt).TotalDays;
            if (transitDays >= 5)
            {
                shippingDelayed = true;
                findings.Add($"Phát hiện bất thường vận chuyển GHN: Thời gian luân chuyển kéo dài {transitDays:0.0} ngày (vượt ngưỡng chuẩn 5 ngày).");
            }
        }
        else if (dispute.Category == "Shipping")
        {
            findings.Add("Nhật ký vận đơn GHN có ít điểm quét — cần đối chiếu trực tiếp với mã vận đơn của đơn vị giao hàng.");
        }

        // 5. Kiểm tra chính sách đổi trả của sản phẩm trong đơn
        var orderItems = await _db.OrderItems.AsNoTracking()
            .Include(i => i.Variant)
                .ThenInclude(v => v!.Product)
                    .ThenInclude(p => p!.Category)
            .Where(i => i.OrderId == dispute.OrderId)
            .ToListAsync(ct);

        var hasNonReturnableItem = orderItems.Any(i => i.Variant?.Product != null && !i.Variant.Product.IsReturnable);
        if (hasNonReturnableItem)
        {
            findings.Add("Lưu ý ngành hàng: Đơn có sản phẩm đặc thù không hỗ trợ trả hàng (như thực phẩm/đồ tươi sống) — ưu tiên phương án Hoàn tiền trực tiếp nếu đúng lỗi.");
        }

        // 6. Tính Điểm tin cậy khiếu nại & Đề xuất Phán quyết AI
        int validity = 55;
        if (custEvidenceCount >= 2) validity += 20;
        else if (custEvidenceCount == 1) validity += 12;
        else validity -= 18;

        if (string.IsNullOrWhiteSpace(dispute.ShopResponseNote)) validity += 10;
        else if (shopEvidenceCount > 0) validity -= 10;

        if (shippingDelayed && dispute.Category == "Shipping") validity += 18;
        if (buyerTrust >= 85) validity += 8;
        if (buyerTrust < 60) validity -= 15;
        if (shopPriorFaults >= 2) validity += 10;
        if (isMall && dispute.ReasonTitle.Contains("giả", StringComparison.OrdinalIgnoreCase))
        {
            validity += 8;
            findings.Add("Cảnh báo nghiêm trọng Sellzy Mall: Khiếu nại liên quan đến nghi vấn hàng giả/nhái — cần kiểm tra hóa đơn chứng từ nhập hàng của Shop.");
        }

        validity = Math.Clamp(validity, 15, 97);

        string riskLevel;
        string riskBadge;
        if (validity >= 75)
        {
            riskLevel = "Ưu tiên bảo vệ Khách hàng";
            riskBadge = "bg-success-subtle text-success-emphasis border-success-subtle";
        }
        else if (validity >= 50)
        {
            riskLevel = "Cần thẩm định thêm bằng chứng";
            riskBadge = "bg-warning-subtle text-warning-emphasis border-warning-subtle";
        }
        else
        {
            riskLevel = "Cảnh báo rủi ro khiếu nại yếu";
            riskBadge = "bg-danger-subtle text-danger-emphasis border-danger-subtle";
        }

        string verdictCode;
        string verdictLabel;
        decimal recRefund = dispute.RequestedAmount;

        if (dispute.Category == "Shipping" && validity >= 65)
        {
            verdictCode = "ResolvedBuyerWinShippingFault";
            verdictLabel = "Khuyến nghị: Hoàn tiền cho Khách (Lỗi Vận chuyển GHN — Không trừ tiền Shop)";
        }
        else if (validity >= 68)
        {
            verdictCode = "ResolvedBuyerWinShopFault";
            verdictLabel = hasNonReturnableItem
                ? "Khuyến nghị: Hoàn tiền trực tiếp cho Khách không cần trả hàng (Lỗi Gian hàng)"
                : "Khuyến nghị: Chấp thuận khiếu nại & Hoàn tiền vào Ví Khách (Lỗi Gian hàng)";
        }
        else if (validity < 45 && !string.IsNullOrWhiteSpace(dispute.ShopResponseNote))
        {
            verdictCode = "ResolvedShopWin";
            verdictLabel = "Khuyến nghị: Bác bỏ khiếu nại (Bằng chứng của Khách chưa đủ thuyết phục)";
            recRefund = 0m;
        }
        else
        {
            verdictCode = "NeedMoreEvidence";
            verdictLabel = "Khuyến nghị: Yêu cầu 2 bên bổ sung video mở hộp / ảnh đóng gói trước khi phán quyết";
        }

        return new DisputeAiAssessmentVm
        {
            ClaimValidityScore = validity,
            BuyerTrustScore = buyerTrust,
            ShopTrustScore = shopTrust,
            RiskLevel = riskLevel,
            RiskBadgeClass = riskBadge,
            RecommendedVerdictCode = verdictCode,
            RecommendedVerdictLabel = verdictLabel,
            RecommendedRefundAmount = recRefund,
            Findings = findings
        };
    }

    public static List<string> ParseUrls(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new List<string>();
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
        }
        catch
        {
            return new List<string>();
        }
    }
}
