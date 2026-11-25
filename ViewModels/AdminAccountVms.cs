namespace DOAN_BANHANG.ViewModels;

public sealed class AdminAccountListQueryVm
{
    public string? Search { get; set; }

    public string? Role { get; set; }

    public string? Status { get; set; }

    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 20;
}

public sealed class AdminAccountListItemVm
{
    public int Id { get; set; }

    public string Email { get; set; } = string.Empty;

    public string Role { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string? AuthProvider { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? LastLoginAt { get; set; }

    public string? FullName { get; set; }

    public string? PhoneNumber { get; set; }

    public string? AvatarUrl { get; set; }

    public string? ShopName { get; set; }

    public string? ShopSlug { get; set; }

    public bool IsMall { get; set; }

    public int OrderCount { get; set; }

    public int ProductCount { get; set; }
}

public sealed class AdminAccountListVm
{
    public AdminAccountListQueryVm Query { get; set; } = new();

    public List<AdminAccountListItemVm> Accounts { get; set; } = new();

    public int TotalItems { get; set; }

    public int TotalCustomers { get; set; }

    public int TotalShops { get; set; }

    public int TotalMallShops { get; set; }

    public int TotalBlocked { get; set; }
}
