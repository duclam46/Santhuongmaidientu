using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DOAN_BANHANG.Models;

[Table("ProductAttributeValues")]
public class ProductAttributeValue
{
    [Key]
    public int Id { get; set; }

    public int ProductId { get; set; }

    [ForeignKey(nameof(ProductId))]
    public Product? Product { get; set; }

    public int CategoryAttributeId { get; set; }

    [ForeignKey(nameof(CategoryAttributeId))]
    public CategoryAttribute? CategoryAttribute { get; set; }

    [Required]
    [MaxLength(255)]
    public string Value { get; set; } = string.Empty;
}
