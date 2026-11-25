using System.ComponentModel.DataAnnotations;

namespace DOAN_BANHANG.ViewModels;

public class ProductAdminSelectOptionVm
{
    public int Value { get; set; }

    public string? ValueString { get; set; }

    public string Label { get; set; } = string.Empty;

    public string? Hex { get; set; }
}

public class ProductAdminAttributeInputVm
{
    public int CategoryAttributeId { get; set; }

    public string? AttributeName { get; set; }

    public string? Unit { get; set; }

    public string? Value { get; set; }
}

public class ProductAdminListQueryVm
{
    public string? Search { get; set; }

    public int? CategoryId { get; set; }

    public string? Status { get; set; }

    public string? StockFilter { get; set; }

    public string? SortBy { get; set; }

    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 10;
}

public class ProductAdminListVm
{
    public ProductAdminListQueryVm Query { get; set; } = new();

    public List<ProductAdminSelectOptionVm> CategoryOptions { get; set; } = new();

    public List<ProductAdminListItemVm> Products { get; set; } = new();

    public int TotalItems { get; set; }

    public int AllProductsCount { get; set; }

    public int ActiveProductsCount { get; set; }

    public int HiddenProductsCount { get; set; }

    public int DraftProductsCount { get; set; }

    public int LowStockProductsCount { get; set; }

    public int OutOfStockProductsCount { get; set; }

    public int TotalAvailableStockAll { get; set; }

    public int TotalVariantsAll { get; set; }

    public int TotalPages => Query.PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalItems / (double)Query.PageSize);
}

public class ProductAdminListVariantVm
{
    public int Id { get; set; }

    public string Sku { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    public decimal Price { get; set; }

    public decimal CostPrice { get; set; }

    public int StockQuantity { get; set; }

    public int ReservedStock { get; set; }

    public int AvailableStock => Math.Max(0, StockQuantity - ReservedStock);

    public int SoldQuantity { get; set; }

    public bool IsActive { get; set; }

    public string? ImageUrl { get; set; }
}

public class ProductAdminListItemVm
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Slug { get; set; }

    public string? CategoryName { get; set; }

    public string? BrandName { get; set; }

    public string? Status { get; set; }

    public DateTime CreatedAt { get; set; }

    public int VariantCount { get; set; }

    public string? PrimaryImageUrl { get; set; }

    public decimal MinPrice { get; set; }

    public decimal MaxPrice { get; set; }

    public int TotalStock { get; set; }

    public int ReservedStock { get; set; }

    public int AvailableStock => Math.Max(0, TotalStock - ReservedStock);

    public int LowStockVariantCount { get; set; }

    public int OutOfStockVariantCount { get; set; }

    public int SoldQuantity { get; set; }

    public List<ProductAdminListVariantVm> Variants { get; set; } = new();
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

    public int? BrandId { get; set; }

    public int WarrantyMonths { get; set; } = 0;

    [StringLength(100)]
    public string? WarrantyType { get; set; }

    public bool IsReturnable { get; set; } = true;

    public bool IsDigital { get; set; } = false;

    [StringLength(1000)]
    public string? DigitalContent { get; set; }

    public DateTime? ManufacturingDate { get; set; }

    public DateTime? ExpiryDate { get; set; }

    [StringLength(50)]
    public string? ShippingCondition { get; set; } = "Tiêu chuẩn";

    public decimal SingleCostPrice { get; set; } = 0m;

