using DOAN_BANHANG.Models;

namespace DOAN_BANHANG.ViewModels;

public sealed class AdminPartnerApplicationListQueryVm
{
    public string? Status { get; set; }
    public string? Search { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public sealed class AdminPartnerApplicationListItemVm
{
    public int Id { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? FullName { get; set; }
    public string? PhoneNumber { get; set; }
    public string? ShopName { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class AdminPartnerApplicationListVm
{
    public AdminPartnerApplicationListQueryVm Query { get; set; } = new();
    public int TotalItems { get; set; }
    public int PendingItems { get; set; }
    public List<AdminPartnerApplicationListItemVm> Items { get; set; } = new();
}

public sealed class AdminPartnerApplicationDetailsVm
{
    public int Id { get; set; }
    public int AccountId { get; set; }
    public string Status { get; set; } = string.Empty;

    public string? FullName { get; set; }
    public string Email { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string? ShopName { get; set; }
    public string? ShopDescription { get; set; }

    public string? Address { get; set; }
    public int ProvinceId { get; set; }
    public string? ProvinceName { get; set; }
    public int DistrictId { get; set; }
    public string? DistrictName { get; set; }
    public string? WardCode { get; set; }
    public string? WardName { get; set; }
    public string? StreetAddress { get; set; }

    public string? RejectionReason { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? DecidedAt { get; set; }
    public int? DecidedByAccountId { get; set; }
}

public sealed class AdminPartnerApplicationDecisionVm
{
    public int Id { get; set; }
    public string? Reason { get; set; }
}
