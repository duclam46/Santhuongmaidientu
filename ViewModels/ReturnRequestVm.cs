namespace DOAN_BANHANG.ViewModels;

public sealed class ReturnRequestVm
{
    public int OrderId { get; set; }

    public string OrderCode { get; set; } = string.Empty;

    public string ShippingAddress { get; set; } = string.Empty;

    public string RecipientName { get; set; } = string.Empty;

    public string RecipientPhone { get; set; } = string.Empty;

    public DateTime DeliveredAt { get; set; }

    public int DaysRemaining { get; set; }

    public bool CanReturn { get; set; }

    /// <summary>
    /// Existing return request, if any.
    /// </summary>
    public ReturnRequestStatusVm? ExistingRequest { get; set; }
}

public sealed class ReturnRequestStatusVm
{
    public int Id { get; set; }

    public string Status { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;

    public List<string> MediaUrls { get; set; } = new();

    public string? RejectionNote { get; set; }

    public string? ReturnTrackingCode { get; set; }

    public string? EstimatedPickupTime { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}

public sealed class SubmitReturnRequestVm
{
    public int OrderId { get; set; }

    public string Reason { get; set; } = string.Empty;

    /// <summary>
    /// JSON array string of uploaded media URLs.
    /// </summary>
    public string? MediaUrlsJson { get; set; }
}
