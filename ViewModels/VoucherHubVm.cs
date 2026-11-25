namespace DOAN_BANHANG.ViewModels;

public class VoucherHubVm
{
    public bool IsAuthenticated { get; set; }
    public int SavedCount { get; set; }
    public List<VoucherHubCardVm> FreeShippingVouchers { get; set; } = new();
    public List<VoucherHubCardVm> PlatformVouchers { get; set; } = new();
    public List<VoucherHubCardVm> ShopVouchers { get; set; } = new();
    public int TotalActiveCount => FreeShippingVouchers.Count + PlatformVouchers.Count + ShopVouchers.Count;
}

public class MyVouchersVm
{
    public bool IsAuthenticated { get; set; }
    public List<VoucherHubCardVm> UsableVouchers { get; set; } = new();
    public List<VoucherHubCardVm> ExpiredOrUsedVouchers { get; set; } = new();
    public int ExpiringSoonCount => UsableVouchers.Count(v => v.IsExpiringSoon);
    public int FreeShippingCount => UsableVouchers.Count(v => v.DiscountType == "FreeShipping");
}

public class VoucherHubCardVm
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string DiscountType { get; set; } = string.Empty;
    public decimal DiscountValue { get; set; }
    public decimal MinOrderValue { get; set; }
    public decimal? MaxDiscountValue { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public bool IsShopVoucher { get; set; }
    public string ShopName { get; set; } = string.Empty;
    public string? ShopSlug { get; set; }
    public string? ShopLogoUrl { get; set; }
    public bool IsMallShop { get; set; }
    public string ScopeType { get; set; } = "All";
    public string ScopeLabel { get; set; } = string.Empty;
    public string TargetAudience { get; set; } = "Public";
    public bool IsSaved { get; set; }
    public int UsageLimit { get; set; }
    public int UsedCount { get; set; }
    public int UsedPercent { get; set; }
    public int RemainingPercent { get; set; } = 100;
    public bool IsExpiringSoon { get; set; }
    public bool IsExpiredOrExhausted { get; set; }
    public string StatusNote { get; set; } = string.Empty;
    public string UseNowUrl => !string.IsNullOrWhiteSpace(ShopSlug) ? $"/shop/{ShopSlug}" : "/products";
}
