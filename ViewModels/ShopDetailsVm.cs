namespace DOAN_BANHANG.ViewModels;

public class ShopDetailsVm
{
    public ShopSummaryVm Shop { get; set; } = new();

    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 12;

    public int TotalItems { get; set; }

    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalItems / (double)PageSize);

    public List<ProductCardVm> Products { get; set; } = new();

    public List<StorefrontVoucherVm> ShopVouchers { get; set; } = new();
}
