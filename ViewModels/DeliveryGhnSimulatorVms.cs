using System.ComponentModel.DataAnnotations;

namespace DOAN_BANHANG.ViewModels;

public sealed class DeliveryGhnSimulatorQueryVm
{
    [Display(Name = "Mã vận đơn")]
    public string? TrackingCode { get; set; }

    [Display(Name = "Đồng bộ danh sách kho")]
    public bool SyncStationCatalog { get; set; }
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

    // True when external tracking returned detailed logs (real journey).
    // In that case we typically don't show simulator "arrive/depart" controls.
    public bool IsLiveGhnRoute { get; set; }

    public bool IsReturnRequestSim { get; set; }

    public List<DeliveryGhnRouteStepVm> RouteSteps { get; set; } = new();
}

public sealed class DeliveryGhnRouteStepVm
{
    public int Index { get; set; }

    public string? Location { get; set; }

    public string? StatusName { get; set; }

    public string? Message { get; set; }

    public string? TimeRaw { get; set; }

    // ---- Simulator progress (persisted via OrderTrackingLogs notes) ----
    public bool SimArrived { get; set; }
    public bool SimDeparted { get; set; }
    public bool SimRefused { get; set; }
    public bool SimIsActive { get; set; }
    public bool SimCanArrive { get; set; }
    public bool SimCanDepart { get; set; }
}

public sealed class DeliveryGhnSimulatorCheckInVm
{
    [MaxLength(100)]
    public string? TrackingCode { get; set; }

    [Required]
    public int OrderId { get; set; }

    [Required]
    public string Event { get; set; } = "arrive";

    [Required]
    [Range(1, int.MaxValue)]
    public int StepIndex { get; set; }

    [MaxLength(255)]
    public string? CustomNote { get; set; }

    // Optional: last-mile shipper info (used when delivering to customer).
    [MaxLength(100)]
    public string? ShipperName { get; set; }

    [MaxLength(30)]
    public string? ShipperPhone { get; set; }

    // Route/Location data (best-effort from external detail/log)
    [Required]
    [MaxLength(255)]
    public string? RouteLocation { get; set; }

    [MaxLength(255)]
    public string? RouteStatusName { get; set; }

    [MaxLength(255)]
    public string? RouteTimeRaw { get; set; }
}
