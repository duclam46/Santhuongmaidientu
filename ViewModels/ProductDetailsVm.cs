namespace DOAN_BANHANG.ViewModels;

public class ProductDetailsVm
{
    public int ProductId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Slug { get; set; } = string.Empty;

    public decimal Price { get; set; }

    public decimal AverageRating { get; set; }

    public int ReviewCount { get; set; }

    public int StockQuantity { get; set; }

    public DateTime CreatedAt { get; set; }

    public string? Description { get; set; }

    public string? PrimaryCategoryName { get; set; }

    public string? PrimaryCategorySlug { get; set; }

    public List<ProductDetailsImageVm> Images { get; set; } = new();

    public List<ProductVariantVm> Variants { get; set; } = new();

    public int? SelectedVariantId { get; set; }

    public List<ProductSizeGuideVm> SizeGuides { get; set; } = new();

    public List<ProductReviewVm> Reviews { get; set; } = new();

    public List<ProductCardVm> RelatedProducts { get; set; } = new();

    public ShopSummaryVm? Shop { get; set; }
}

public class ShopSummaryVm
{
    public int ShopId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Slug { get; set; }

    public string? LogoUrl { get; set; }

    public string? Location { get; set; }

    public string? LastActiveText { get; set; }

    public decimal RatingAverage { get; set; }

    public int ProductCount { get; set; }

    public int? ChatResponseRate { get; set; }
}

public class ProductVariantVm
{
    public int VariantId { get; set; }

    public int? ColorId { get; set; }

    public int? SizeId { get; set; }

    public string? SizeLabel { get; set; }

    public string? Color { get; set; }

    public string? ColorHex { get; set; }

    // Base/original variant price (non-sale)
    public decimal BasePrice { get; set; }

    public decimal? SalePrice { get; set; }

    public DateTime? SaleStartDate { get; set; }

    public DateTime? SaleEndDate { get; set; }

    public bool IsOnSale { get; set; }

    // Remaining seconds until sale ends (server-computed, null if no end time / not on sale)
    public int? SaleSecondsLeft { get; set; }

    public decimal Price { get; set; }

    public int StockQuantity { get; set; }
}

public class ProductDetailsImageVm
{
    public string Url { get; set; } = string.Empty;

    public string Alt { get; set; } = string.Empty;

    public bool IsPrimary { get; set; }

    public int SortOrder { get; set; }
}

public class ProductSizeGuideVm
{
    public string? SizeLabel { get; set; }

    public string? MeasurementName { get; set; }

    public string? MeasurementValue { get; set; }

    public string? Note { get; set; }
}

public class ProductReviewVm
{
    public int Rating { get; set; }

    public string? Comment { get; set; }

    public DateTime CreatedAt { get; set; }

    public string AuthorName { get; set; } = string.Empty;

    public string? AvatarUrl { get; set; }
}
