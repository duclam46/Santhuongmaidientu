using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DOAN_BANHANG.Models;

[Table("OrderItems")]
public class OrderItem
{
    [Key]
    public int Id { get; set; }

    public int OrderId { get; set; }

    [ForeignKey(nameof(OrderId))]
    public Order? Order { get; set; }

    public int VariantId { get; set; }

    [ForeignKey(nameof(VariantId))]
    public ProductVariant? Variant { get; set; }

    [Required]
    [MaxLength(255)]
    public string ProductName { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? ColorName { get; set; }

    [MaxLength(50)]
    public string? SizeName { get; set; }

    [MaxLength(255)]
    public string? VariantDescription { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal CommissionAmount { get; set; } = 0m;

    [Column(TypeName = "decimal(18,2)")]
    public decimal UnitCostPrice { get; set; } = 0m;

    [Column(TypeName = "decimal(18,2)")]
    public decimal UnitPrice { get; set; }

    [Range(1, int.MaxValue)]
    public int Quantity { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal LineTotal { get; set; }

    [NotMapped]
    public string EffectiveVariantText
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(VariantDescription))
                return VariantDescription;
            var parts = new[] { ColorName, SizeName }.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
            return parts.Count > 0 ? string.Join(" - ", parts) : string.Empty;
        }
    }
}
