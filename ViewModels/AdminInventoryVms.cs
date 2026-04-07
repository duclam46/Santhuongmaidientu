namespace DOAN_BANHANG.ViewModels;

public sealed class AdminInventoryQueryVm
{
    public string? Search { get; set; }

    public int? ProductId { get; set; }
}

public sealed class AdminInventoryIndexVm
{
    public AdminInventoryQueryVm Query { get; set; } = new();

    public List<AdminInventoryProductPickVm> Products { get; set; } = new();

    public AdminInventorySelectedProductVm? SelectedProduct { get; set; }

    public AdminInventoryUpdateVm Update { get; set; } = new();
}

public sealed class AdminInventoryProductPickVm
{
    public int ProductId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? PrimaryImageUrl { get; set; }

    public int VariantCount { get; set; }

    public int TotalStockQuantity { get; set; }
}

public sealed class AdminInventorySelectedProductVm
{
    public int ProductId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? PrimaryImageUrl { get; set; }

    public List<AdminInventoryVariantRowVm> Variants { get; set; } = new();
}

public sealed class AdminInventoryVariantRowVm
{
    public int VariantId { get; set; }

    public string Sku { get; set; } = string.Empty;

    public string? ColorName { get; set; }

    public string? SizeName { get; set; }

    public int StockQuantity { get; set; }

    public int ReservedStock { get; set; }

    public int AvailableStock { get; set; }

    public bool IsActive { get; set; }
}

public sealed class AdminInventoryUpdateVm
{
    public int ProductId { get; set; }

    public string? Note { get; set; }

    public List<AdminInventoryVariantUpdateRowVm> Variants { get; set; } = new();
}

public sealed class AdminInventoryVariantUpdateRowVm
{
    public int VariantId { get; set; }

    public int StockQuantity { get; set; }
}
