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

    public int? ColorId { get; set; }

    [ForeignKey(nameof(ColorId))]
    public Color? Color { get; set; }

    public int? SizeId { get; set; }

    [ForeignKey(nameof(SizeId))]
    public Size? Size { get; set; }

    [Required]
    [MaxLength(100)]
    public string Sku { get; set; } = string.Empty;

    [Column(TypeName = "decimal(18,2)")]
    public decimal Price { get; set; }

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
}
