namespace DOAN_BANHANG.Models;

public sealed class UserAddress
{
    public int Id { get; set; }

    public int AccountId { get; set; }

    public Account? Account { get; set; }

    public string FullName { get; set; } = string.Empty;

    public string PhoneNumber { get; set; } = string.Empty;

    public int ProvinceId { get; set; }

    public string ProvinceName { get; set; } = string.Empty;

    public int DistrictId { get; set; }

    public string DistrictName { get; set; } = string.Empty;

    public string WardCode { get; set; } = string.Empty;

    public string WardName { get; set; } = string.Empty;

    public string StreetAddress { get; set; } = string.Empty;

    public AddressType AddressType { get; set; } = AddressType.Personal;

    public bool IsDefault { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
