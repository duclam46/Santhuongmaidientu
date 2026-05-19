using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DOAN_BANHANG.Models;

[Table("ReturnRequests")]
public class ReturnRequest
{
    [Key]
    public int Id { get; set; }

    public int OrderId { get; set; }

    [ForeignKey(nameof(OrderId))]
    public Order? Order { get; set; }

    public int CustomerId { get; set; }

    [ForeignKey(nameof(CustomerId))]
    public Account? Customer { get; set; }

    [Required]
    [MaxLength(500)]
    public string Reason { get; set; } = string.Empty;

    /// <summary>
    /// JSON array of image/video URLs uploaded by customer as evidence.
    /// </summary>
    [MaxLength(4000)]
    public string? MediaUrlsJson { get; set; }

    /// <summary>
    /// Status: Pending, Approved, Rejected
    /// </summary>
    [Required]
    [MaxLength(30)]
    public string Status { get; set; } = "Pending";

    /// <summary>
    /// Reason for rejection (if rejected by shop).
    /// </summary>
    [MaxLength(500)]
    public string? RejectionNote { get; set; }

    /// <summary>
    /// GHN return tracking code (set after shop approves and GHN picks up).
    /// </summary>
    [MaxLength(100)]
    public string? ReturnTrackingCode { get; set; }

    /// <summary>
    /// Estimated pickup time from GHN (unix timestamp or readable).
    /// </summary>
    [MaxLength(100)]
    public string? EstimatedPickupTime { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
