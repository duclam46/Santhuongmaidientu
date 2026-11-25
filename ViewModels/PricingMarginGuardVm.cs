namespace DOAN_BANHANG.ViewModels;

public class PricingMarginGuardVm
{
    public int ShopId { get; set; }
    public string ShopName { get; set; } = string.Empty;
    public bool IsMall { get; set; }
    public bool IsAdminViewer { get; set; }

    public decimal DefaultCommissionPercent { get; set; } = 8m;
    public decimal MallSurchargePercent { get; set; } = 2m;
    public decimal SellerTaxRatePercent { get; set; } = 1.5m;

    public int ActiveShopVoucherCount { get; set; }
    public List<ShopSelectOptionVm> AvailableShops { get; set; } = new();
    public List<SkuMarginRowVm> Items { get; set; } = new();
}

public class ShopSelectOptionVm
{
    public int ShopId { get; set; }
    public string ShopName { get; set; } = string.Empty;
    public bool IsMall { get; set; }
}

public class SkuMarginRowVm
{
    public int VariantId { get; set; }
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string ProductStatus { get; set; } = "Active";
    public string ImageUrl { get; set; } = "/assets/images/placeholder.png";
    public string Sku { get; set; } = string.Empty;
    public string VariantLabel { get; set; } = "Mặc định";
    public int AvailableStock { get; set; }

    public int CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public decimal BaseCommissionPercent { get; set; }
    public decimal EffectiveCommissionPercent { get; set; }
    public decimal? MaxCommissionCap { get; set; }

    public decimal CostPrice { get; set; }
    public decimal SellingPrice { get; set; }

    public bool HasActiveFlashSale { get; set; }
    public decimal? FlashSalePrice { get; set; }

    // Worst-case Shop Voucher discount on normal price vs Flash Sale price
    public string? NormalVoucherCode { get; set; }
    public decimal NormalVoucherDiscount { get; set; }

    public string? FlashVoucherCode { get; set; }
    public decimal FlashVoucherDiscount { get; set; }
}

public class VariantPriceUpdateItemDto
{
    public int VariantId { get; set; }
    public decimal CostPrice { get; set; }
    public decimal Price { get; set; }
}

public class BulkVariantPriceUpdateRequest
{
    public List<VariantPriceUpdateItemDto> Items { get; set; } = new();
}
