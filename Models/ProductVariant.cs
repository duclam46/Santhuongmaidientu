using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DOAN_BANHANG.Models;

[Table("ProductVariants")]
public class ProductVariant
{
    [Key]
    public int Id { get; set; }

    public int ProductId { get; set; }

    [ForeignKey(nameof(ProductId))]
    public Product? Product { get; set; }

    public int? OptionValue1Id { get; set; }

    [ForeignKey(nameof(OptionValue1Id))]
    public ProductOptionValue? OptionValue1 { get; set; }

    public int? OptionValue2Id { get; set; }

    [ForeignKey(nameof(OptionValue2Id))]
    public ProductOptionValue? OptionValue2 { get; set; }

    public int WeightGram { get; set; } = 300;

    public int LengthCm { get; set; } = 10;

    public int WidthCm { get; set; } = 10;

    public int HeightCm { get; set; } = 10;

    [Required]
    [MaxLength(100)]
    public string Sku { get; set; } = string.Empty;

    [Column(TypeName = "decimal(18,2)")]
    public decimal Price { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal CostPrice { get; set; } = 0m;

    [Column(TypeName = "decimal(18,2)")]
    public decimal? SalePrice { get; set; }

    public DateTime? SaleStartDate { get; set; }

    public DateTime? SaleEndDate { get; set; }

    [Range(0, int.MaxValue)]
    public int StockQuantity { get; set; } = 0;

    [Range(0, int.MaxValue)]
    public int ReservedStock { get; set; } = 0;

    public bool IsDefault { get; set; } = false;

    public bool IsActive { get; set; } = true;

    public bool IsDeleted { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<VariantImage> Images { get; set; } = new List<VariantImage>();

    [NotMapped]
    public string DisplayVariantName
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(OptionValue1?.Value))
            {
                var opt1Name = OptionValue1.ProductOption?.Name;
                parts.Add(!string.IsNullOrWhiteSpace(opt1Name) ? $"{opt1Name}: {OptionValue1.Value}" : OptionValue1.Value);
            }

            if (!string.IsNullOrWhiteSpace(OptionValue2?.Value))
            {
                var opt2Name = OptionValue2.ProductOption?.Name;
                parts.Add(!string.IsNullOrWhiteSpace(opt2Name) ? $"{opt2Name}: {OptionValue2.Value}" : OptionValue2.Value);
            }

            return string.Join(", ", parts);
        }
    }

    [NotMapped]
    public string ShortVariantLabel
    {
        get
        {
            var v1 = OptionValue1?.Value;
            var v2 = OptionValue2?.Value;
            if (!string.IsNullOrWhiteSpace(v1) && !string.IsNullOrWhiteSpace(v2))
                return $"{v1} - {v2}";
            return v1 ?? v2 ?? "Mặc định";
        }
    }
}
