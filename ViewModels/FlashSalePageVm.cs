namespace DOAN_BANHANG.ViewModels;

public class FlashSalePageVm
{
    public List<FlashSaleTabVm> Tabs { get; set; } = new();
}

public class FlashSaleTabVm
{
    public string Key { get; set; } = "all";

    public string Label { get; set; } = string.Empty;

    public List<ProductCardVm> UpTo10 { get; set; } = new();

    public List<ProductCardVm> Above10 { get; set; } = new();
}
