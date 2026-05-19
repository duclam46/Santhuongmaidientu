namespace DOAN_BANHANG.ViewModels;

public sealed class CheckoutSuccessPageVm
{
    public bool IsSignedIn { get; set; }

    public List<CheckoutSuccessOrderVm> Orders { get; set; } = new();
}

public sealed class CheckoutSuccessOrderVm
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

    public DateTime CreatedAt { get; set; }

    public List<CheckoutSuccessItemVm> Items { get; set; } = new();
}

public sealed class CheckoutSuccessItemVm
{
    public string ProductName { get; set; } = string.Empty;

    public string? ColorName { get; set; }

    public string? SizeName { get; set; }

    public decimal UnitPrice { get; set; }

    public int Quantity { get; set; }

    public decimal LineTotal { get; set; }
}
