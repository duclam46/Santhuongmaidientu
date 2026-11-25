using System.ComponentModel.DataAnnotations;
using DOAN_BANHANG.Models;

namespace DOAN_BANHANG.ViewModels;

public class CreateDisputeVm
{
    public int OrderId { get; set; }
    public string OrderCode { get; set; } = string.Empty;
    public decimal OrderFinalAmount { get; set; }
    public decimal OrderSubTotal { get; set; }
    public string OrderStatus { get; set; } = string.Empty;
    public string ShopName { get; set; } = string.Empty;
    public bool IsShopMall { get; set; }
    public string? TrackingCode { get; set; }

    [Required(ErrorMessage = "Vui lòng chọn nhóm khiếu nại.")]
    public string Category { get; set; } = "Product"; // Product | Shipping | Service

    [Required(ErrorMessage = "Vui lòng chọn lý do khiếu nại.")]
    [MaxLength(150)]
    public string ReasonTitle { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng mô tả chi tiết vấn đề gặp phải.")]
    [MaxLength(2000)]
    public string Description { get; set; } = string.Empty;

    [Required]
    public string RequestedResolution { get; set; } = "ReturnAndRefund";

    [Range(0, double.MaxValue, ErrorMessage = "Số tiền yêu cầu hoàn không hợp lệ.")]
    public decimal RequestedAmount { get; set; }

    public List<DisputeOrderItemSummaryVm> OrderItems { get; set; } = new();
}

public class DisputeOrderItemSummaryVm
{
    public string ProductName { get; set; } = string.Empty;
    public string? VariantDescription { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; set; }
}

public class DisputeAiAssessmentVm
{
    public int ClaimValidityScore { get; set; } // 0 - 100
    public int BuyerTrustScore { get; set; }    // 0 - 100
    public int ShopTrustScore { get; set; }     // 0 - 100
    public string RiskLevel { get; set; } = "Trung bình"; // Thấp | Trung bình | Cao
    public string RiskBadgeClass { get; set; } = "bg-warning-subtle text-warning-emphasis";
    public string RecommendedVerdictCode { get; set; } = "NeedMoreEvidence";
    public string RecommendedVerdictLabel { get; set; } = string.Empty;
    public decimal RecommendedRefundAmount { get; set; }
    public List<string> Findings { get; set; } = new();
}

public class AdminDisputeListVm
{
    public string? StatusFilter { get; set; }
    public string? CategoryFilter { get; set; }
    public string? Search { get; set; }
    public bool IsAdmin { get; set; }

    public int TotalAll { get; set; }
    public int WaitingAdminCount { get; set; }
    public int OpenOrShopCount { get; set; }
    public int ResolvedCount { get; set; }
    public int ProductDisputeCount { get; set; }
    public int ShippingDisputeCount { get; set; }

    public List<OrderDispute> Items { get; set; } = new();
}

public class DisputeDetailsVm
{
    public OrderDispute Dispute { get; set; } = null!;
    public List<string> CustomerEvidenceUrls { get; set; } = new();
    public List<string> ShopEvidenceUrls { get; set; } = new();
    public List<OrderTrackingLog> TrackingLogs { get; set; } = new();
    public DisputeAiAssessmentVm AiAssessment { get; set; } = new();
    public bool IsShopMall { get; set; }
    public string ShopName { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;
    public bool IsAdminView { get; set; }
    public bool IsShopView { get; set; }
}
