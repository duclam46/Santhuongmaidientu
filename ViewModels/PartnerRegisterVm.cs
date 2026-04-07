namespace DOAN_BANHANG.ViewModels;

public class PartnerRegisterVm
{
    public string? FullName { get; set; }

    public string? Email { get; set; }

    public string? PhoneNumber { get; set; }

    public string? ShopName { get; set; }

    public string? ShopDescription { get; set; }

    public string? Address { get; set; }

    // GHN-style address (same structure as My Account address settings)
    public int ProvinceId { get; set; }

    public string? ProvinceName { get; set; }

    public int DistrictId { get; set; }

    public string? DistrictName { get; set; }

    public string? WardCode { get; set; }

    public string? WardName { get; set; }

    public string? StreetAddress { get; set; }

    public string? Password { get; set; }

    public string? ConfirmPassword { get; set; }

    public string? SuccessMessage { get; set; }
}
