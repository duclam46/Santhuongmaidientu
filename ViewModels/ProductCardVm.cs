namespace DOAN_BANHANG.ViewModels;

public class ProductCardVm
{
    public int ProductId { get; set; }

    public int DefaultVariantId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Slug { get; set; } = string.Empty;

    public decimal Price { get; set; }

    public decimal? OldPrice { get; set; }

    public decimal RatingAverage { get; set; }

    public int RatingCount { get; set; }

    public int DiscountPercent { get; set; }

    public string? PrimaryImageUrl { get; set; }
}
