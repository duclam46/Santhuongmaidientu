namespace DOAN_BANHANG.Helpers;

public static class ColorUi
{
    public static bool TryGetColorIconUrl(string? colorName, out string iconUrl)
    {
        iconUrl = string.Empty;
        var key = (colorName ?? string.Empty).Trim().ToLowerInvariant();

        iconUrl = key switch
        {
            "green" => "/assets/images/product-details/color-selector-1.png",
            "blue" => "/assets/images/product-details/color-selector-2.png",
            "yellow" => "/assets/images/product-details/color-selector-3.png",
            "red" => "/assets/images/product-details/color-selector-4.png",
            _ => string.Empty
        };

        return !string.IsNullOrEmpty(iconUrl);
    }
}
