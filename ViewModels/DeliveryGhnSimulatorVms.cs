using System.ComponentModel.DataAnnotations;

namespace DOAN_BANHANG.ViewModels;

public sealed class DeliveryGhnSimulatorQueryVm
{
    [Display(Name = "Mã vận đơn (GHN)")]
    public string? TrackingCode { get; set; }
}

public sealed class DeliveryGhnStationVm
{
    public int LocationId { get; set; }

    public int LocationCode { get; set; }

    public string? LocationName { get; set; }

    public string? Address { get; set; }

    public string? WardName { get; set; }

    public string? DistrictName { get; set; }

    public string? ProvinceName { get; set; }
}

public sealed class DeliveryGhnSimulatorVm
{
    public DeliveryGhnSimulatorQueryVm Query { get; set; } = new();

    public int? ResolvedOrderId { get; set; }

    public string? ResolvedOrderCode { get; set; }

    public string? ResolvedTrackingCode { get; set; }

    public string? GhnStatusName { get; set; }

    public List<DeliveryGhnRouteStepVm> RouteSteps { get; set; } = new();
}

public sealed class DeliveryGhnRouteStepVm
{
    public int Index { get; set; }

    public string? Location { get; set; }

    public string? StatusName { get; set; }

    public string? Message { get; set; }

    public string? TimeRaw { get; set; }
}

public sealed class DeliveryGhnSimulatorCheckInVm
{
    [MaxLength(100)]
    public string? TrackingCode { get; set; }

    [Required]
    public int OrderId { get; set; }

    [Required]
    public string Event { get; set; } = "arrive";

    [MaxLength(255)]
    public string? CustomNote { get; set; }

    // Route/Location data (best-effort from GHN detail/log)
    [Required]
    [MaxLength(255)]
    public string? RouteLocation { get; set; }

    [MaxLength(255)]
    public string? RouteStatusName { get; set; }

    [MaxLength(255)]
    public string? RouteTimeRaw { get; set; }
}
