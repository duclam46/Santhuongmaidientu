using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DOAN_BANHANG.Models;

[Table("WarehouseRoutes")]
public sealed class WarehouseRoute
{
    [Key]
    public int Id { get; set; }

    public int FromWarehouseId { get; set; }

    [ForeignKey(nameof(FromWarehouseId))]
    public Warehouse? FromWarehouse { get; set; }

    public int ToWarehouseId { get; set; }

    [ForeignKey(nameof(ToWarehouseId))]
    public Warehouse? ToWarehouse { get; set; }

    [Required]
    [MaxLength(30)]
    public string RouteKind { get; set; } = "Linehaul";

    // Default duration estimate for this leg.
    public int DefaultHours { get; set; } = 12;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
