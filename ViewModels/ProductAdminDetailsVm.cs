namespace DOAN_BANHANG.ViewModels;

public class ProductAdminDetailsVm
{
    public int ProductId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Slug { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string? CategoryName { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public decimal AverageRating { get; set; }

    public int ReviewCount { get; set; }

    public int TotalStockQuantity { get; set; }

    public string? Description { get; set; }

    public List<ProductAdminDetailsImageVm> Images { get; set; } = new();

    public List<ProductAdminDetailsVariantVm> Variants { get; set; } = new();

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

    public decimal Price { get; set; }

    public decimal? SalePrice { get; set; }

    public DateTime? SaleStartDate { get; set; }

    public DateTime? SaleEndDate { get; set; }

    public int StockQuantity { get; set; }

    public bool IsActive { get; set; }

    public bool IsDefault { get; set; }
}
