using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DOAN_BANHANG.Models;

[Table("UserProfiles")]
public class UserProfile
{
    [Key]
    public int Id { get; set; }

    public int AccountId { get; set; }

    [ForeignKey(nameof(AccountId))]
    public Account? Account { get; set; }

    [MaxLength(150)]
    public string? FullName { get; set; }

    [MaxLength(20)]
    public string? PhoneNumber { get; set; }

    [MaxLength(500)]
    public string? AvatarUrl { get; set; }

    // Shop info (if Role = Shop)
    [MaxLength(200)]
    public string? ShopName { get; set; }

    [MaxLength(200)]
    public string? ShopSlug { get; set; }

    [MaxLength(500)]
    public string? ShopLogoUrl { get; set; }

    public string? ShopDescription { get; set; }

    [NotMapped]
    public string SellerType { get; set; } = "Individual"; // Individual | Enterprise

    [NotMapped]
    public decimal AnnualRevenueTracker { get; set; } = 0m;

    // Shop Tier & Mall Certification (Standard | Mall)
    public bool IsMall { get; set; } = false;

    [MaxLength(30)]
    public string ShopTier { get; set; } = "Standard"; // Standard | Mall

    [MaxLength(30)]
    public string MallStatus { get; set; } = "None"; // None | Pending | Approved | Rejected

    [MaxLength(255)]
    public string? BusinessName { get; set; }

    [MaxLength(50)]
    public string? BusinessTaxCode { get; set; }

    [MaxLength(200)]
    public string? BrandName { get; set; }

    [MaxLength(500)]
    public string? BusinessLicenseUrl { get; set; }

    [MaxLength(500)]
    public string? BrandAuthorizationUrl { get; set; }

    [MaxLength(500)]
    public string? MallRejectionReason { get; set; }

    public DateTime? MallApprovedAt { get; set; }

    [MaxLength(500)]
    public string? Address { get; set; }

    [MaxLength(1000)]
    public string? AutoReplyMessage { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
