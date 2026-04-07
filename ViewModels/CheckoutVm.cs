namespace DOAN_BANHANG.ViewModels;

public sealed class CheckoutPageVm
{
    public bool IsSignedIn { get; set; }

    public bool IsBuyNow { get; set; }

    public int BuyNowVariantId { get; set; }

    public int BuyNowQuantity { get; set; }

    public List<CheckoutAddressVm> Addresses { get; set; } = new();

    public int SelectedAddressId { get; set; }

    public CheckoutAddressVm? Address { get; set; }

    public List<CheckoutItemVm> Items { get; set; } = new();

    public int SelectedItemCount { get; set; }

    public decimal SelectedSubTotal { get; set; }

    public decimal ShippingFee { get; set; }

    public decimal FinalAmount { get; set; }

    public string PaymentMethod { get; set; } = "COD";

    public string? ErrorMessage { get; set; }
}

public sealed class CheckoutAddressVm
{
    public int Id { get; set; }

    public string FullName { get; set; } = string.Empty;

    public string PhoneNumber { get; set; } = string.Empty;

    public string FullAddress { get; set; } = string.Empty;
}

public sealed class CheckoutItemVm
{
    public int CartItemId { get; set; }

    public int VariantId { get; set; }

    public int ShopId { get; set; }

    public string ProductName { get; set; } = string.Empty;

    public string ProductSlug { get; set; } = string.Empty;

    public string ImageUrl { get; set; } = string.Empty;

    public string? Color { get; set; }

    public string? Size { get; set; }

    public decimal UnitPrice { get; set; }

    public int Quantity { get; set; }

    public decimal LineTotal { get; set; }
}

public sealed class PlaceOrderRequest
{
    public int AddressId { get; set; }

    public string PaymentMethod { get; set; } = "COD";

    public int? BuyNowVariantId { get; set; }

    public int? BuyNowQuantity { get; set; }
}
