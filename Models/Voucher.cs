using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DOAN_BANHANG.Models;

[Table("Vouchers")]
public class Voucher
{
    [Key]
    public int Id { get; set; }

    public int? ShopId { get; set; }

    [ForeignKey(nameof(ShopId))]
    public Account? Shop { get; set; }

    [Required]
    [MaxLength(50)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [MaxLength(20)]
    public string DiscountType { get; set; } = string.Empty; // Percent | FixedAmount

    [Column(TypeName = "decimal(18,2)")]
    public decimal DiscountValue { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal MinOrderValue { get; set; } = 0m;

    [Column(TypeName = "decimal(18,2)")]
    public decimal? MaxDiscountValue { get; set; }

    public int UsageLimit { get; set; } = 0;

    public int UsedCount { get; set; } = 0;

    public DateTime StartDate { get; set; }

    public DateTime EndDate { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
