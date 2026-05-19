using DOAN_BANHANG.Models;

namespace DOAN_BANHANG.Helpers;

public static class PartnerShopAddressHelper
{
    public static string ComposeAddress(string? streetAddress, string? wardName, string? districtName, string? provinceName)
        => string.Join(", ", new[] { streetAddress, wardName, districtName, provinceName }
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s!.Trim()));

    public static bool TryNormalizeShopAddress(
        string? fullName,
        string? phoneNumber,
        int provinceId,
        string? provinceName,
        int districtId,
        string? districtName,
        string? wardCode,
        string? wardName,
        string? streetAddress,
        out string error,
        out UserAddress normalized)
    {
        error = string.Empty;
        normalized = new UserAddress();

        fullName = string.IsNullOrWhiteSpace(fullName) ? null : fullName.Trim();
        phoneNumber = string.IsNullOrWhiteSpace(phoneNumber) ? null : phoneNumber.Trim();
        provinceName = string.IsNullOrWhiteSpace(provinceName) ? null : provinceName.Trim();
        districtName = string.IsNullOrWhiteSpace(districtName) ? null : districtName.Trim();
        wardCode = string.IsNullOrWhiteSpace(wardCode) ? null : wardCode.Trim();
        wardName = string.IsNullOrWhiteSpace(wardName) ? null : wardName.Trim();
        streetAddress = string.IsNullOrWhiteSpace(streetAddress) ? null : streetAddress.Trim();

        if (string.IsNullOrWhiteSpace(fullName))
        {
            error = "Vui lòng nhập họ và tên.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            error = "Vui lòng nhập số điện thoại.";
            return false;
        }

        if (provinceId <= 0 || string.IsNullOrWhiteSpace(provinceName))
        {
            error = "Vui lòng chọn Tỉnh/Thành phố.";
            return false;
        }

        if (districtId <= 0 || string.IsNullOrWhiteSpace(districtName))
        {
            error = "Vui lòng chọn Quận/Huyện.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(wardCode) || string.IsNullOrWhiteSpace(wardName))
        {
            error = "Vui lòng chọn Phường/Xã.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(streetAddress))
        {
            error = "Vui lòng nhập địa chỉ (số nhà, tên đường).";
            return false;
        }

        if (fullName.Length > 150) fullName = fullName[..150];
        if (phoneNumber.Length > 20) phoneNumber = phoneNumber[..20];
        if (provinceName.Length > 100) provinceName = provinceName[..100];
        if (districtName.Length > 100) districtName = districtName[..100];
        if (wardCode.Length > 50) wardCode = wardCode[..50];
        if (wardName.Length > 100) wardName = wardName[..100];
        if (streetAddress.Length > 500) streetAddress = streetAddress[..500];

        normalized = new UserAddress
        {
            FullName = fullName,
            PhoneNumber = phoneNumber,
            ProvinceId = provinceId,
            ProvinceName = provinceName,
            DistrictId = districtId,
            DistrictName = districtName,
            WardCode = wardCode,
            WardName = wardName,
            StreetAddress = streetAddress,
            AddressType = AddressType.Shop,
            IsDefault = true
        };

        return true;
    }
}
