namespace DOAN_BANHANG.ViewModels;

public class ProductDetailsVm
{
    public int ProductId { get; set; }

    public int ShopId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Slug { get; set; } = string.Empty;

    public decimal Price { get; set; }

    public decimal MinVariantPrice { get; set; }

    public decimal MaxVariantPrice { get; set; }

    public bool HasPriceRange => MaxVariantPrice > MinVariantPrice && MinVariantPrice > 0;

    public decimal AverageRating { get; set; }

    public int ReviewCount { get; set; }

    public int StockQuantity { get; set; }

    public DateTime CreatedAt { get; set; }

    public string? Description { get; set; }

    public string? PrimaryCategoryName { get; set; }

    public string? PrimaryCategorySlug { get; set; }

    public string? BrandName { get; set; }

    public string? BrandSlug { get; set; }

    public string? BrandLogoUrl { get; set; }

    public int WarrantyMonths { get; set; }

    public string? WarrantyType { get; set; }

    public bool IsReturnable { get; set; } = true;

    public bool IsDigital { get; set; }

    public DateTime? ManufacturingDate { get; set; }

    public DateTime? ExpiryDate { get; set; }

    public string? ShippingCondition { get; set; }

    public bool HasSizeGuide { get; set; } = true;

    public List<ProductOptionGroupVm> OptionGroups { get; set; } = new();

    public List<ProductSpecItemVm> Attributes { get; set; } = new();

    public List<ProductDetailsImageVm> Images { get; set; } = new();

    public List<ProductVariantVm> Variants { get; set; } = new();

    public int? SelectedVariantId { get; set; }

    public List<ProductSizeGuideVm> SizeGuides { get; set; } = new();

    public List<ProductReviewVm> Reviews { get; set; } = new();

    public List<ProductCardVm> RelatedProducts { get; set; } = new();

    public List<ProductCardVm> RecentlyViewedProducts { get; set; } = new();

    public List<ProductCardVm> BundleProducts { get; set; } = new();

    public List<StorefrontVoucherVm> AvailableVouchers { get; set; } = new();

    public HashSet<int> SavedVoucherIds { get; set; } = new();

    public ShopSummaryVm? Shop { get; set; }
}

public class StorefrontVoucherVm
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string DiscountType { get; set; } = string.Empty;
    public decimal DiscountValue { get; set; }
    public decimal MinOrderValue { get; set; }
    public decimal? MaxDiscountValue { get; set; }
    public DateTime EndDate { get; set; }
    public bool IsShopVoucher { get; set; }
    public string SponsorBadge { get; set; } = string.Empty;
    public string ScopeBadge { get; set; } = string.Empty;
    public bool IsSaved { get; set; }
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

    public bool IsMall { get; set; }

    public string ShopTier { get; set; } = "Standard";

    public int FollowerCount { get; set; }

    public bool IsFollowing { get; set; }
}

public class ProductVariantVm
{
    public int VariantId { get; set; }

    public string? Sku { get; set; }

    public int? ColorId { get; set; }

    public int? SizeId { get; set; }

    public string? SizeLabel { get; set; }

    public string? Color { get; set; }

    public string? ColorHex { get; set; }

    public int? OptionValue1Id { get; set; }

    public int? OptionValue2Id { get; set; }

    public string? Option1Value { get; set; }

    public string? Option2Value { get; set; }

    public string? ImageUrl { get; set; }

    public int WeightGram { get; set; } = 300;

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

    public string? HeightRange { get; set; }

    public string? WeightRange { get; set; }

    public decimal? ShoulderWidthCm { get; set; }

    public decimal? BustCm { get; set; }

    public decimal? WaistCm { get; set; }

    public decimal? HipCm { get; set; }

    public decimal? LengthCm { get; set; }
    public decimal? WidthCm { get; set; }
    public decimal? HeightCm { get; set; }
    public decimal? ShoeLengthCm { get; set; }

    public string? Note { get; set; }
}

public class ProductReviewVm
{
    public int Rating { get; set; }

    public string? Comment { get; set; }

    public DateTime CreatedAt { get; set; }

    public string AuthorName { get; set; } = string.Empty;

    public string? AvatarUrl { get; set; }

    public string? SellerReply { get; set; }

    public DateTime? RepliedAt { get; set; }

    public string? ShopName { get; set; }

    public string? VariantName { get; set; }

    public List<string> MediaUrls { get; set; } = new();
}

public class ProductOptionGroupVm
{
    public int Tier { get; set; } // 1 or 2
    public string Name { get; set; } = string.Empty;
    public List<ProductOptionValueVm> Values { get; set; } = new();
}

public class ProductOptionValueVm
{
    public int Id { get; set; }
    public string Value { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public string? ColorHex { get; set; }
}

public class ProductSpecItemVm
{
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string? Unit { get; set; }
}
