using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DOAN_BANHANG.Models;

[Table("PartnerApplications")]
public class PartnerApplication
{
    [Key]
    public int Id { get; set; }

    public int AccountId { get; set; }

    [MaxLength(150)]
    public string? FullName { get; set; }

    [MaxLength(255)]
    public string Email { get; set; } = string.Empty;

    [MaxLength(20)]
    public string? PhoneNumber { get; set; }

    [MaxLength(200)]
    public string? ShopName { get; set; }

    public string? ShopDescription { get; set; }

    [MaxLength(500)]
    public string? Address { get; set; }

    public int ProvinceId { get; set; }

    [MaxLength(100)]
    public string? ProvinceName { get; set; }

    public int DistrictId { get; set; }

    [MaxLength(100)]
    public string? DistrictName { get; set; }

    [MaxLength(50)]
    public string? WardCode { get; set; }

    [MaxLength(100)]
    public string? WardName { get; set; }

    [MaxLength(500)]
    public string? StreetAddress { get; set; }

    [MaxLength(30)]
    public string Status { get; set; } = "Pending"; // Pending | Approved | Rejected

    [MaxLength(500)]
    public string? RejectionReason { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? DecidedAt { get; set; }

    public int? DecidedByAccountId { get; set; }
}
