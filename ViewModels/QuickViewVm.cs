namespace DOAN_BANHANG.ViewModels;

public class QuickViewVm
{
    public int ProductId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Slug { get; set; } = string.Empty;

    public string? CategoryName { get; set; }

    public string? BrandName { get; set; }

    public string Option1Label { get; set; } = "Màu sắc";

    public string Option2Label { get; set; } = "Kích cỡ";

    public decimal Price { get; set; }

    public string? DefaultColor { get; set; }

    public string? DefaultSize { get; set; }

    public decimal AverageRating { get; set; }

    public int ReviewCount { get; set; }

    public string? Description { get; set; }

    public List<string> ImageUrls { get; set; } = new();

    public List<QuickViewColorVm> Colors { get; set; } = new();

    public List<QuickViewSizeVm> Sizes { get; set; } = new();

    public List<QuickViewVariantVm> Variants { get; set; } = new();
}

public class QuickViewColorVm
{
    public string Key { get; set; } = string.Empty;

    public string Hex { get; set; } = "#dfe3e8";
}

public class QuickViewSizeVm
{
    public string Label { get; set; } = string.Empty;
}

public class QuickViewVariantVm
{
    public int VariantId { get; set; }

    public string? Color { get; set; }

    public string? Size { get; set; }

    public decimal Price { get; set; }

    public int StockQuantity { get; set; }
}
