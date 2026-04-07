using System.ComponentModel.DataAnnotations;

namespace DOAN_BANHANG.ViewModels;

public class AdminFlashSaleIndexVm
{
    public string? Search { get; set; }

    public List<AdminFlashSaleProductItemVm> Products { get; set; } = new();

    public AdminFlashSaleApplyVm Apply { get; set; } = new();

    public AdminFlashSaleApplyVm ApplySelected { get; set; } = new() { Scope = "selected" };
}

public class AdminFlashSaleListVm
{
    public string? Search { get; set; }

    public List<AdminFlashSaleConfiguredItemVm> Items { get; set; } = new();
}

public class AdminFlashSaleConfiguredItemVm
{
    public int ProductId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? PrimaryImageUrl { get; set; }

    public int VariantOnSaleCount { get; set; }

    public decimal MinPrice { get; set; }

    public decimal? MinSalePrice { get; set; }

    public DateTime? SaleStartDate { get; set; }

    public DateTime? SaleEndDate { get; set; }
}

public class AdminFlashSaleProductItemVm
{
    public int ProductId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public int VariantCount { get; set; }

    public string? PrimaryImageUrl { get; set; }

    public decimal MinPrice { get; set; }

    public decimal? MinSalePrice { get; set; }

    public DateTime? SaleStartDate { get; set; }

    public DateTime? SaleEndDate { get; set; }
}

public class AdminFlashSaleApplyVm
{
    [Required]
    public string Scope { get; set; } = "all"; // all | selected | single

    // JSON map: { [productId]: { mode: "all"|"selected", variantIds: number[] } }
    // Used by the Setup wizard when choosing variants per product.
    public string? VariantSelectionJson { get; set; }

    // bulk | pervariant
    public string PricingMode { get; set; } = "bulk";

    // JSON map: { [variantId]: { discountPercent: number, saleStartDate: "yyyy-MM-ddTHH:mm", saleEndDate: "yyyy-MM-ddTHH:mm" } }
    // Used when PricingMode = pervariant.
    public string? PerVariantConfigJson { get; set; }

    [Range(1, 90, ErrorMessage = "Giảm giá nên từ 1% đến 90%.")]
    public int DiscountPercent { get; set; } = 10;

    public DateTime? SaleStartDate { get; set; }

    public DateTime? SaleEndDate { get; set; }

    public List<int> ProductIds { get; set; } = new();

    [Required]
    public string Action { get; set; } = "apply"; // apply | clear
}
