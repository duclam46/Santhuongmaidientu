using System.Collections.Generic;

namespace DOAN_BANHANG.ViewModels;

public class ProductCompareVm
{
    public List<ProductCompareItemVm> Products { get; set; } = new();
}

public class ProductCompareItemVm
{
    public int ProductId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public decimal Price { get; set; }
    public decimal? OldPrice { get; set; }
    public decimal Rating { get; set; }
    public int ReviewCount { get; set; }
    public string? Description { get; set; }
    public string? CategoryName { get; set; }
    public string? BrandName { get; set; }
    public int WarrantyMonths { get; set; }
    public string? WarrantyType { get; set; }
    public bool IsReturnable { get; set; } = true;
    public Dictionary<string, string> Specs { get; set; } = new();
    public List<string> Colors { get; set; } = new();
    public List<string> Sizes { get; set; } = new();
    public bool InStock { get; set; }
    public string? ShopName { get; set; }
}
