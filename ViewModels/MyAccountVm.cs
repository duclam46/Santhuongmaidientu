namespace DOAN_BANHANG.ViewModels;

public sealed class MyAccountPageVm
{
    public bool IsSignedIn { get; set; }

    public MyAccountVm? Account { get; set; }
}

public sealed class MyAccountVm
{
    public int AccountId { get; set; }

    public string Email { get; set; } = string.Empty;

    public bool HasPassword { get; set; }

    public string Role { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string? AuthProvider { get; set; }

    public string? FullName { get; set; }

    public string? PhoneNumber { get; set; }

    public string? Address { get; set; }

    public string? AvatarUrl { get; set; }

    public List<UserAddressVm> Addresses { get; set; } = new();

    public List<OrderSummaryVm> Orders { get; set; } = new();

    public List<WishlistItemVm> WishlistItems { get; set; } = new();

    public List<ReviewSummaryVm> Reviews { get; set; } = new();

    public List<ReviewSummaryVm> RecentReviews { get; set; } = new();

    public int ReviewsTotalCount { get; set; }

    public int ReviewsPage { get; set; } = 1;

    public int ReviewsPageSize { get; set; } = 5;

    public DateTime CreatedAt { get; set; }

    public DateTime? LastLoginAt { get; set; }
}

public sealed class WishlistItemVm
{
    public int ProductId { get; set; }

    public string ProductName { get; set; } = string.Empty;

    public string ProductSlug { get; set; } = string.Empty;

    public string? ProductImageUrl { get; set; }

    public string? CategoryName { get; set; }

    public decimal Price { get; set; }

    public decimal? SalePrice { get; set; }

    public int VariantId { get; set; }

    public int AvailableStock { get; set; }

    public decimal AverageRating { get; set; }

    public int ReviewCount { get; set; }
}

public sealed class ReviewSummaryVm
{
    public int Id { get; set; }

    public int ProductId { get; set; }

    public string ProductName { get; set; } = string.Empty;

    public string ProductSlug { get; set; } = string.Empty;

    public string? ProductImageUrl { get; set; }

    public int Rating { get; set; }

    public string? Comment { get; set; }

    public DateTime CreatedAt { get; set; }
}

public sealed class OrderSummaryVm
{
    public int Id { get; set; }
    public string OrderCode { get; set; } = string.Empty;
    public string OrderStatus { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = string.Empty;
    public string PaymentMethod { get; set; } = string.Empty;
    public DateTime? PaymentDeadlineUtc { get; set; }
    public decimal FinalAmount { get; set; }
    public decimal ShippingFee { get; set; }
    public int ItemCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string FirstProductSlug { get; set; } = string.Empty;
    public bool HasReviewed { get; set; }
    public int FirstProductId { get; set; }
    public string FirstProductName { get; set; } = string.Empty;
    public string? FirstProductImageUrl { get; set; }
    public string? FirstProductVariantName { get; set; }
    public bool IsReceivedByCustomer { get; set; }
}

public sealed class UserAddressVm
{
    public int Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;

    public int AddressType { get; set; }

    public int ProvinceId { get; set; }
    public string ProvinceName { get; set; } = string.Empty;
    public int DistrictId { get; set; }
    public string DistrictName { get; set; } = string.Empty;
    public string WardCode { get; set; } = string.Empty;
    public string WardName { get; set; } = string.Empty;
    public string StreetAddress { get; set; } = string.Empty;

    public bool IsDefault { get; set; }
}
