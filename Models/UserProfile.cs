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

    [MaxLength(500)]
    public string? Address { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
