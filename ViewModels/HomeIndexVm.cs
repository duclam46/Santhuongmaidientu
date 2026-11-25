namespace DOAN_BANHANG.ViewModels;

public class HomeIndexVm
{
    public List<CategoryLinkVm> TopCategories { get; set; } = new();

    public List<ProductCardVm> FeaturedProducts { get; set; } = new();

    public List<ProductCardVm> LatestProducts { get; set; } = new();

    public List<ProductCardVm> TopSellers { get; set; } = new();

    public List<ProductCardVm> MostWanted { get; set; } = new();

    public Dictionary<string, List<ProductCardVm>> CategoryDeals { get; set; } = new();

    public List<DOAN_BANHANG.Models.HomeBanner> DynamicBanners { get; set; } = new();

    public List<DOAN_BANHANG.Models.PlatformCampaign> ActiveCampaigns { get; set; } = new();

    public List<StorefrontVoucherVm> HotVouchers { get; set; } = new();
}

public class CategoryLinkVm
{
    public int CategoryId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Slug { get; set; } = string.Empty;

    public string? IconUrl { get; set; }

    public int ProductCount { get; set; }
}