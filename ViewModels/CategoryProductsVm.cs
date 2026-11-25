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

    public int? SelectedBrandId { get; set; }

    public IReadOnlyList<BrandFilterItemVm> Brands { get; set; } = Array.Empty<BrandFilterItemVm>();

    public IReadOnlyList<DynamicAttributeFilterVm> DynamicAttributes { get; set; } = Array.Empty<DynamicAttributeFilterVm>();

    public Dictionary<int, string> SelectedAttributes { get; set; } = new();

    public IReadOnlyList<ProductCardVm> Products { get; set; } = Array.Empty<ProductCardVm>();
}

public class BrandFilterItemVm
{
    public int BrandId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? LogoUrl { get; set; }
    public int ProductCount { get; set; }
    public bool IsSelected { get; set; }
}

public class DynamicAttributeFilterVm
{
    public int AttributeId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Unit { get; set; }
    public List<string> AvailableValues { get; set; } = new();
    public string? SelectedValue { get; set; }
}
