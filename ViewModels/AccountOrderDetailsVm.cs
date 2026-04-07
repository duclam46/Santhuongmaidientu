namespace DOAN_BANHANG.ViewModels;

public sealed class MyOrderDetailsPageVm
{
    public bool IsSignedIn { get; set; }

    public CustomerOrderDetailsVm? Order { get; set; }
}

public sealed class CustomerOrderDetailsVm
{
    public int Id { get; set; }

    public string OrderCode { get; set; } = string.Empty;

    public string OrderStatus { get; set; } = string.Empty;

    public string PaymentMethod { get; set; } = string.Empty;

    public string PaymentStatus { get; set; } = string.Empty;

    public decimal SubTotal { get; set; }

    public decimal ShippingFee { get; set; }

    public decimal DiscountAmount { get; set; }

    public decimal FinalAmount { get; set; }

    public string RecipientName { get; set; } = string.Empty;

    public string RecipientPhone { get; set; } = string.Empty;

    public string ShippingAddress { get; set; } = string.Empty;

    public string? TrackingCode { get; set; }

    public DateTime? EstimatedDeliveryAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public List<CustomerOrderItemVm> Items { get; set; } = new();

    public List<CustomerOrderTrackingLogVm> TrackingLogs { get; set; } = new();
}

public sealed class CustomerOrderItemVm
{
    public string ProductName { get; set; } = string.Empty;

    public string? ColorName { get; set; }

    public string? SizeName { get; set; }

    public decimal UnitPrice { get; set; }

    public int Quantity { get; set; }

    public decimal LineTotal { get; set; }
}

public sealed class CustomerOrderTrackingLogVm
{
    public string Status { get; set; } = string.Empty;

    public string? Note { get; set; }

    public DateTime CreatedAt { get; set; }
}