    public string? Description { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập giá sản phẩm.")]
    [Range(0, double.MaxValue, ErrorMessage = "Giá sản phẩm không hợp lệ.")]
    public decimal Price { get; set; }

    [Range(0, double.MaxValue, ErrorMessage = "Giá khuyến mãi không hợp lệ.")]
    public decimal? SalePrice { get; set; }

    [Required]
    public string Status { get; set; } = "Active";

    public List<ProductAdminSelectOptionVm> CategoryOptions { get; set; } = new();

    public List<ProductAdminSelectOptionVm> ParentCategoryOptions { get; set; } = new();

    public List<ProductAdminSelectOptionVm> ChildCategoryOptions { get; set; } = new();

    public List<ProductAdminSelectOptionVm> BrandOptions { get; set; } = new();

    public List<ProductAdminSelectOptionVm> StatusOptions { get; set; } = new();

    public List<ProductAdminSelectOptionVm> ColorOptions { get; set; } = new();

    public List<int> SelectedColorIds { get; set; } = new();

    public List<ProductAdminSelectOptionVm> SizeOptions { get; set; } = new();

    public List<ProductAdminSizeGuideRowVm> SizeGuide { get; set; } = new();

    // Dynamic EAV technical specs
    public List<ProductAdminAttributeInputVm> AttributeValues { get; set; } = new();

    // Dynamic 2-tier option group names (e.g. "Màu sắc", "Dung lượng")
    public bool UseDynamicOptions { get; set; } = true;

    public bool HasSizeGuide { get; set; } = false;

    public string? Option1Name { get; set; }

    public string? Option2Name { get; set; }

    public List<string> Option1Values { get; set; } = new();

    public List<string> Option2Values { get; set; } = new();

    public bool HasSizeVariants { get; set; } = false;

    public string SizeScheme { get; set; } = "clothing";

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

    public int SingleWeightGram { get; set; } = 300;

    public int SingleLengthCm { get; set; } = 10;

    public int SingleWidthCm { get; set; } = 10;

    public int SingleHeightCm { get; set; } = 10;

    public List<ProductAdminInitialSizeStockVm> InitialSizeStocks { get; set; } = new();

    public List<ProductAdminCustomOptionStockVm> VolumeOptionStocks { get; set; } = new();

    public List<ProductAdminVariantRowVm> Variants { get; set; } = new();

    public List<ProductAdminImageRowVm> Images { get; set; } = new();

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
    public int? ColorId { get; set; }

    public string? ColorName { get; set; }

    [StringLength(50)]
    public string? OptionName { get; set; }

    [Range(0, int.MaxValue)]
    public int StockQuantity { get; set; }
}

public class ProductAdminInitialSizeStockVm
{
    public int? ColorId { get; set; }

    public string? ColorName { get; set; }

    public int SizeId { get; set; }

    public string? SizeName { get; set; }

    [Range(0, int.MaxValue)]
    public int StockQuantity { get; set; }

    public bool IsSelected { get; set; }
}

public class ProductAdminSizeGuideRowVm
{
    public string? SizeLabel { get; set; }

    public string? HeightRange { get; set; }

    public string? WeightRange { get; set; }

    public string? ClothingLength { get; set; }
    public string? ClothingWidth { get; set; }
    public string? ShoeLength { get; set; }
    public string? ShoulderWidth { get; set; }
    public string? Bust { get; set; }
    public string? Waist { get; set; }
    public string? Hip { get; set; }

    public string? Note { get; set; }
}

public class ProductAdminVariantRowVm
{
    public int VariantId { get; set; }
    [StringLength(100)]
    public string? Sku { get; set; }

    public int? ColorId { get; set; }

    public string? ColorName { get; set; }
    public string? ColorHex { get; set; }

    public int? SizeId { get; set; }

    public string? SizeName { get; set; }

    // Dynamic 2-tier values
    public int? OptionValue1Id { get; set; }
    public string? Option1Value { get; set; }
    public string? Option1ImageUrl { get; set; }
    public string? ImageUrl { get; set; }

    public int? OptionValue2Id { get; set; }
    public string? Option2Value { get; set; }

    public string? VariantLabel { get; set; }

    // Physical weight & dimensions for GHN
    public int WeightGram { get; set; } = 300;
    public int LengthCm { get; set; } = 10;
    public int WidthCm { get; set; } = 10;
    public int HeightCm { get; set; } = 10;

    [Range(0, double.MaxValue)]
    public decimal Price { get; set; }

    [Range(0, double.MaxValue)]
    public decimal? SalePrice { get; set; }

    public DateTime? SaleStartDate { get; set; }

    public DateTime? SaleEndDate { get; set; }

    public int StockQuantity { get; set; }

    public decimal CostPrice { get; set; } = 0m;

    public bool IsActive { get; set; } = true;
    public string? IsActiveStr { get; set; }
}

public class ProductAdminVariantEditVm
{
    public int ProductId { get; set; }

    public int? VariantId { get; set; }

    public int? ColorId { get; set; }

    public int? SizeId { get; set; }

    [StringLength(100)]
    public string? Sku { get; set; }

    [Range(0, double.MaxValue)]
    public decimal Price { get; set; }

    [Range(0, int.MaxValue)]
    public int StockQuantity { get; set; }

    public decimal CostPrice { get; set; } = 0m;

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
