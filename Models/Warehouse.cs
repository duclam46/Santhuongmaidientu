using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DOAN_BANHANG.Models;

[Table("Warehouses")]
public sealed class Warehouse
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(50)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [MaxLength(255)]
    public string Name { get; set; } = string.Empty;

    // Soc | ProvinceHub | DistrictHub
    [Required]
    [MaxLength(30)]
    public string NodeType { get; set; } = "DistrictHub";

    // North | Central | South | Unknown
    [MaxLength(20)]
    public string? Region { get; set; }

    // GHN master-data ids (best-effort)
    public int? GhnProvinceId { get; set; }

    public int? GhnDistrictId { get; set; }

    public int? ParentId { get; set; }

    [ForeignKey(nameof(ParentId))]
    public Warehouse? Parent { get; set; }

    public ICollection<Warehouse> Children { get; set; } = new List<Warehouse>();

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
