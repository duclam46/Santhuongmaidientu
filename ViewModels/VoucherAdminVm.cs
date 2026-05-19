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
}

public class VoucherAdminListVm
{
    public VoucherAdminQueryVm Query { get; set; } = new();
    public List<VoucherAdminListItemVm> Items { get; set; } = new();
    public int TotalItems { get; set; }
    public bool IsAdmin { get; set; } // Để biết View nên hiển thị cột Shop Name hay không
}

public class VoucherAdminUpsertVm
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập mã Voucher")]
    [MaxLength(50)]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng chọn loại giảm giá")]
    [MaxLength(20)]
    public string DiscountType { get; set; } = "Percent"; // Percent | FixedAmount

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

    [Required(ErrorMessage = "Vui lòng chọn ngày bắt đầu")]
    public DateTime? StartDate { get; set; }

    [Required(ErrorMessage = "Vui lòng chọn ngày kết thúc")]
    public DateTime? EndDate { get; set; }

    public bool IsActive { get; set; } = true;
}
