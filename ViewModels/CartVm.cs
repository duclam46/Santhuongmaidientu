namespace DOAN_BANHANG.ViewModels;

public sealed class AddToCartRequest
{
    public int VariantId { get; set; }

    public int Quantity { get; set; } = 1;
}

public sealed class UpdateCartQuantityRequest
{
    public int CartItemId { get; set; }

    public int Quantity { get; set; }
}

public sealed class RemoveCartItemRequest
{
    public int CartItemId { get; set; }
}

public sealed class SetCartItemSelectedRequest
{
    public int CartItemId { get; set; }

    public bool Selected { get; set; }
}

public sealed class SelectOnlyCartItemRequest
{
    public int VariantId { get; set; }
}

public sealed class CartItemsResponse
{
    public int ItemCount { get; set; }

    public decimal SubTotal { get; set; }

    public int SelectedItemCount { get; set; }

    public decimal SelectedSubTotal { get; set; }

    public List<CartItemDto> Items { get; set; } = new();
}

public sealed class CartSuggestionsResponse
{
    public List<SuggestedProductDto> Items { get; set; } = new();
}

public sealed class SuggestedProductDto
{
    public int ProductId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Slug { get; set; } = string.Empty;

    public decimal Price { get; set; }

    public decimal? OldPrice { get; set; }

    public int DiscountPercent { get; set; }

    public string? PrimaryImageUrl { get; set; }

    public string? CategoryName { get; set; }
}

public sealed class CartItemDto
{
    public int CartItemId { get; set; }

    public int VariantId { get; set; }

    public string ProductName { get; set; } = string.Empty;

    public string ProductSlug { get; set; } = string.Empty;

    public string ImageUrl { get; set; } = string.Empty;

    public string? Color { get; set; }

    public string? Size { get; set; }

    public decimal UnitPrice { get; set; }

    public int Quantity { get; set; }

    public bool IsSelected { get; set; }
}
