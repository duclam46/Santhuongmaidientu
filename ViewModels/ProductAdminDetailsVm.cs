namespace DOAN_BANHANG.ViewModels;

public class ProductAdminDetailsVm
{
    public int ProductId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Slug { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string? CategoryName { get; set; }

    public string? BrandName { get; set; }

    public int WarrantyMonths { get; set; }

    public string? WarrantyType { get; set; }

    public bool IsReturnable { get; set; } = true;

    public bool IsDigital { get; set; } = false;

    public string? DigitalContent { get; set; }

    public DateTime? ManufacturingDate { get; set; }

    public DateTime? ExpiryDate { get; set; }

    public string? ShippingCondition { get; set; } = "Tiêu chuẩn";

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public decimal AverageRating { get; set; }

    public int ReviewCount { get; set; }

    public int TotalStockQuantity { get; set; }

    public int TotalReservedStock { get; set; }

    public int TotalSoldQuantity { get; set; }

    public string? Option1Name { get; set; }

    public string? Option2Name { get; set; }

    public string? Description { get; set; }

    public List<ProductAdminDetailsImageVm> Images { get; set; } = new();

    public List<ProductAdminDetailsVariantVm> Variants { get; set; } = new();

    public List<ProductAdminAttributeInputVm> Attributes { get; set; } = new();

    public int? SelectedVariantId { get; set; }

    public List<ProductAdminSizeGuideRowVm> SizeGuides { get; set; } = new();

    public List<ProductAdminReviewItemVm> RecentReviews { get; set; } = new();
}

public class ProductAdminDetailsImageVm
{
    public string Url { get; set; } = string.Empty;

    public string Alt { get; set; } = string.Empty;

    public bool IsPrimary { get; set; }

    public int SortOrder { get; set; }
}

public class ProductAdminDetailsVariantVm
{
    public int VariantId { get; set; }

    public string Sku { get; set; } = string.Empty;

    public int? ColorId { get; set; }

    public string? ColorName { get; set; }

    public string? ColorHex { get; set; }

    public int? SizeId { get; set; }

    public string? SizeName { get; set; }

    public string? Option1Value { get; set; }

    public string? Option2Value { get; set; }

    public string? VariantLabel { get; set; }

    public string? ImageUrl { get; set; }

    public int WeightGram { get; set; } = 300;

    public int LengthCm { get; set; } = 10;

    public int WidthCm { get; set; } = 10;

    public int HeightCm { get; set; } = 10;

    public decimal Price { get; set; }

    public decimal CostPrice { get; set; } = 0m;

    public decimal? SalePrice { get; set; }

    public DateTime? SaleStartDate { get; set; }

    public DateTime? SaleEndDate { get; set; }

    public int StockQuantity { get; set; }

    public int ReservedStock { get; set; }

    public int AvailableStock => Math.Max(0, StockQuantity - ReservedStock);

    public int SoldQuantity { get; set; }

    public bool IsActive { get; set; }

    public bool IsDefault { get; set; }
}
