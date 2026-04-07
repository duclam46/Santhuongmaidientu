using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DOAN_BANHANG.Models;

[Table("SizeGuideDetails")]
public class SizeGuideDetail
{
    [Key]
    public int Id { get; set; }

    public int SizeGuideId { get; set; }

    [ForeignKey(nameof(SizeGuideId))]
    public SizeGuide? SizeGuide { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(50)]
    public string Value { get; set; } = string.Empty;

    [MaxLength(20)]
    public string? Unit { get; set; }

    public int SortOrder { get; set; } = 0;
}
