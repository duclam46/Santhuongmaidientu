namespace DOAN_BANHANG.ViewModels;

public class ShopListVm
{
    public int Page { get; set; }

    public int PageSize { get; set; }

    public int TotalItems { get; set; }

    public int TotalPages { get; set; }

    public int? SelectedCategoryId { get; set; }

    public string? SelectedCategoryName { get; set; }

    public int? SelectedMinRating { get; set; }

    public int? SelectedMaxRating { get; set; }

    public string? SelectedTier { get; set; } // null | "Mall" | "Standard"

    public IReadOnlyList<ShopCategoryFilterItemVm> Categories { get; set; } = Array.Empty<ShopCategoryFilterItemVm>();

    public IReadOnlyList<ShopSummaryVm> Shops { get; set; } = Array.Empty<ShopSummaryVm>();
}
