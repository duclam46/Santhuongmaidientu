namespace DOAN_BANHANG.Services;

public sealed class GhnOptions
{
    public string BaseUrl { get; set; } = "https://online-gateway.ghn.vn/shiip/public-api";

    public string Token { get; set; } = string.Empty;

    /// <summary>
    /// GHN ShopId used in request headers. Required for fee calculation endpoints.
    /// </summary>
    public int ShopId { get; set; }

    /// <summary>
    /// Default package weight in grams when product weight is not available.
    /// </summary>
    public int DefaultWeightGram { get; set; } = 500;

    public int DefaultLengthCm { get; set; } = 20;

    public int DefaultWidthCm { get; set; } = 15;

    public int DefaultHeightCm { get; set; } = 10;

    /// <summary>
    /// GHN service type. 2 = light goods, 5 = heavy goods.
    /// </summary>
    public int ServiceTypeId { get; set; } = 2;
}
