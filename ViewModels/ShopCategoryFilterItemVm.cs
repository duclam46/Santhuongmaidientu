namespace DOAN_BANHANG.ViewModels;

public class ShopCategoryFilterItemVm
{
    public int CategoryId { get; set; }

    public string Name { get; set; } = string.Empty;

    public int ShopCount { get; set; }

    public bool IsSelected { get; set; }
}
