using DOAN_BANHANG.Models;

namespace DOAN_BANHANG.ViewModels;

/// <summary>ViewModel cho trang chủ ví (My Wallet).</summary>
public class WalletIndexVm
{
    public decimal Balance { get; set; }
    public List<WalletTransaction> Transactions { get; set; } = new();

    public int Page     { get; set; } = 1;
    public int PageSize { get; set; } = 15;
    public int TotalCount { get; set; }

    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
}
