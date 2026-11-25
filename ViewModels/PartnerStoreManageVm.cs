namespace DOAN_BANHANG.ViewModels;

public sealed class PartnerStoreManageVm
{
    public string Email { get; set; } = string.Empty;

    public string? PhoneNumber { get; set; }

    public string? ShopName { get; set; }

    public string? ShopSlug { get; set; }

    public string? ShopDescription { get; set; }

    public string? AutoReplyMessage { get; set; }

    public string? Address { get; set; }

    public int? ProvinceId { get; set; }

    public string? ProvinceName { get; set; }

    public int? DistrictId { get; set; }

    public string? DistrictName { get; set; }

    public string? WardCode { get; set; }

    public string? WardName { get; set; }

    public string? StreetAddress { get; set; }

    public string? ShopLogoUrl { get; set; }

    public string? ShopCoverUrl { get; set; }

    // Shop Tier & Mall Upgrade Status
    public bool IsMall { get; set; }

    public string ShopTier { get; set; } = "Standard";

    public string MallStatus { get; set; } = "None"; // None | Pending | Approved | Rejected

    public string? BusinessName { get; set; }

    public string? BusinessTaxCode { get; set; }

    public string? BrandName { get; set; }

    public string? BusinessLicenseUrl { get; set; }

    public string? BrandAuthorizationUrl { get; set; }

    public string? MallRejectionReason { get; set; }

    public DateTime? MallApprovedAt { get; set; }

    // Live Operational Metrics for Mall Upgrade Eligibility
    public int CompletedOrderCount { get; set; }

    public decimal CancelRatePercent { get; set; }

    public decimal AverageRating { get; set; }

    public int ActiveProductCount { get; set; }
}
