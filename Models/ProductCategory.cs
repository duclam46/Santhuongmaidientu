using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DOAN_BANHANG.Models;

[Table("Categories")]
public class Category
{
    [Key]
    public int Id { get; set; }

    public int? ParentId { get; set; }

    [ForeignKey(nameof(ParentId))]
    public Category? Parent { get; set; }

    [Required]
    [MaxLength(150)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(150)]
    public string Slug { get; set; } = string.Empty;

    [Column(TypeName = "decimal(5,2)")]
    public decimal? CommissionRate { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal? MaxCommissionAmount { get; set; }

    [MaxLength(500)]
    public string? IconUrl { get; set; }

    public bool HasSizeGuide { get; set; } = false;

    public bool IsReturnableDefault { get; set; } = true;

    public int SortOrder { get; set; } = 0;

    public bool IsActive { get; set; } = true;

    public bool IsDeleted { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Category> Children { get; set; } = new List<Category>();

    public ICollection<CategoryAttribute> Attributes { get; set; } = new List<CategoryAttribute>();
}
