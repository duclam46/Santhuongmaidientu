using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DOAN_BANHANG.Models;

[Table("CategoryAttributes")]
public class CategoryAttribute
{
    [Key]
    public int Id { get; set; }

    public int CategoryId { get; set; }

    [ForeignKey(nameof(CategoryId))]
    public Category? Category { get; set; }

    [Required]
    [MaxLength(150)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? Unit { get; set; }

    public bool IsFilterable { get; set; } = true;

    public int SortOrder { get; set; } = 0;

    public ICollection<ProductAttributeValue> ProductValues { get; set; } = new List<ProductAttributeValue>();
}
