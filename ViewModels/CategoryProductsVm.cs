namespace DOAN_BANHANG.ViewModels;

public class CategoryProductsVm
{
    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 12;

    public int TotalItems { get; set; }

    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalItems / (double)PageSize);

    public string CategoryName { get; set; } = string.Empty;

    public string? CategorySlug { get; set; }

    public int? SelectedChildCategoryId { get; set; }

    public string? Sort { get; set; }

    public string? SearchTerm { get; set; }

    public decimal? SelectedMinPrice { get; set; }

    public decimal? SelectedMaxPrice { get; set; }

    public int? SelectedRating { get; set; }

    public string? SelectedColor { get; set; }

    public string? SelectedSize { get; set; }

    public string? SelectedPackSize { get; set; }

    public IReadOnlyList<string> SelectedDiscounts { get; set; } = Array.Empty<string>();

    public decimal PriceMin { get; set; }

    public decimal PriceMax { get; set; }

    public IReadOnlyList<CategoryFilterVm> ChildCategories { get; set; } = Array.Empty<CategoryFilterVm>();

    public IReadOnlyList<ProductCardVm> Products { get; set; } = Array.Empty<ProductCardVm>();
}
