using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DOAN_BANHANG.Models;

[Table("ProductOptionValues")]
public class ProductOptionValue
{
    [Key]
    public int Id { get; set; }

    public int ProductOptionId { get; set; }

    [ForeignKey(nameof(ProductOptionId))]
    public ProductOption? ProductOption { get; set; }

    [Required]
    [MaxLength(100)]
    public string Value { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? ImageUrl { get; set; }
}
