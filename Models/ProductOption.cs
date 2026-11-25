using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DOAN_BANHANG.Models;

[Table("ProductOptions")]
public class ProductOption
{
    [Key]
    public int Id { get; set; }

    public int ProductId { get; set; }

    [ForeignKey(nameof(ProductId))]
    public Product? Product { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    public int SortOrder { get; set; } = 0;

    public ICollection<ProductOptionValue> Values { get; set; } = new List<ProductOptionValue>();
}
