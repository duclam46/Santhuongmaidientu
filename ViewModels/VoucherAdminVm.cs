using System.ComponentModel.DataAnnotations;

namespace DOAN_BANHANG.ViewModels;

public class VoucherAdminQueryVm
{
    public string? Search { get; set; }
    public string? Status { get; set; } // All, Active, Inactive, Expired
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}

public class VoucherAdminListItemVm
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string DiscountType { get; set; } = string.Empty;
    public decimal DiscountValue { get; set; }
    public decimal MinOrderValue { get; set; }
    public decimal? MaxDiscountValue { get; set; }
    public int UsageLimit { get; set; }
    public int UsedCount { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public int? ShopId { get; set; }
    public string? ShopName { get; set; }

    public string ScopeType { get; set; } = "All";
    public string ScopeDisplayText { get; set; } = "Toàn bộ";
    public string TargetAudience { get; set; } = "Public";
    public string AudienceDisplayText { get; set; } = "Công khai";
    public int MaxUsagePerUser { get; set; } = 1;
    public bool ExcludeFlashSaleItems { get; set; }
    public string FundingSourceText { get; set; } = string.Empty;
}

public class VoucherAdminListVm
{
    public VoucherAdminQueryVm Query { get; set; } = new();
    public List<VoucherAdminListItemVm> Items { get; set; } = new();
    public int TotalItems { get; set; }
    public int AllCount { get; set; }
    public int ActiveCount { get; set; }
    public int InactiveCount { get; set; }
    public int ExpiredCount { get; set; }
    public int TotalUsedCount { get; set; }
    public bool IsAdmin { get; set; }
}

public class VoucherScopeCategoryOptionVm
{
    public int CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public decimal CommissionRatePercent { get; set; }
    public int ProductCount { get; set; }
}

public class VoucherScopeProductOptionVm
{
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
    public decimal SellingPrice { get; set; }
    public decimal CostPrice { get; set; }
    public decimal EstimatedNetMarginPercent { get; set; }
    public int CurrentStock { get; set; }
    public int UnitsSold { get; set; }
    public bool IsInFlashSale { get; set; }
    public string BcgTag { get; set; } = string.Empty;
    public string BcgBadgeClass { get; set; } = string.Empty;
}

public class VoucherAdminUpsertVm
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập mã Voucher")]
    [MaxLength(50)]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng chọn loại giảm giá")]
    [MaxLength(20)]
    public string DiscountType { get; set; } = "Percent"; // Percent | FixedAmount | FreeShipping

    [Required(ErrorMessage = "Vui lòng nhập mức giảm")]
    [Range(0, double.MaxValue, ErrorMessage = "Mức giảm phải >= 0")]
    public decimal DiscountValue { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập giá trị đơn hàng tối thiểu")]
    [Range(0, double.MaxValue, ErrorMessage = "Giá trị đơn hàng tối thiểu >= 0")]
    public decimal MinOrderValue { get; set; } = 0m;

    [Range(0, double.MaxValue, ErrorMessage = "Mức giảm tối đa >= 0")]
    public decimal? MaxDiscountValue { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập số lượng sử dụng")]
    [Range(0, int.MaxValue, ErrorMessage = "Số lượng không hợp lệ")]
    public int UsageLimit { get; set; } = 0;

    [Range(0, 100, ErrorMessage = "Giới hạn lượt/khách từ 0 đến 100")]
    public int MaxUsagePerUser { get; set; } = 1;

    [MaxLength(40)]
    public string ScopeType { get; set; } = "All"; // All | ByCategory | SpecificProducts | MallOnly

    public List<int> SelectedCategoryIds { get; set; } = new();

    public List<int> SelectedProductIds { get; set; } = new();

    [MaxLength(30)]
    public string TargetAudience { get; set; } = "Public"; // Public | NewBuyerOnly | PrivateChatOnly

    public bool ExcludeFlashSaleItems { get; set; } = true;

    [Required(ErrorMessage = "Vui lòng chọn ngày bắt đầu")]
    public DateTime? StartDate { get; set; }

    [Required(ErrorMessage = "Vui lòng chọn ngày kết thúc")]
    public DateTime? EndDate { get; set; }

    public bool IsActive { get; set; } = true;

    // Context metadata for View & AI Margin Guard
    public bool IsAdmin { get; set; }
    public string ShopName { get; set; } = string.Empty;
    public decimal ShopAvgNetMarginPercent { get; set; } = 28m;
    public decimal ShopAvgCommissionPercent { get; set; } = 10m;
    public List<VoucherScopeCategoryOptionVm> AvailableCategories { get; set; } = new();
    public List<VoucherScopeProductOptionVm> AvailableProducts { get; set; } = new();
}
