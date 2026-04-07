using System.ComponentModel.DataAnnotations;

namespace DOAN_BANHANG.ViewModels;

public class ProductAdminSelectOptionVm
{
    public int Value { get; set; }

    public string? ValueString { get; set; }

    public string Label { get; set; } = string.Empty;

    // Optional: used for visual pickers (e.g., Color hex code)
    public string? Hex { get; set; }
}

public class ProductAdminListQueryVm
{
    public string? Search { get; set; }

    public int? CategoryId { get; set; }

    public string? Status { get; set; }

    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 10;
}

public class ProductAdminListVm
{
    public ProductAdminListQueryVm Query { get; set; } = new();

    public List<ProductAdminSelectOptionVm> CategoryOptions { get; set; } = new();

    public List<ProductAdminListItemVm> Products { get; set; } = new();

    public int TotalItems { get; set; }

    public int TotalPages => Query.PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalItems / (double)Query.PageSize);
}

public class ProductAdminListItemVm
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? CategoryName { get; set; }

    public string? Status { get; set; }

    public DateTime CreatedAt { get; set; }

    public int VariantCount { get; set; }

    public string? PrimaryImageUrl { get; set; }
}

public class ProductAdminUpsertVm
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập tên sản phẩm.")]
    [StringLength(200)]
    public string Name { get; set; } = string.Empty;

    [StringLength(200)]
    public string? Slug { get; set; }

    [Required(ErrorMessage = "Vui lòng chọn danh mục.")]
    public int CategoryId { get; set; }

    public int? ParentCategoryId { get; set; }

    public string? Description { get; set; }

    [Required]
    public string Status { get; set; } = "Active";

    public List<ProductAdminSelectOptionVm> CategoryOptions { get; set; } = new();

    // Create-only: parent/child category selection
    public List<ProductAdminSelectOptionVm> ParentCategoryOptions { get; set; } = new();

    public List<ProductAdminSelectOptionVm> ChildCategoryOptions { get; set; } = new();

    public List<ProductAdminSelectOptionVm> StatusOptions { get; set; } = new();

    public List<ProductAdminSelectOptionVm> ColorOptions { get; set; } = new();

    // Create-only (and usable for bulk generation): selected colors to generate variants
    public List<int> SelectedColorIds { get; set; } = new();

    public List<ProductAdminSelectOptionVm> SizeOptions { get; set; } = new();

    public List<ProductAdminSizeGuideRowVm> SizeGuide { get; set; } = new();

    // Create-only: variant mode
    // - true: variants by size (clothing)
    // - false: single variant (watch/cosmetic)
    public bool HasSizeVariants { get; set; } = true;

    // When HasSizeVariants=true, this controls which options are shown (shoe size, clothing, ml...)
    public string SizeScheme { get; set; } = "clothing";

    // Product measurements/specs (optional). Unit: cm.
    [Range(0, double.MaxValue)]
    public decimal? ClothingLengthCm { get; set; }

    [Range(0, double.MaxValue)]
    public decimal? ClothingWidthCm { get; set; }

    [Range(0, double.MaxValue)]
    public decimal? ClothingHeightCm { get; set; }

    [Range(0, double.MaxValue)]
    public decimal? ShoeLengthCm { get; set; }

    [Range(0, int.MaxValue)]
    public int SingleStockQuantity { get; set; }

    // Create-only: initial inventory by size
    public List<ProductAdminInitialSizeStockVm> InitialSizeStocks { get; set; } = new();

    // Create-only: custom option rows (e.g., 50ml, 100ml) for volume scheme
    public List<ProductAdminCustomOptionStockVm> VolumeOptionStocks { get; set; } = new();

    public List<ProductAdminVariantRowVm> Variants { get; set; } = new();

    public List<ProductAdminImageRowVm> Images { get; set; } = new();

    // Create-only uploads
    public Microsoft.AspNetCore.Http.IFormFile? PrimaryImage { get; set; }

    public List<Microsoft.AspNetCore.Http.IFormFile> ContentImages { get; set; } = new();

    public ProductAdminVariantEditVm NewVariant { get; set; } = new();
}

public class ProductAdminGenerateVariantsVm
{
    public int ProductId { get; set; }

    public List<int> ColorIds { get; set; } = new();

    public List<int> SizeIds { get; set; } = new();
}

public class ProductAdminCustomOptionStockVm
{
    [StringLength(50)]
    public string? OptionName { get; set; }

    [Range(0, int.MaxValue)]
    public int StockQuantity { get; set; }
}

public class ProductAdminInitialSizeStockVm
{
    public int SizeId { get; set; }

    public string? SizeName { get; set; }

    [Range(0, int.MaxValue)]
    public int StockQuantity { get; set; }
}

public class ProductAdminSizeGuideRowVm
{
    public string? SizeLabel { get; set; }

