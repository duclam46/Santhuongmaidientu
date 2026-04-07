using System.ComponentModel.DataAnnotations;

namespace DOAN_BANHANG.ViewModels;

public sealed class AdminOrderListQueryVm
{
    public string? Search { get; set; }

    public string? Status { get; set; }

    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 20;
}

public sealed class AdminOrderListItemVm
{
    public int Id { get; set; }

    public string OrderCode { get; set; } = string.Empty;

    public string OrderStatus { get; set; } = string.Empty;

    public string PaymentStatus { get; set; } = string.Empty;

    public decimal FinalAmount { get; set; }

    public int ItemCount { get; set; }

    public DateTime CreatedAt { get; set; }

    public string? CustomerEmail { get; set; }

    public string? RecipientName { get; set; }

    public string? RecipientPhone { get; set; }
}

public sealed class AdminOrderListVm
{
    public AdminOrderListQueryVm Query { get; set; } = new();

    public List<AdminOrderListItemVm> Orders { get; set; } = new();

    public int TotalItems { get; set; }
}

public sealed class AdminOrderItemVm
{
    public int Id { get; set; }

    public string ProductName { get; set; } = string.Empty;

    public string? ColorName { get; set; }

    public string? SizeName { get; set; }

    public decimal UnitPrice { get; set; }

    public int Quantity { get; set; }

    public decimal LineTotal { get; set; }
}

public sealed class AdminOrderDetailsVm
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

    public string? PickupShiftTitle { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public string? CustomerEmail { get; set; }

    public string? CancellationNote { get; set; }

    public List<AdminOrderItemVm> Items { get; set; } = new();
}

public sealed class AdminOrderDecisionVm
{
    [Required]
    public int Id { get; set; }

    [MaxLength(255)]
    public string? Note { get; set; }
}
