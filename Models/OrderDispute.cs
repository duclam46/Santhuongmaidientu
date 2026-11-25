using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DOAN_BANHANG.Models;

[Table("OrderDisputes")]
public class OrderDispute
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(40)]
    public string DisputeCode { get; set; } = string.Empty;

    public int OrderId { get; set; }

    [ForeignKey(nameof(OrderId))]
    public Order? Order { get; set; }

    public int CustomerId { get; set; }

    [ForeignKey(nameof(CustomerId))]
    public Account? Customer { get; set; }

    public int ShopId { get; set; }

    [ForeignKey(nameof(ShopId))]
    public Account? Shop { get; set; }

    /// <summary>
    /// Product | Shipping | Service
    /// </summary>
    [Required]
    [MaxLength(30)]
    public string Category { get; set; } = "Product";

    [Required]
    [MaxLength(150)]
    public string ReasonTitle { get; set; } = string.Empty;

    [Required]
    [MaxLength(2000)]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// ReturnAndRefund | RefundOnly | PartialRefund | ShippingInvestigation
    /// </summary>
    [Required]
    [MaxLength(40)]
    public string RequestedResolution { get; set; } = "ReturnAndRefund";

    [Column(TypeName = "decimal(18,2)")]
    public decimal RequestedAmount { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal ApprovedRefundAmount { get; set; }

    [MaxLength(4000)]
    public string? EvidenceUrlsJson { get; set; }

    /// <summary>
    /// Open | ShopResponded | EscalatedToAdmin | ResolvedBuyerWinShopFault | ResolvedBuyerWinShippingFault | ResolvedShopWin | Closed
    /// </summary>
    [Required]
    [MaxLength(40)]
    public string Status { get; set; } = "Open";

    [MaxLength(1500)]
    public string? ShopResponseNote { get; set; }

    [MaxLength(4000)]
    public string? ShopEvidenceUrlsJson { get; set; }

    public DateTime? ShopRespondedAt { get; set; }

    [MaxLength(1500)]
    public string? AdminDecisionNote { get; set; }

    public int? ResolvedByAdminId { get; set; }

    public DateTime? ResolvedAt { get; set; }

    public bool IsRefundedToWallet { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<OrderDisputeMessage> Messages { get; set; } = new List<OrderDisputeMessage>();
}

[Table("OrderDisputeMessages")]
public class OrderDisputeMessage
{
    [Key]
    public int Id { get; set; }

    public int DisputeId { get; set; }

    [ForeignKey(nameof(DisputeId))]
    public OrderDispute? Dispute { get; set; }

    public int SenderAccountId { get; set; }

    [ForeignKey(nameof(SenderAccountId))]
    public Account? SenderAccount { get; set; }

    /// <summary>
    /// Customer | Shop | Admin | System
    /// </summary>
    [Required]
    [MaxLength(20)]
    public string SenderRole { get; set; } = "Customer";

    [Required]
    [MaxLength(2000)]
    public string Content { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? AttachmentUrl { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
