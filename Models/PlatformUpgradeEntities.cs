using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DOAN_BANHANG.Models;

[Table("SavedVouchers")]
public class SavedVoucher
{
    [Key]
    public int Id { get; set; }

    public int AccountId { get; set; }

    [ForeignKey(nameof(AccountId))]
    public Account? Account { get; set; }

    public int VoucherId { get; set; }

    [ForeignKey(nameof(VoucherId))]
    public Voucher? Voucher { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

[Table("ShopFollowers")]
public class ShopFollower
{
    [Key]
    public int Id { get; set; }

    public int FollowerAccountId { get; set; }

    [ForeignKey(nameof(FollowerAccountId))]
    public Account? FollowerAccount { get; set; }

    public int ShopId { get; set; }

    [ForeignKey(nameof(ShopId))]
    public Account? Shop { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

[Table("PayoutRequests")]
public class PayoutRequest
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(50)]
    public string RequestCode { get; set; } = string.Empty;

    public int ShopId { get; set; }

    [ForeignKey(nameof(ShopId))]
    public Account? Shop { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal Amount { get; set; }

    [Required]
    [MaxLength(150)]
    public string BankName { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string AccountNumber { get; set; } = string.Empty;

    [Required]
    [MaxLength(150)]
    public string AccountHolder { get; set; } = string.Empty;

    [Required]
    [MaxLength(30)]
    public string Status { get; set; } = "Pending"; // Pending | Approved | Rejected

    [MaxLength(500)]
    public string? AdminNote { get; set; }

    [MaxLength(150)]
    public string? TransactionRef { get; set; }

    public int? ProcessedByAdminId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? ProcessedAt { get; set; }
}

[Table("AuditLogs")]
public class AuditLog
{
    [Key]
    public int Id { get; set; }

    public int? ActorAccountId { get; set; }

    [MaxLength(255)]
    public string? ActorEmail { get; set; }

    [MaxLength(50)]
    public string? ActorRole { get; set; }

    [Required]
    [MaxLength(100)]
    public string Action { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string EntityType { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? EntityId { get; set; }

    [MaxLength(2000)]
    public string? Detail { get; set; }

    [MaxLength(80)]
    public string? IpAddress { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

[Table("HomeBanners")]
public class HomeBanner
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(400)]
    public string? Subtitle { get; set; }

    [MaxLength(80)]
    public string? BadgeText { get; set; }

    [Required]
    [MaxLength(500)]
    public string ImageUrl { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? LinkUrl { get; set; }

    [MaxLength(80)]
    public string? ButtonText { get; set; }

    public int SortOrder { get; set; } = 0;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

[Table("PlatformCampaigns")]
public class PlatformCampaign
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(200)]
    public string Slug { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? BannerUrl { get; set; }

    [MaxLength(30)]
    public string? ThemeColor { get; set; } = "#008374";

    [MaxLength(1000)]
    public string? Description { get; set; }

    [MaxLength(100)]
    public string? DiscountUpToText { get; set; }

    public DateTime StartDate { get; set; } = DateTime.UtcNow;

    public DateTime EndDate { get; set; } = DateTime.UtcNow.AddDays(7);

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
