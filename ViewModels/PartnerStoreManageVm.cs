namespace DOAN_BANHANG.ViewModels;

public sealed class PartnerStoreManageVm
{
    public string Email { get; set; } = string.Empty;

    public string? PhoneNumber { get; set; }

    public string? ShopName { get; set; }

    public string? ShopSlug { get; set; }

    public string? ShopDescription { get; set; }

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
}
