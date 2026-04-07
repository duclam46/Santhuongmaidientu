using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DOAN_BANHANG.Models;

[Table("SizeGuides")]
public class SizeGuide
{
    [Key]
    public int Id { get; set; }

    public int ProductId { get; set; }

    [ForeignKey(nameof(ProductId))]
    public Product? Product { get; set; }

    [MaxLength(50)]
    public string? SizeLabel { get; set; }

    [MaxLength(50)]
    public string? HeightRange { get; set; }

    [MaxLength(50)]
    public string? WeightRange { get; set; }

    [MaxLength(255)]
    public string? Note { get; set; }

    public int SortOrder { get; set; } = 0;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<SizeGuideDetail> Details { get; set; } = new List<SizeGuideDetail>();
}