    public string? HeightRange { get; set; }

    public string? WeightRange { get; set; }

    // Per-size product measurements (optional). Unit: cm.
    [Range(0, double.MaxValue)]
    public decimal? ClothingLengthCm { get; set; }

    [Range(0, double.MaxValue)]
    public decimal? ClothingWidthCm { get; set; }

    [Range(0, double.MaxValue)]
    public decimal? ClothingHeightCm { get; set; }

    [Range(0, double.MaxValue)]
    public decimal? ShoeLengthCm { get; set; }

    public string? Note { get; set; }
}

public class ProductAdminVariantRowVm
{
    public int VariantId { get; set; }
    public string Sku { get; set; } = string.Empty;

    public int? ColorId { get; set; }

    public string? ColorName { get; set; }

    public int? SizeId { get; set; }

    public string? SizeName { get; set; }

    [Range(0, double.MaxValue)]
    public decimal Price { get; set; }

    [Range(0, double.MaxValue)]
    public decimal? SalePrice { get; set; }

    public DateTime? SaleStartDate { get; set; }

    public DateTime? SaleEndDate { get; set; }

    public int StockQuantity { get; set; }

    public bool IsActive { get; set; }
}

public class ProductAdminVariantEditVm
{
    public int ProductId { get; set; }

    public int? VariantId { get; set; }

    public int? ColorId { get; set; }

    public int? SizeId { get; set; }

    // SKU is auto-generated server-side (ProductId-Color-Size) to match the Create flow.
    [StringLength(100)]
    public string? Sku { get; set; }

    [Range(0, double.MaxValue)]
    public decimal Price { get; set; }

    [Range(0, int.MaxValue)]
    public int StockQuantity { get; set; }

    public bool IsActive { get; set; } = true;
}

public class ProductAdminVariantCommandVm
{
    public int ProductId { get; set; }

    public int VariantId { get; set; }
}

public class ProductAdminUploadProductImagesVm
{
    public int ProductId { get; set; }

    public List<Microsoft.AspNetCore.Http.IFormFile> Files { get; set; } = new();
}

public class ProductAdminReorderImagesVm
{
    public int ProductId { get; set; }

    public List<int> ImageIds { get; set; } = new();
}

public class ProductAdminSetPrimaryImageVm
{
    public int ProductId { get; set; }

    public int ImageId { get; set; }
}

public class ProductAdminDeleteImageVm
{
    public int ProductId { get; set; }

    public int ImageId { get; set; }
}

public class ProductAdminUploadVariantImagesVm
{
    public int ProductId { get; set; }

    public int VariantId { get; set; }

    public List<Microsoft.AspNetCore.Http.IFormFile> Files { get; set; } = new();
}

public class ProductAdminVariantImageRowVm
{
    public int ImageId { get; set; }

    public string ImageUrl { get; set; } = string.Empty;

    public bool IsPrimary { get; set; }

    public int SortOrder { get; set; }
}

public class ProductAdminVariantImagesVm
{
    public int ProductId { get; set; }

    public int VariantId { get; set; }

    public string ProductName { get; set; } = string.Empty;

    public string VariantLabel { get; set; } = string.Empty;

    public List<ProductAdminVariantImageRowVm> Images { get; set; } = new();
}

public class ProductAdminImageRowVm
{
    public int ImageId { get; set; }

    public string ImageUrl { get; set; } = string.Empty;

    public bool IsPrimary { get; set; }

    public int SortOrder { get; set; }
}

public class ProductAdminFlashSaleVm
{
    public int ProductId { get; set; }

    public string ProductName { get; set; } = string.Empty;

    public List<ProductAdminFlashSaleVariantVm> Variants { get; set; } = new();
}

public class ProductAdminFlashSaleVariantVm
{
    public int VariantId { get; set; }

    public string VariantLabel { get; set; } = string.Empty;

    public decimal Price { get; set; }

    public decimal? SalePrice { get; set; }

    public DateTime? SaleStartDate { get; set; }

    public DateTime? SaleEndDate { get; set; }

    public bool IsOnSale { get; set; }

    public bool IsActive { get; set; }
}

public class ProductAdminReviewsVm
{
    public int ProductId { get; set; }

    public string ProductName { get; set; } = string.Empty;

    public List<ProductAdminReviewItemVm> Reviews { get; set; } = new();
}

public class ProductAdminReviewItemVm
{
    public int ReviewId { get; set; }

    public int Rating { get; set; }

    public string? Comment { get; set; }

    public DateTime CreatedAt { get; set; }

    public string AuthorName { get; set; } = string.Empty;

    public string? SellerReply { get; set; }
}

public class ProductAdminReplyReviewVm
{
    public int ProductId { get; set; }

    public int ReviewId { get; set; }

    [StringLength(1000)]
    public string? SellerReply { get; set; }
}
