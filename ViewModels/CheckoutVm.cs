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
    
    public decimal VatAmount { get; set; }

    public decimal GlobalVatRate { get; set; } = 0.08m;

    public string PaymentMethod { get; set; } = "COD";

    public string? ErrorMessage { get; set; }

    public string? AppliedVoucherCode { get; set; }

    public string? AppliedShippingVoucherCode { get; set; }

    public string? AppliedDiscountVoucherCode { get; set; }

    public decimal ShippingVoucherDiscountAmount { get; set; }

    public decimal DiscountVoucherDiscountAmount { get; set; }

    public decimal VoucherDiscountAmount { get; set; }

    public List<CheckoutVoucherVm> AvailableVouchers { get; set; } = new();

    public bool IsAllDigital { get; set; }
    public bool RequiresVatInvoice { get; set; }
    public string? CompanyName { get; set; }
    public string? TaxCode { get; set; }
    public string? CompanyAddress { get; set; }
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

    public int ProductId { get; set; }

    public int CategoryId { get; set; }

    public int RootCategoryId { get; set; }

    public int ShopId { get; set; }

    public bool IsMallShop { get; set; }

    public bool IsFlashSaleItem { get; set; }

    public string ProductName { get; set; } = string.Empty;

    public string ProductSlug { get; set; } = string.Empty;

    public string ImageUrl { get; set; } = string.Empty;

    public string? Color { get; set; }

    public string? Size { get; set; }

    public decimal UnitPrice { get; set; }

    public int Quantity { get; set; }

    public decimal LineTotal { get; set; }
    
    public decimal VatRate { get; set; }

    public bool IsDigital { get; set; }
}

public sealed class CheckoutVoucherVm
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string DiscountType { get; set; } = string.Empty;
    public decimal DiscountValue { get; set; }
    public decimal MinOrderValue { get; set; }
    public decimal? MaxDiscountValue { get; set; }
    public DateTime EndDate { get; set; }
    public bool IsShopVoucher { get; set; }
    public string SponsorLabel { get; set; } = string.Empty;
    public string ScopeLabel { get; set; } = string.Empty;
    public bool IsEligible { get; set; }
    public string? IneligibleReason { get; set; }
}

public sealed class PlaceOrderRequest
{
    public int AddressId { get; set; }

    public string PaymentMethod { get; set; } = "COD";

    public int? BuyNowVariantId { get; set; }

    public int? BuyNowQuantity { get; set; }

    public string? VoucherCode { get; set; }

    public string? ShippingVoucherCode { get; set; }

    public string? DiscountVoucherCode { get; set; }

    public bool RequiresVatInvoice { get; set; }

    public string? CompanyName { get; set; }

    public string? TaxCode { get; set; }

    public string? CompanyAddress { get; set; }
}
