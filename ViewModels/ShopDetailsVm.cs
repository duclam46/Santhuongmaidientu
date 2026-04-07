namespace DOAN_BANHANG.ViewModels;

public class ShopDetailsVm
{
    public ShopSummaryVm Shop { get; set; } = new();

    public List<ProductCardVm> Products { get; set; } = new();
}
