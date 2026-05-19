using System.Security.Claims;
using DOAN_BANHANG.Helpers;
using DOAN_BANHANG.Models;
using DOAN_BANHANG.Services;
using DOAN_BANHANG.ViewModels;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DOAN_BANHANG.Controllers;

[Route("checkout")]
public sealed class CheckoutController : Controller
{
    private readonly DataContext _db;
    private readonly GhnClient _ghn;
    private readonly GhnOptions _ghnOptions;
    private readonly ILogger<CheckoutController> _logger;
    private readonly VnPayService _vnPay;
    private readonly WalletService _walletService;

    private const string SessionCartSelectionKey = "cart_selected";

    private sealed class CartSelectionState
    {
        public bool Initialized { get; set; }

        public HashSet<int> DeselectedIds { get; set; } = new();
    }

    public CheckoutController(
        DataContext db,
        GhnClient ghn,
        Microsoft.Extensions.Options.IOptions<GhnOptions> ghnOptions,
        ILogger<CheckoutController> logger,
        VnPayService vnPay,
        WalletService walletService)
    {
        _db = db;
        _ghn = ghn;
        _ghnOptions = ghnOptions.Value;
        _logger = logger;
        _vnPay = vnPay;
        _walletService = walletService;
    }

    private int? GetSignedInAccountId()
    {
        if (!(User?.Identity?.IsAuthenticated ?? false)) return null;

        var idRaw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(idRaw, out var id) ? id : null;
    }

    private async Task<decimal> GetGlobalVatRateAsync(CancellationToken cancellationToken)
    {
        var vatSetting = await _db.SystemSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == "Tax_VAT_Default", cancellationToken);

        if (vatSetting != null && decimal.TryParse(vatSetting.Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var parsedVat))
        {
            return parsedVat / 100m;
        }
        return 0.08m; // default fallback 8%
    }

    private CartSelectionState GetCartSelectionState()
        => HttpContext.Session.GetJson<CartSelectionState>(SessionCartSelectionKey) ?? new CartSelectionState();

    private static decimal GetEffectiveUnitPrice(ProductVariant variant, DateTime nowUtc)
    {
        var inSaleWindow = variant.SalePrice.HasValue
            && (!variant.SaleStartDate.HasValue || variant.SaleStartDate <= nowUtc)
            && (!variant.SaleEndDate.HasValue || variant.SaleEndDate >= nowUtc);

        return inSaleWindow ? variant.SalePrice!.Value : variant.Price;
    }

    private static string ComposeAddress(UserAddress a)
        => string.Join(", ", new[]
        {
            a.StreetAddress,
            a.WardName,
            a.DistrictName,
            a.ProvinceName
        }.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()));

    private static string GenerateOrderCode(DateTime nowUtc)
    {
        var stamp = nowUtc.ToString("yyyyMMddHHmmss");
        var rand = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        return $"ORD-{stamp}-{rand}";
    }

    private int ResolvePackageWeightGram(int quantity)
    {
        var baseWeight = _ghnOptions.DefaultWeightGram <= 0 ? 500 : _ghnOptions.DefaultWeightGram;
        var q = Math.Max(1, quantity);
        var weight = checked(baseWeight * q);
        return Math.Clamp(weight, 1, 1_600_000);
    }

    private (int length, int width, int height) ResolvePackageDimensionsCm()
    {
        var length = _ghnOptions.DefaultLengthCm <= 0 ? 20 : _ghnOptions.DefaultLengthCm;
        var width = _ghnOptions.DefaultWidthCm <= 0 ? 15 : _ghnOptions.DefaultWidthCm;
        var height = _ghnOptions.DefaultHeightCm <= 0 ? 10 : _ghnOptions.DefaultHeightCm;

        return (
            Math.Clamp(length, 1, 200),
            Math.Clamp(width, 1, 200),
            Math.Clamp(height, 1, 200)
        );
    }

    private async Task<decimal> CalculateShippingFeeForShopAsync(UserAddress shopAddress, UserAddress customerAddress, int totalQuantity, CancellationToken cancellationToken)
    {
        var serviceType = _ghnOptions.ServiceTypeId <= 0 ? 2 : _ghnOptions.ServiceTypeId;
        var weight = ResolvePackageWeightGram(totalQuantity);
        var (length, width, height) = ResolvePackageDimensionsCm();

        var req = new GhnCalculateFeeRequest
        {
            ServiceTypeId = serviceType,
            FromDistrictId = shopAddress.DistrictId,
            FromWardCode = shopAddress.WardCode,
            ToDistrictId = customerAddress.DistrictId,
            ToWardCode = customerAddress.WardCode,
            Weight = weight,
            Length = length,
            Width = width,
            Height = height,
            InsuranceValue = 0
        };

        var fee = await _ghn.CalculateFeeAsync(req, shopId: null, cancellationToken);
        return fee.Total;
    }

    private Task<CheckoutPageVm> BuildCheckoutVmAsync(
        int accountId,
        int? preferredAddressId,
        string? paymentMethod,
        string? voucherCode,
        string? shippingVoucherCode,
        string? discountVoucherCode,
        CancellationToken cancellationToken)
        => BuildCheckoutVmAsync(accountId, preferredAddressId, paymentMethod, buyNowVariantId: null, buyNowQuantity: null, voucherCode, shippingVoucherCode, discountVoucherCode, cancellationToken);

    private static decimal CalculateVoucherDiscountAmount(string discountType, decimal discountValue, decimal eligibleSubtotal, decimal shippingFee, decimal? maxDiscountValue)
    {
        decimal discountAmount;

        if (discountType == "Percent")
        {
            discountAmount = eligibleSubtotal * (discountValue / 100m);
        }
        else if (discountType == "FixedAmount")
        {
            discountAmount = discountValue;
        }
        else if (discountType == "FreeShipping")
        {
            discountAmount = discountValue;
            if (discountAmount <= 0)
            {
                discountAmount = shippingFee;
            }

            if (discountAmount > shippingFee)
            {
                discountAmount = shippingFee;
            }
        }
        else
        {
            discountAmount = 0m;
        }

        if (discountAmount > eligibleSubtotal && discountType != "FreeShipping")
        {
            discountAmount = eligibleSubtotal;
        }

        if (maxDiscountValue.HasValue && discountAmount > maxDiscountValue.Value)
        {
            discountAmount = maxDiscountValue.Value;
        }

        if (discountAmount < 0)
        {
            discountAmount = 0;
        }

        return discountAmount;
    }

    private async Task PopulateVouchersAsync(CheckoutPageVm vm, string? voucherCode, string? shippingVoucherCode, string? discountVoucherCode, CancellationToken cancellationToken)
    {
        if (vm.Items.Count == 0)
        {
            return;
        }

        var now = DateTime.Now;
        var shopIdsInOrder = vm.Items.Select(i => i.ShopId).Distinct().ToList();

        var activeVouchers = await _db.Vouchers
            .AsNoTracking()
            .Where(v => v.IsActive
                && v.StartDate <= now
                && v.EndDate >= now
                && (v.UsageLimit == 0 || v.UsedCount < v.UsageLimit)
                && (v.ShopId == null || shopIdsInOrder.Contains(v.ShopId.Value)))
            .ToListAsync(cancellationToken);

        var candidates = activeVouchers
            .Select(v =>
            {
                var eligibleSubtotal = v.ShopId == null
                    ? vm.SelectedSubTotal
                    : vm.Items.Where(i => i.ShopId == v.ShopId).Sum(i => i.LineTotal);

                var isEligible = eligibleSubtotal >= v.MinOrderValue;
                var reason = isEligible ? null : $"Đơn tối thiểu {v.MinOrderValue:N0}đ";

                if (v.DiscountType == "FreeShipping" && vm.ShippingFee <= 0)
                {
                    isEligible = false;
                    reason = "Không có phí vận chuyển";
                }

                var potentialDiscount = CalculateVoucherDiscountAmount(
                    v.DiscountType,
                    v.DiscountValue,
                    eligibleSubtotal,
                    vm.ShippingFee,
                    v.MaxDiscountValue);

                return new
                {
                    Voucher = v,
                    EligibleSubtotal = eligibleSubtotal,
                    IsEligible = isEligible,
                    Reason = reason,
                    PotentialDiscount = potentialDiscount
                };
            })
            .OrderByDescending(x => x.IsEligible)
            .ThenByDescending(x => x.PotentialDiscount)
            .ThenBy(x => x.Voucher.EndDate)
            .ThenByDescending(x => x.Voucher.CreatedAt)
            .ToList();

        foreach (var x in candidates)
        {
            var v = x.Voucher;
            vm.AvailableVouchers.Add(new CheckoutVoucherVm
            {
                Id = v.Id,
                Code = v.Code,
                DiscountType = v.DiscountType,
                DiscountValue = v.DiscountValue,
                MinOrderValue = v.MinOrderValue,
                MaxDiscountValue = v.MaxDiscountValue,
                EndDate = v.EndDate,
                IsEligible = x.IsEligible,
                IneligibleReason = x.Reason
            });
        }

        Voucher? legacySelected = null;
        if (!string.IsNullOrWhiteSpace(voucherCode))
        {
            var legacyCode = voucherCode.Trim().ToUpperInvariant();
            legacySelected = activeVouchers.FirstOrDefault(x => x.Code.ToUpper() == legacyCode);
            if (legacySelected is null)
            {
                vm.ErrorMessage = "Mã giảm giá không tồn tại, hết hạn hoặc không áp dụng cho đơn này.";
            }
        }

        var selectedShippingCode = string.IsNullOrWhiteSpace(shippingVoucherCode) ? null : shippingVoucherCode.Trim();
        var selectedDiscountCode = string.IsNullOrWhiteSpace(discountVoucherCode) ? null : discountVoucherCode.Trim();

        if (legacySelected is not null)
        {
            if (legacySelected.DiscountType == "FreeShipping" && string.IsNullOrWhiteSpace(selectedShippingCode))
            {
                selectedShippingCode = legacySelected.Code;
            }
            else if (legacySelected.DiscountType != "FreeShipping" && string.IsNullOrWhiteSpace(selectedDiscountCode))
            {
                selectedDiscountCode = legacySelected.Code;
            }
        }

        var errors = new List<string>();

        decimal shippingDiscount = 0m;
        if (!string.IsNullOrWhiteSpace(selectedShippingCode))
        {
            var code = selectedShippingCode.Trim().ToUpperInvariant();
            var v = activeVouchers.FirstOrDefault(x => x.Code.ToUpper() == code);

            if (v is null)
            {
                errors.Add("Mã miễn phí vận chuyển không tồn tại, hết hạn hoặc không áp dụng cho đơn này.");
            }
            else if (v.DiscountType != "FreeShipping")
            {
                errors.Add($"Mã {v.Code} không phải voucher miễn phí vận chuyển.");
            }
            else
            {
                var eligibleSubtotal = v.ShopId == null
                    ? vm.SelectedSubTotal
                    : vm.Items.Where(i => i.ShopId == v.ShopId).Sum(i => i.LineTotal);

                if (eligibleSubtotal < v.MinOrderValue)
                {
                    errors.Add($"Mã {v.Code} yêu cầu đơn tối thiểu {v.MinOrderValue:N0}đ.");
                }
                else if (vm.ShippingFee <= 0)
                {
                    errors.Add($"Mã {v.Code} chỉ áp dụng khi có phí vận chuyển.");
                }
                else
                {
                    shippingDiscount = CalculateVoucherDiscountAmount(
                        v.DiscountType,
                        v.DiscountValue,
                        eligibleSubtotal,
                        vm.ShippingFee,
                        v.MaxDiscountValue);

                    vm.AppliedShippingVoucherCode = v.Code;
                    vm.ShippingVoucherDiscountAmount = shippingDiscount;
                }
            }
        }

        decimal discountDiscount = 0m;
        if (!string.IsNullOrWhiteSpace(selectedDiscountCode))
        {
            var code = selectedDiscountCode.Trim().ToUpperInvariant();
            var v = activeVouchers.FirstOrDefault(x => x.Code.ToUpper() == code);

            if (v is null)
            {
                errors.Add("Mã giảm giá không tồn tại, hết hạn hoặc không áp dụng cho đơn này.");
            }
            else if (v.DiscountType == "FreeShipping")
            {
                errors.Add($"Mã {v.Code} là voucher miễn phí vận chuyển. Vui lòng chọn vào mục miễn phí vận chuyển.");
            }
            else
            {
                var eligibleSubtotal = v.ShopId == null
                    ? vm.SelectedSubTotal
                    : vm.Items.Where(i => i.ShopId == v.ShopId).Sum(i => i.LineTotal);

                if (eligibleSubtotal < v.MinOrderValue)
                {
                    errors.Add($"Mã {v.Code} yêu cầu đơn tối thiểu {v.MinOrderValue:N0}đ.");
                }
                else
                {
                    discountDiscount = CalculateVoucherDiscountAmount(
                        v.DiscountType,
                        v.DiscountValue,
                        eligibleSubtotal,
                        vm.ShippingFee,
                        v.MaxDiscountValue);

                    vm.AppliedDiscountVoucherCode = v.Code;
                    vm.DiscountVoucherDiscountAmount = discountDiscount;
                }
            }
        }

        vm.VoucherDiscountAmount = shippingDiscount + discountDiscount;
        vm.FinalAmount -= vm.VoucherDiscountAmount;
        if (vm.FinalAmount < 0)
        {
            vm.FinalAmount = 0;
        }

        if (errors.Count > 0)
        {
            vm.ErrorMessage = string.Join(" ", errors);
        }
    }

    private async Task<CheckoutPageVm> BuildCheckoutVmAsync(int accountId, int? preferredAddressId, string? paymentMethod, int? buyNowVariantId, int? buyNowQuantity, string? voucherCode, string? shippingVoucherCode, string? discountVoucherCode, CancellationToken cancellationToken)
    {
        var globalVatRate = await GetGlobalVatRateAsync(cancellationToken);
        var vm = new CheckoutPageVm
        {
            IsSignedIn = true,
            PaymentMethod = string.IsNullOrWhiteSpace(paymentMethod) ? "COD" : paymentMethod.Trim(),
            IsBuyNow = buyNowVariantId.HasValue && buyNowVariantId.Value > 0,
            BuyNowVariantId = buyNowVariantId.GetValueOrDefault(0),
            BuyNowQuantity = Math.Clamp(buyNowQuantity.GetValueOrDefault(1), 1, 99),
            GlobalVatRate = globalVatRate
        };

        // Prefer personal addresses for checkout; fallback to any if user hasn't created any personal address yet.
        var addresses = await _db.UserAddresses
            .AsNoTracking()
            .Where(a => a.AccountId == accountId && a.AddressType == AddressType.Personal)
            .OrderByDescending(a => a.IsDefault)
            .ThenByDescending(a => a.UpdatedAt)
            .ToListAsync(cancellationToken);

        if (addresses.Count == 0)
        {
            addresses = await _db.UserAddresses
                .AsNoTracking()
                .Where(a => a.AccountId == accountId)
                .OrderByDescending(a => a.IsDefault)
                .ThenByDescending(a => a.UpdatedAt)
                .ToListAsync(cancellationToken);
        }

        vm.Addresses = addresses
            .Select(a => new CheckoutAddressVm
            {
                Id = a.Id,
                FullName = a.FullName,
                PhoneNumber = a.PhoneNumber,
                FullAddress = ComposeAddress(a)
            })
            .ToList();

        CheckoutAddressVm? selected = null;
        if (preferredAddressId.HasValue && preferredAddressId.Value > 0)
        {
            selected = vm.Addresses.FirstOrDefault(a => a.Id == preferredAddressId.Value);
        }

        selected ??= vm.Addresses.FirstOrDefault();

        vm.Address = selected;
        vm.SelectedAddressId = selected?.Id ?? 0;

        if (vm.IsBuyNow && vm.BuyNowVariantId > 0)
        {
            var qty = vm.BuyNowQuantity <= 0 ? 1 : vm.BuyNowQuantity;
            qty = Math.Clamp(qty, 1, 99);

            var buyNowLocal = DateTime.Now;
            var v = await _db.ProductVariants
                .AsNoTracking()
                .Where(x => x.Id == vm.BuyNowVariantId && x.IsActive && !x.IsDeleted)
                .Include(x => x.Product)!.ThenInclude(p => p!.Images)
                .Include(x => x.Images)
                .Include(x => x.Color)
                .Include(x => x.Size)
                .FirstOrDefaultAsync(cancellationToken);

            if (v?.Product is null)
            {
                vm.ErrorMessage = "Sản phẩm không hợp lệ hoặc đã ngừng bán.";
                return vm;
            }

            if (v.StockQuantity <= 0)
            {
                vm.ErrorMessage = "Sản phẩm đã hết hàng.";
                return vm;
            }

            if (qty > v.StockQuantity)
            {
                qty = v.StockQuantity;
                vm.BuyNowQuantity = qty;
            }

            var unitPrice = GetEffectiveUnitPrice(v, buyNowLocal);
            var lineTotal = unitPrice * qty;

            var imageUrl = v.Images
                ?.OrderByDescending(x => x.IsPrimary)
                .ThenBy(x => x.SortOrder)
                .ThenBy(x => x.Id)
                .Select(x => x.ImageUrl)
                .FirstOrDefault(u => !string.IsNullOrWhiteSpace(u))
                ?? v.Product.Images
                    ?.OrderByDescending(x => x.IsPrimary)
                    .ThenBy(x => x.SortOrder)
                    .ThenBy(x => x.Id)
                    .Select(x => x.ImageUrl)
                    .FirstOrDefault(u => !string.IsNullOrWhiteSpace(u))
                ?? "assets/images/vitamin-c.png";

            vm.Items.Add(new CheckoutItemVm
            {
                CartItemId = 0,
                VariantId = v.Id,
                ShopId = v.Product.ShopId,
                ProductName = v.Product.Name,
                ProductSlug = v.Product.Slug,
                ImageUrl = imageUrl,
                Color = v.Color?.Name,
                Size = v.Size?.Name,
                UnitPrice = unitPrice,
                Quantity = qty,
                LineTotal = lineTotal,
                VatRate = globalVatRate
            });

            vm.SelectedItemCount = qty;
            vm.SelectedSubTotal = lineTotal;
            vm.VatAmount = lineTotal * globalVatRate;

            // Shipping fee for buy-now
            if (vm.Address is not null)
            {
                if (string.IsNullOrWhiteSpace(_ghnOptions.Token))
                {
                    vm.ErrorMessage ??= "Chưa cấu hình GHN Token nên chưa tính được phí vận chuyển.";
                }
                else if (_ghnOptions.ShopId <= 0)
                {
                    vm.ErrorMessage ??= "Chưa cấu hình GHN ShopId nên chưa tính được phí vận chuyển.";
                }
                else
                {
                    try
                    {
                        var shopAddr = await _db.UserAddresses
                            .AsNoTracking()
                            .Where(a => a.AccountId == v.Product.ShopId)
                            .OrderByDescending(a => a.AddressType == AddressType.Shop)
                            .ThenByDescending(a => a.IsDefault)
                            .ThenByDescending(a => a.UpdatedAt)
                            .FirstOrDefaultAsync(cancellationToken);

                        var customerAddress = await _db.UserAddresses
                            .AsNoTracking()
                            .FirstOrDefaultAsync(a => a.Id == vm.SelectedAddressId && a.AccountId == accountId, cancellationToken);

                        if (shopAddr is null)
                        {
                            vm.ErrorMessage ??= "Thiếu địa chỉ người gửi của shop nên chưa tính được phí vận chuyển.";
                        }
                        else if (customerAddress is null)
                        {
                            vm.ErrorMessage ??= "Không tìm thấy địa chỉ nhận hàng để tính phí vận chuyển.";
                        }
                        else if (shopAddr.DistrictId <= 0 || string.IsNullOrWhiteSpace(shopAddr.WardCode))
                        {
                            vm.ErrorMessage ??= "Địa chỉ shop thiếu Quận/Huyện hoặc Phường/Xã (WardCode) nên chưa tính được phí vận chuyển.";
                        }
                        else if (customerAddress.DistrictId <= 0 || string.IsNullOrWhiteSpace(customerAddress.WardCode))
                        {
                            vm.ErrorMessage ??= "Địa chỉ nhận hàng thiếu Quận/Huyện hoặc Phường/Xã (WardCode) nên chưa tính được phí vận chuyển.";
                        }
                        else
                        {
                            vm.ShippingFee = await CalculateShippingFeeForShopAsync(shopAddr, customerAddress, qty, cancellationToken);
                        }
                    }
                    catch (InvalidOperationException ex)
                    {
                        _logger.LogWarning(ex, "GHN fee not configured or invalid (GET /checkout buy-now). ShopId={ShopId}", _ghnOptions.ShopId);
                        vm.ErrorMessage ??= ex.Message;
                    }
                    catch (HttpRequestException ex)
                    {
                        _logger.LogWarning(ex, "GHN fee request failed (GET /checkout buy-now). ShopId={ShopId}", _ghnOptions.ShopId);
                        vm.ErrorMessage ??= "Không tính được phí vận chuyển GHN. Vui lòng kiểm tra ShopId/Token, địa chỉ (District/Ward) hoặc thử lại.";
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Unexpected error while calculating GHN fee (GET /checkout buy-now). ShopId={ShopId}", _ghnOptions.ShopId);
                        vm.ErrorMessage ??= "Không tính được phí vận chuyển GHN. Vui lòng thử lại.";
                    }
                }
            }

            vm.FinalAmount = vm.SelectedSubTotal + vm.ShippingFee + vm.VatAmount;

            await PopulateVouchersAsync(vm, voucherCode, shippingVoucherCode, discountVoucherCode, cancellationToken);
            return vm;
        }

        var cartId = await _db.Carts
            .AsNoTracking()
            .Where(c => c.AccountId == accountId)
            .Select(c => (int?)c.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (!cartId.HasValue)
        {
            return vm;
        }

        var selection = GetCartSelectionState();
        var nowLocal = DateTime.Now;

        var cartItems = await _db.CartItems
            .AsNoTracking()
            .Where(i => i.CartId == cartId.Value)
            .Include(i => i.Variant)!.ThenInclude(v => v!.Product)!.ThenInclude(p => p!.Images)
            .Include(i => i.Variant)!.ThenInclude(v => v!.Images)
            .Include(i => i.Variant)!.ThenInclude(v => v!.Color)
            .Include(i => i.Variant)!.ThenInclude(v => v!.Size)
            .OrderByDescending(i => i.Id)
            .ToListAsync(cancellationToken);

        foreach (var item in cartItems)
        {
            var isSelected = selection.Initialized ? !selection.DeselectedIds.Contains(item.Id) : true;
            if (!isSelected) continue;

            var v = item.Variant;
            var p = v?.Product;
            if (v is null || p is null) continue;

            var unitPrice = GetEffectiveUnitPrice(v, nowLocal);
            var lineTotal = unitPrice * item.Quantity;

            var imageUrl = v.Images
                ?.OrderByDescending(x => x.IsPrimary)
                .ThenBy(x => x.SortOrder)
                .ThenBy(x => x.Id)
                .Select(x => x.ImageUrl)
                .FirstOrDefault(u => !string.IsNullOrWhiteSpace(u))
                ?? p.Images
                    ?.OrderByDescending(x => x.IsPrimary)
                    .ThenBy(x => x.SortOrder)
                    .ThenBy(x => x.Id)
                    .Select(x => x.ImageUrl)
                    .FirstOrDefault(u => !string.IsNullOrWhiteSpace(u))
                ?? "assets/images/vitamin-c.png";

            vm.Items.Add(new CheckoutItemVm
            {
                CartItemId = item.Id,
                VariantId = item.VariantId,
                ShopId = p.ShopId,
                ProductName = p.Name,
                ProductSlug = p.Slug,
                ImageUrl = imageUrl,
                Color = v.Color?.Name,
                Size = v.Size?.Name,
                UnitPrice = unitPrice,
                Quantity = item.Quantity,
                LineTotal = lineTotal,
                VatRate = globalVatRate
            });
        }

        vm.SelectedItemCount = vm.Items.Sum(i => i.Quantity);
        vm.SelectedSubTotal = vm.Items.Sum(i => i.LineTotal);
        vm.VatAmount = vm.Items.Sum(i => i.LineTotal * i.VatRate);

        // Shipping fee: sum per shop using GHN fee API.
        if (vm.Address is not null && vm.Items.Count > 0)
        {
            if (string.IsNullOrWhiteSpace(_ghnOptions.Token))
            {
                vm.ErrorMessage = "Chưa cấu hình GHN Token nên chưa tính được phí vận chuyển.";
            }
            else if (_ghnOptions.ShopId <= 0)
            {
                vm.ErrorMessage = "Chưa cấu hình GHN ShopId nên chưa tính được phí vận chuyển.";
            }
            else
            {
                try
                {
                    var shopIds = vm.Items.Select(i => i.ShopId).Distinct().ToList();
                    // Prefer Shop addresses for sender; fallback to any address if shop hasn't marked one yet.
                    var shopAddresses = await _db.UserAddresses
                        .AsNoTracking()
                        .Where(a => shopIds.Contains(a.AccountId))
                        .OrderByDescending(a => a.IsDefault)
                        .ThenByDescending(a => a.UpdatedAt)
                        .ToListAsync(cancellationToken);

                    var byShop = shopAddresses
                        .GroupBy(a => a.AccountId)
                        .ToDictionary(
                            g => g.Key,
                            g => g.OrderByDescending(x => x.AddressType == AddressType.Shop)
                                  .ThenByDescending(x => x.IsDefault)
                                  .ThenByDescending(x => x.UpdatedAt)
                                  .First());

                    var customerAddress = await _db.UserAddresses
                        .AsNoTracking()
                        .FirstOrDefaultAsync(a => a.Id == vm.SelectedAddressId && a.AccountId == accountId, cancellationToken);

                    if (customerAddress is not null)
                    {
                        if (customerAddress.DistrictId <= 0 || string.IsNullOrWhiteSpace(customerAddress.WardCode))
                        {
                            vm.ErrorMessage = "Địa chỉ nhận hàng thiếu Quận/Huyện hoặc Phường/Xã (WardCode) nên chưa tính được phí vận chuyển.";
                        }

                        decimal shippingTotal = 0m;
                        foreach (var grp in vm.Items.GroupBy(i => i.ShopId))
                        {
                            if (!byShop.TryGetValue(grp.Key, out var shopAddr))
                            {
                                vm.ErrorMessage = "Thiếu địa chỉ người gửi của shop nên chưa tính được phí vận chuyển.";
                                continue;
                            }

                            if (shopAddr.DistrictId <= 0 || string.IsNullOrWhiteSpace(shopAddr.WardCode))
                            {
                                vm.ErrorMessage = "Địa chỉ shop thiếu Quận/Huyện hoặc Phường/Xã (WardCode) nên chưa tính được phí vận chuyển.";
                                continue;
                            }

                            var qty = grp.Sum(x => x.Quantity);
                            shippingTotal += await CalculateShippingFeeForShopAsync(shopAddr, customerAddress, qty, cancellationToken);
                        }

                        vm.ShippingFee = shippingTotal;
                    }
                }
                catch (InvalidOperationException ex)
                {
                    _logger.LogWarning(ex, "GHN fee not configured or invalid (GET /checkout). ShopId={ShopId}", _ghnOptions.ShopId);
                    vm.ErrorMessage ??= ex.Message;
                }
                catch (HttpRequestException ex)
                {
                    _logger.LogWarning(ex, "GHN fee request failed (GET /checkout). ShopId={ShopId}", _ghnOptions.ShopId);
                    vm.ErrorMessage ??= "Không tính được phí vận chuyển GHN. Vui lòng kiểm tra ShopId/Token, địa chỉ (District/Ward) hoặc thử lại.";
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unexpected error while calculating GHN fee (GET /checkout). ShopId={ShopId}", _ghnOptions.ShopId);
                    vm.ErrorMessage ??= "Không tính được phí vận chuyển GHN. Vui lòng thử lại.";
                }
            }
        }

        vm.FinalAmount = vm.SelectedSubTotal + vm.ShippingFee + vm.VatAmount;

        await PopulateVouchersAsync(vm, voucherCode, shippingVoucherCode, discountVoucherCode, cancellationToken);

        return vm;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index([FromQuery] int? addressId, [FromQuery] int? variantId, [FromQuery] int? quantity, [FromQuery] string? voucherCode, [FromQuery] string? shippingVoucherCode, [FromQuery] string? discountVoucherCode, CancellationToken cancellationToken)
    {
        var accountId = GetSignedInAccountId();
        if (!accountId.HasValue)
        {
            return Redirect("/my-account");
        }

        var vmFinal = await BuildCheckoutVmAsync(
            accountId.Value,
            preferredAddressId: addressId,
            paymentMethod: null,
            buyNowVariantId: variantId,
            buyNowQuantity: quantity,
            voucherCode: voucherCode,
            shippingVoucherCode: shippingVoucherCode,
            discountVoucherCode: discountVoucherCode,
            cancellationToken);

        if (vmFinal.Address is null)
        {
            vmFinal.ErrorMessage = "Vui lòng thêm địa chỉ giao hàng trước khi thanh toán.";
        }

        return View(vmFinal);
    }

    [HttpGet("summary")]
    public async Task<IActionResult> Summary([FromQuery] int? addressId, [FromQuery] int? variantId, [FromQuery] int? quantity, [FromQuery] string? voucherCode, [FromQuery] string? shippingVoucherCode, [FromQuery] string? discountVoucherCode, CancellationToken cancellationToken)
    {
        var accountId = GetSignedInAccountId();
        if (!accountId.HasValue)
        {
            return Unauthorized(new { message = "Unauthorized" });
        }

        var vm = await BuildCheckoutVmAsync(
            accountId.Value,
            preferredAddressId: addressId,
            paymentMethod: null,
            buyNowVariantId: variantId,
            buyNowQuantity: quantity,
            voucherCode: voucherCode,
            shippingVoucherCode: shippingVoucherCode,
            discountVoucherCode: discountVoucherCode,
            cancellationToken);

        return Ok(new
        {
            isBuyNow = vm.IsBuyNow,
            buyNowVariantId = vm.BuyNowVariantId,
            buyNowQuantity = vm.BuyNowQuantity,
            selectedItemCount = vm.SelectedItemCount,
            selectedSubTotal = vm.SelectedSubTotal,
            shippingFee = vm.ShippingFee,
            appliedShippingVoucherCode = vm.AppliedShippingVoucherCode,
            appliedDiscountVoucherCode = vm.AppliedDiscountVoucherCode,
            shippingVoucherDiscountAmount = vm.ShippingVoucherDiscountAmount,
            discountVoucherDiscountAmount = vm.DiscountVoucherDiscountAmount,
            voucherDiscountAmount = vm.VoucherDiscountAmount,
            finalAmount = vm.FinalAmount,
            errorMessage = vm.ErrorMessage,
            items = vm.Items.Select(i => new
            {
                cartItemId = i.CartItemId,
                variantId = i.VariantId,
                quantity = i.Quantity,
                unitPrice = i.UnitPrice,
                lineTotal = i.LineTotal,
                vatRate = i.VatRate
            })
        });
    }

    [HttpGet("success")]
    public async Task<IActionResult> Success([FromQuery] string? codes, CancellationToken cancellationToken)
    {
        var accountId = GetSignedInAccountId();
        if (!accountId.HasValue)
        {
            return Redirect("/my-account");
        }

        if (string.IsNullOrWhiteSpace(codes))
        {
            TempData["AccountError"] = "Không tìm thấy thông tin đơn hàng vừa đặt.";
            return Redirect("/my-orders");
        }

        var codeList = codes
            .Split(new[] { ',', ';', '|', ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !string.IsNullOrWhiteSpace(x) && x.Length <= 50)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(20)
            .ToList();

        if (codeList.Count == 0)
        {
            TempData["AccountError"] = "Không tìm thấy thông tin đơn hàng vừa đặt.";
            return Redirect("/my-orders");
        }

        var orders = await _db.Orders
            .AsNoTracking()
            .Where(o => o.CustomerId == accountId.Value && codeList.Contains(o.OrderCode))
            .Include(o => o.Items)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync(cancellationToken);

        if (orders.Count == 0)
        {
            TempData["AccountError"] = "Không tìm thấy đơn hàng. Vui lòng kiểm tra lịch sử đơn hàng.";
            return Redirect("/my-orders");
        }

        var vm = new CheckoutSuccessPageVm
        {
            IsSignedIn = true,
            Orders = orders.Select(o => new CheckoutSuccessOrderVm
            {
                Id = o.Id,
                OrderCode = o.OrderCode,
                OrderStatus = o.OrderStatus,
                PaymentMethod = o.PaymentMethod,
                PaymentStatus = o.PaymentStatus,
                SubTotal = o.SubTotal,
                ShippingFee = o.ShippingFee,
                DiscountAmount = o.DiscountAmount,
                FinalAmount = o.FinalAmount,
                RecipientName = o.RecipientName,
                RecipientPhone = o.RecipientPhone,
                ShippingAddress = o.ShippingAddress,
                CreatedAt = o.CreatedAt,
                Items = o.Items.Select(i => new CheckoutSuccessItemVm
                {
                    ProductName = i.ProductName,
                    ColorName = i.ColorName,
                    SizeName = i.SizeName,
                    UnitPrice = i.UnitPrice,
                    Quantity = i.Quantity,
                    LineTotal = i.LineTotal
                }).ToList()
            }).ToList()
        };

        return View(vm);
    }

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PlaceOrder([FromForm] PlaceOrderRequest req, CancellationToken cancellationToken)
    {
        var accountId = GetSignedInAccountId();
        if (!accountId.HasValue)
        {
            return Redirect("/my-account");
        }

        var globalVatRate = await GetGlobalVatRateAsync(cancellationToken);

        var paymentMethod = string.IsNullOrWhiteSpace(req.PaymentMethod) ? "COD" : req.PaymentMethod.Trim();
        if (paymentMethod.Length > 30) paymentMethod = paymentMethod[..30];

        var address = await _db.UserAddresses
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == req.AddressId && a.AccountId == accountId.Value, cancellationToken);

        if (address is null)
        {
            var vmMissingAddress = await BuildCheckoutVmAsync(accountId.Value, preferredAddressId: null, paymentMethod, req.VoucherCode, req.ShippingVoucherCode, req.DiscountVoucherCode, cancellationToken);
            vmMissingAddress.ErrorMessage = "Địa chỉ không hợp lệ. Vui lòng chọn lại.";
            return View("Index", vmMissingAddress);
        }

        var nowLocal = DateTime.Now;

        // Buy-now mode: do NOT touch cart.
        if (req.BuyNowVariantId.HasValue && req.BuyNowVariantId.Value > 0)
        {
            var buyQty = Math.Clamp(req.BuyNowQuantity.GetValueOrDefault(1), 1, 99);
            var v = await _db.ProductVariants
                .Where(x => x.Id == req.BuyNowVariantId.Value && x.IsActive && !x.IsDeleted)
                .Include(x => x.Product)!
                .Include(x => x.Color)
                .Include(x => x.Size)
                .FirstOrDefaultAsync(cancellationToken);

            if (v?.Product is null)
            {
                var vmBad = await BuildCheckoutVmAsync(accountId.Value, preferredAddressId: address.Id, paymentMethod, req.VoucherCode, req.ShippingVoucherCode, req.DiscountVoucherCode, cancellationToken);
                vmBad.ErrorMessage = "Sản phẩm không hợp lệ hoặc đã ngừng bán.";
                return View("Index", vmBad);
            }

            if (v.StockQuantity <= 0)
            {
                var vmOos = await BuildCheckoutVmAsync(accountId.Value, preferredAddressId: address.Id, paymentMethod, req.VoucherCode, req.ShippingVoucherCode, req.DiscountVoucherCode, cancellationToken);
                vmOos.ErrorMessage = "Sản phẩm đã hết hàng.";
                return View("Index", vmOos);
            }

            if (buyQty > v.StockQuantity)
            {
                buyQty = v.StockQuantity;
            }

            // Preload shop sender address
            var senderAddress = await _db.UserAddresses
                .AsNoTracking()
                .Where(a => a.AccountId == v.Product.ShopId)
                .OrderByDescending(a => a.AddressType == AddressType.Shop)
                .ThenByDescending(a => a.IsDefault)
                .ThenByDescending(a => a.UpdatedAt)
                .FirstOrDefaultAsync(cancellationToken);

            if (senderAddress is null)
            {
                var vmNoSender = await BuildCheckoutVmAsync(accountId.Value, preferredAddressId: address.Id, paymentMethod, req.VoucherCode, req.ShippingVoucherCode, req.DiscountVoucherCode, cancellationToken);
                vmNoSender.ErrorMessage = "Thiếu địa chỉ người gửi của shop. Không thể tính phí vận chuyển.";
                return View("Index", vmNoSender);
            }

            decimal shippingFee;
            try
            {
                shippingFee = await CalculateShippingFeeForShopAsync(senderAddress, address, buyQty, cancellationToken);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "GHN fee not configured or invalid (POST /checkout buy-now). ShopId={ShopId}", _ghnOptions.ShopId);
                var vmFeeFail = await BuildCheckoutVmAsync(accountId.Value, preferredAddressId: address.Id, paymentMethod, req.VoucherCode, req.ShippingVoucherCode, req.DiscountVoucherCode, cancellationToken);
                vmFeeFail.ErrorMessage = ex.Message;
                return View("Index", vmFeeFail);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "GHN fee request failed (POST /checkout buy-now). ShopId={ShopId}", _ghnOptions.ShopId);
                var vmFeeFail = await BuildCheckoutVmAsync(accountId.Value, preferredAddressId: address.Id, paymentMethod, req.VoucherCode, req.ShippingVoucherCode, req.DiscountVoucherCode, cancellationToken);
                vmFeeFail.ErrorMessage = "Không tính được phí vận chuyển GHN. Vui lòng kiểm tra ShopId/Token và địa chỉ (District/Ward), hoặc thử lại.";
                return View("Index", vmFeeFail);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error while calculating GHN fee (POST /checkout buy-now). ShopId={ShopId}", _ghnOptions.ShopId);
                var vmFeeFail = await BuildCheckoutVmAsync(accountId.Value, preferredAddressId: address.Id, paymentMethod, req.VoucherCode, req.ShippingVoucherCode, req.DiscountVoucherCode, cancellationToken);
                vmFeeFail.ErrorMessage = "Không tính được phí vận chuyển GHN. Vui lòng thử lại.";
                return View("Index", vmFeeFail);
            }

            var unitPrice = GetEffectiveUnitPrice(v, nowLocal);
            var subTotal = unitPrice * buyQty;

            var shipCodeRaw = string.IsNullOrWhiteSpace(req.ShippingVoucherCode) ? null : req.ShippingVoucherCode.Trim();
            var discCodeRaw = string.IsNullOrWhiteSpace(req.DiscountVoucherCode) ? null : req.DiscountVoucherCode.Trim();

            if (string.IsNullOrWhiteSpace(shipCodeRaw) && string.IsNullOrWhiteSpace(discCodeRaw) && !string.IsNullOrWhiteSpace(req.VoucherCode))
            {
                // Backward compatibility: allow old single VoucherCode.
                discCodeRaw = req.VoucherCode.Trim();
            }

            decimal discountAmount = 0m;
            int? appliedVoucherId = null;

            Voucher? shippingVoucher = null;
            if (!string.IsNullOrWhiteSpace(shipCodeRaw))
            {
                var code = shipCodeRaw.Trim().ToUpperInvariant();
                shippingVoucher = await _db.Vouchers
                    .Where(x => x.Code.ToUpper() == code && x.IsActive && x.StartDate <= nowLocal && x.EndDate >= nowLocal && (x.UsageLimit == 0 || x.UsedCount < x.UsageLimit))
                    .Where(x => x.DiscountType == "FreeShipping")
                    .Where(x => x.ShopId == null || x.ShopId == v.Product.ShopId)
                    .FirstOrDefaultAsync(cancellationToken);

                if (shippingVoucher is not null && subTotal >= shippingVoucher.MinOrderValue && shippingFee > 0)
                {
                    var shipDiscount = CalculateVoucherDiscountAmount(
                        shippingVoucher.DiscountType,
                        shippingVoucher.DiscountValue,
                        eligibleSubtotal: subTotal,
                        shippingFee,
                        shippingVoucher.MaxDiscountValue);

                    if (shipDiscount > 0)
                    {
                        discountAmount += shipDiscount;
                        shippingVoucher.UsedCount += 1;
                        appliedVoucherId ??= shippingVoucher.Id;
                    }
                }
            }

            Voucher? discountVoucher = null;
            if (!string.IsNullOrWhiteSpace(discCodeRaw))
            {
                var code = discCodeRaw.Trim().ToUpperInvariant();
                discountVoucher = await _db.Vouchers
                    .Where(x => x.Code.ToUpper() == code && x.IsActive && x.StartDate <= nowLocal && x.EndDate >= nowLocal && (x.UsageLimit == 0 || x.UsedCount < x.UsageLimit))
                    .Where(x => x.DiscountType != "FreeShipping")
                    .Where(x => x.ShopId == null || x.ShopId == v.Product.ShopId)
                    .FirstOrDefaultAsync(cancellationToken);

                if (discountVoucher is not null && subTotal >= discountVoucher.MinOrderValue)
                {
                    var discDiscount = CalculateVoucherDiscountAmount(
                        discountVoucher.DiscountType,
                        discountVoucher.DiscountValue,
                        eligibleSubtotal: subTotal,
                        shippingFee,
                        discountVoucher.MaxDiscountValue);

                    if (discDiscount > 0)
                    {
                        discountAmount += discDiscount;
                        discountVoucher.UsedCount += 1;
                        appliedVoucherId = discountVoucher.Id;
                    }
                }
            }

            var vatAmount = subTotal * globalVatRate;
            var finalAmt = subTotal + shippingFee + vatAmount - discountAmount;
            if (finalAmt < 0) finalAmt = 0;

            var isBankTransfer = paymentMethod.Equals("BankTransfer", StringComparison.OrdinalIgnoreCase);

            var order = new Order
            {
                CustomerId = accountId.Value,
                ShopId = v.Product.ShopId,
                OrderCode = GenerateOrderCode(nowLocal),
                SubTotal = subTotal,
                ShippingFee = shippingFee,
                VoucherId = appliedVoucherId,
                DiscountAmount = discountAmount,
                FinalAmount = finalAmt,
                OrderStatus = "Pending",
                PaymentMethod = paymentMethod,
                PaymentStatus = isBankTransfer ? "AwaitingPayment" : "Pending",
                RecipientName = address.FullName,
                RecipientPhone = address.PhoneNumber,
                ShippingAddress = ComposeAddress(address),
                ShippingProvinceId = address.ProvinceId,
                ShippingDistrictId = address.DistrictId,
                ShippingWardCode = address.WardCode,
                ShippingStreetAddress = address.StreetAddress,
                CreatedAt = nowLocal,
                UpdatedAt = nowLocal,
            };

            order.Items.Add(new OrderItem
            {
                VariantId = v.Id,
                ProductName = v.Product.Name,
                ColorName = v.Color?.Name,
                SizeName = v.Size?.Name,
                UnitPrice = unitPrice,
                Quantity = buyQty,
                LineTotal = subTotal
            });

            _db.Orders.Add(order);
            await _db.SaveChangesAsync(cancellationToken);

            // Nếu thanh toán bằng Ví hoặc kết hợp Ví
            if (paymentMethod.Equals("Wallet", StringComparison.OrdinalIgnoreCase) ||
                paymentMethod.Equals("WalletCOD", StringComparison.OrdinalIgnoreCase) ||
                paymentMethod.Equals("WalletVNPay", StringComparison.OrdinalIgnoreCase))
            {
                var walletBalance = await _walletService.GetBalanceAsync(accountId.Value, cancellationToken);
                
                // Trường hợp 1: Chọn Wallet thông thường hoặc số dư đủ thanh toán 100%
                if (paymentMethod.Equals("Wallet", StringComparison.OrdinalIgnoreCase) || walletBalance >= finalAmt)
                {
                    var (ok, msg) = await _walletService.PayWithWalletAsync(
                        accountId.Value, finalAmt, order.OrderCode,
                        $"Thanh toán đơn hàng {order.OrderCode}", cancellationToken);

                    if (!ok)
                    {
                        // Rollback — xóa order vừa tạo
                        _db.Orders.Remove(order);
                        await _db.SaveChangesAsync(cancellationToken);
                        var vmWalletFail = await BuildCheckoutVmAsync(accountId.Value, preferredAddressId: address.Id, paymentMethod, req.VoucherCode, req.ShippingVoucherCode, req.DiscountVoucherCode, cancellationToken);
                        vmWalletFail.ErrorMessage = msg;
                        return View("Index", vmWalletFail);
                    }

                    order.PaymentMethod = "Wallet";
                    order.PaymentStatus = "Paid";
                    await _db.SaveChangesAsync(cancellationToken);
                    var codesW = Uri.EscapeDataString(order.OrderCode);
                    return Redirect($"/checkout/success?codes={codesW}");
                }
                
                // Trường hợp 2: Thiếu số dư và chọn kết hợp COD (WalletCOD)
                if (paymentMethod.Equals("WalletCOD", StringComparison.OrdinalIgnoreCase))
                {
                    if (walletBalance > 0)
                    {
                        var (ok, msg) = await _walletService.PayWithWalletAsync(
                            accountId.Value, walletBalance, order.OrderCode,
                            $"Thanh toán một phần đơn hàng {order.OrderCode} (Còn lại COD)", cancellationToken);

                        if (!ok)
                        {
                            _db.Orders.Remove(order);
                            await _db.SaveChangesAsync(cancellationToken);
                            var vmWalletFail = await BuildCheckoutVmAsync(accountId.Value, preferredAddressId: address.Id, paymentMethod, req.VoucherCode, req.ShippingVoucherCode, req.DiscountVoucherCode, cancellationToken);
                            vmWalletFail.ErrorMessage = msg;
                            return View("Index", vmWalletFail);
                        }
                    }

                    order.PaymentMethod = "WalletCOD";
                    order.PaymentStatus = "PartiallyPaid"; // Phần ví đã thanh toán, phần COD thu sau
                    await _db.SaveChangesAsync(cancellationToken);
                    
                    var codesW = Uri.EscapeDataString(order.OrderCode);
                    return Redirect($"/checkout/success?codes={codesW}");
                }

                // Trường hợp 3: Thiếu số dư và chọn kết hợp VNPay (WalletVNPay)
                if (paymentMethod.Equals("WalletVNPay", StringComparison.OrdinalIgnoreCase))
                {
                    if (walletBalance > 0)
                    {
                        var (ok, msg) = await _walletService.PayWithWalletAsync(
                            accountId.Value, walletBalance, order.OrderCode,
                            $"Thanh toán một phần đơn hàng {order.OrderCode} (Còn lại VNPay)", cancellationToken);

                        if (!ok)
                        {
                            _db.Orders.Remove(order);
                            await _db.SaveChangesAsync(cancellationToken);
                            var vmWalletFail = await BuildCheckoutVmAsync(accountId.Value, preferredAddressId: address.Id, paymentMethod, req.VoucherCode, req.ShippingVoucherCode, req.DiscountVoucherCode, cancellationToken);
                            vmWalletFail.ErrorMessage = msg;
                            return View("Index", vmWalletFail);
                        }
                    }

                    order.PaymentMethod = "WalletVNPay";
                    order.PaymentStatus = "Pending";
                    await _db.SaveChangesAsync(cancellationToken);

                    // Chuyển hướng VNPay với số tiền còn lại = finalAmt - walletBalance
                    var remainingAmt = finalAmt - walletBalance;
                    var ipAddress = HttpContext.Connection.RemoteIpAddress?.MapToIPv4().ToString() ?? "127.0.0.1";
                    var orderInfo = $"Thanh toan phan con lai don hang {order.OrderCode}";
                    var paymentUrl = _vnPay.CreatePaymentUrl(order.OrderCode, remainingAmt, orderInfo, ipAddress);

                    HttpContext.Session.SetString("vnpay_order_codes", order.OrderCode);
                    return Redirect(paymentUrl);
                }
            }

            // If BankTransfer, redirect to VNPay
            if (paymentMethod.Equals("BankTransfer", StringComparison.OrdinalIgnoreCase))
            {
                return RedirectToVnPay(order);
            }

            var codes = Uri.EscapeDataString(order.OrderCode);
            return Redirect($"/checkout/success?codes={codes}");
        }

        // Default: checkout selected items from cart
        var cartId = await _db.Carts
            .AsNoTracking()
            .Where(c => c.AccountId == accountId.Value)
            .Select(c => (int?)c.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (!cartId.HasValue)
        {
            var vmEmpty = await BuildCheckoutVmAsync(accountId.Value, preferredAddressId: address.Id, paymentMethod, req.VoucherCode, req.ShippingVoucherCode, req.DiscountVoucherCode, cancellationToken);
            vmEmpty.ErrorMessage = "Giỏ hàng trống.";
            return View("Index", vmEmpty);
        }

        var selection = GetCartSelectionState();

        var cartItems = await _db.CartItems
            .Where(i => i.CartId == cartId.Value)
            .Include(i => i.Variant)!.ThenInclude(v => v!.Product)!
            .Include(i => i.Variant)!.ThenInclude(v => v!.Color)
            .Include(i => i.Variant)!.ThenInclude(v => v!.Size)
            .ToListAsync(cancellationToken);

        var selectedItems = cartItems
            .Where(i => !selection.Initialized || !selection.DeselectedIds.Contains(i.Id))
            .Where(i => i.Variant != null && i.Variant.Product != null)
            .ToList();

        if (selectedItems.Count == 0)
        {
            var vmNone = await BuildCheckoutVmAsync(accountId.Value, preferredAddressId: address.Id, paymentMethod, req.VoucherCode, req.ShippingVoucherCode, req.DiscountVoucherCode, cancellationToken);
            vmNone.ErrorMessage = "Vui lòng chọn ít nhất 1 sản phẩm để thanh toán.";
            return View("Index", vmNone);
        }

        // Preload shop sender addresses
        var shopIds = selectedItems.Select(i => i.Variant!.Product!.ShopId).Distinct().ToList();
        var shopAddresses = await _db.UserAddresses
            .AsNoTracking()
            .Where(a => shopIds.Contains(a.AccountId))
            .OrderByDescending(a => a.IsDefault)
            .ThenByDescending(a => a.UpdatedAt)
            .ToListAsync(cancellationToken);

        var senderByShop = shopAddresses
            .GroupBy(a => a.AccountId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(x => x.AddressType == AddressType.Shop)
                      .ThenByDescending(x => x.IsDefault)
                      .ThenByDescending(x => x.UpdatedAt)
                      .First());

        var orders = new List<Order>();
        foreach (var grp in selectedItems.GroupBy(i => i.Variant!.Product!.ShopId))
        {
            var subTotal = 0m;
            var shippingFee = 0m;

            if (!senderByShop.TryGetValue(grp.Key, out var senderAddress))
            {
                var vmNoSender = await BuildCheckoutVmAsync(accountId.Value, preferredAddressId: address.Id, paymentMethod, req.VoucherCode, req.ShippingVoucherCode, req.DiscountVoucherCode, cancellationToken);
                vmNoSender.ErrorMessage = "Thiếu địa chỉ người gửi của shop. Không thể tính phí vận chuyển.";
                return View("Index", vmNoSender);
            }

            try
            {
                var qty = grp.Sum(x => x.Quantity);
                shippingFee = await CalculateShippingFeeForShopAsync(senderAddress, address, qty, cancellationToken);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "GHN fee not configured or invalid (POST /checkout). ShopId={ShopId}", _ghnOptions.ShopId);
                var vmFeeFail = await BuildCheckoutVmAsync(accountId.Value, preferredAddressId: address.Id, paymentMethod, req.VoucherCode, req.ShippingVoucherCode, req.DiscountVoucherCode, cancellationToken);
                vmFeeFail.ErrorMessage = ex.Message;
                return View("Index", vmFeeFail);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "GHN fee request failed (POST /checkout). ShopId={ShopId}", _ghnOptions.ShopId);
                var vmFeeFail = await BuildCheckoutVmAsync(accountId.Value, preferredAddressId: address.Id, paymentMethod, req.VoucherCode, req.ShippingVoucherCode, req.DiscountVoucherCode, cancellationToken);
                vmFeeFail.ErrorMessage = "Không tính được phí vận chuyển GHN. Vui lòng kiểm tra ShopId/Token và địa chỉ (District/Ward), hoặc thử lại.";
                return View("Index", vmFeeFail);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error while calculating GHN fee (POST /checkout). ShopId={ShopId}", _ghnOptions.ShopId);
                var vmFeeFail = await BuildCheckoutVmAsync(accountId.Value, preferredAddressId: address.Id, paymentMethod, req.VoucherCode, req.ShippingVoucherCode, req.DiscountVoucherCode, cancellationToken);
                vmFeeFail.ErrorMessage = "Không tính được phí vận chuyển GHN. Vui lòng thử lại.";
                return View("Index", vmFeeFail);
            }

            var isBankTransferCart = paymentMethod.Equals("BankTransfer", StringComparison.OrdinalIgnoreCase);

            var order = new Order
            {
                CustomerId = accountId.Value,
                ShopId = grp.Key,
                OrderCode = GenerateOrderCode(nowLocal),
                SubTotal = 0m,
                ShippingFee = shippingFee,
                DiscountAmount = 0m,
                FinalAmount = 0m,
                OrderStatus = "Pending",
                PaymentMethod = paymentMethod,
                PaymentStatus = isBankTransferCart ? "AwaitingPayment" : "Pending",
                RecipientName = address.FullName,
                RecipientPhone = address.PhoneNumber,
                ShippingAddress = ComposeAddress(address),
                ShippingProvinceId = address.ProvinceId,
                ShippingDistrictId = address.DistrictId,
                ShippingWardCode = address.WardCode,
                ShippingStreetAddress = address.StreetAddress,
                CreatedAt = nowLocal,
                UpdatedAt = nowLocal,
            };

            foreach (var item in grp)
            {
                var v = item.Variant!;
                var p = v.Product!;

                var unitPrice = GetEffectiveUnitPrice(v, nowLocal);
                var lineTotal = unitPrice * item.Quantity;
                subTotal += lineTotal;
                var vatAmountItem = lineTotal * globalVatRate;

                order.Items.Add(new OrderItem
                {
                    VariantId = item.VariantId,
                    ProductName = p.Name,
                    ColorName = v.Color?.Name,
                    SizeName = v.Size?.Name,
                    UnitPrice = unitPrice,
                    Quantity = item.Quantity,
                    LineTotal = lineTotal
                });
            }

            order.SubTotal = subTotal;
            var totalVat = grp.Sum(item => GetEffectiveUnitPrice(item.Variant!, nowLocal) * item.Quantity * globalVatRate);
            order.FinalAmount = subTotal + order.ShippingFee + totalVat;
            orders.Add(order);
        }

        static void DistributeVoucherDiscount(Voucher voucher, List<Order> eligibleOrders, decimal totalDiscount, decimal globalVatRate)
        {
            if (eligibleOrders.Count == 0 || totalDiscount <= 0) return;

            var eligibleSubtotal = eligibleOrders.Sum(o => o.SubTotal);
            var eligibleShipping = eligibleOrders.Sum(o => o.ShippingFee);

            decimal remaining = totalDiscount;
            for (var i = 0; i < eligibleOrders.Count; i++)
            {
                var o = eligibleOrders[i];
                decimal part;

                if (i == eligibleOrders.Count - 1)
                {
                    part = remaining;
                }
                else
                {
                    decimal weight = voucher.DiscountType == "FreeShipping"
                        ? (eligibleShipping > 0 ? o.ShippingFee / eligibleShipping : 0)
                        : (eligibleSubtotal > 0 ? o.SubTotal / eligibleSubtotal : 0);

                    part = Math.Round(totalDiscount * weight);
                    if (part > remaining) part = remaining;
                }

                o.DiscountAmount += part;
                var vatAmount = o.SubTotal * globalVatRate;
                o.FinalAmount = o.SubTotal + o.ShippingFee + vatAmount - o.DiscountAmount;
                if (o.FinalAmount < 0) o.FinalAmount = 0;

                remaining -= part;
            }
        }

        var shopIdsInOrderAll = orders.Select(o => o.ShopId).Distinct().ToList();
        var evalTimeAll = DateTime.Now;

        var shipCodeCart = string.IsNullOrWhiteSpace(req.ShippingVoucherCode) ? null : req.ShippingVoucherCode.Trim();
        var discCodeCart = string.IsNullOrWhiteSpace(req.DiscountVoucherCode) ? null : req.DiscountVoucherCode.Trim();
        if (string.IsNullOrWhiteSpace(shipCodeCart) && string.IsNullOrWhiteSpace(discCodeCart) && !string.IsNullOrWhiteSpace(req.VoucherCode))
        {
            discCodeCart = req.VoucherCode.Trim();
        }

        if (!string.IsNullOrWhiteSpace(shipCodeCart))
        {
            var code = shipCodeCart.Trim().ToUpperInvariant();
            var voucher = await _db.Vouchers
                .Where(v => v.Code.ToUpper() == code && v.IsActive && v.StartDate <= evalTimeAll && v.EndDate >= evalTimeAll && (v.UsageLimit == 0 || v.UsedCount < v.UsageLimit))
                .Where(v => v.DiscountType == "FreeShipping")
                .Where(v => v.ShopId == null || shopIdsInOrderAll.Contains(v.ShopId.Value))
                .FirstOrDefaultAsync(cancellationToken);

            if (voucher is not null)
            {
                var eligibleOrders = voucher.ShopId == null ? orders : orders.Where(o => o.ShopId == voucher.ShopId).ToList();
                var eligibleSubtotal = eligibleOrders.Sum(o => o.SubTotal);
                var eligibleShipping = eligibleOrders.Sum(o => o.ShippingFee);

                if (eligibleSubtotal >= voucher.MinOrderValue && eligibleShipping > 0)
                {
                    var discountAmount = CalculateVoucherDiscountAmount(
                        voucher.DiscountType,
                        voucher.DiscountValue,
                        eligibleSubtotal,
                        eligibleShipping,
                        voucher.MaxDiscountValue);

                    if (discountAmount > 0)
                    {
                        voucher.UsedCount += 1;
                        DistributeVoucherDiscount(voucher, eligibleOrders, discountAmount, globalVatRate);

                        foreach (var o in eligibleOrders)
                        {
                            if (o.VoucherId is null)
                            {
                                o.VoucherId = voucher.Id;
                            }
                        }
                    }
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(discCodeCart))
        {
            var code = discCodeCart.Trim().ToUpperInvariant();
            var voucher = await _db.Vouchers
                .Where(v => v.Code.ToUpper() == code && v.IsActive && v.StartDate <= evalTimeAll && v.EndDate >= evalTimeAll && (v.UsageLimit == 0 || v.UsedCount < v.UsageLimit))
                .Where(v => v.DiscountType != "FreeShipping")
                .Where(v => v.ShopId == null || shopIdsInOrderAll.Contains(v.ShopId.Value))
                .FirstOrDefaultAsync(cancellationToken);

            if (voucher is not null)
            {
                var eligibleOrders = voucher.ShopId == null ? orders : orders.Where(o => o.ShopId == voucher.ShopId).ToList();
                var eligibleSubtotal = eligibleOrders.Sum(o => o.SubTotal);
                var eligibleShipping = eligibleOrders.Sum(o => o.ShippingFee);

                if (eligibleSubtotal >= voucher.MinOrderValue)
                {
                    var discountAmount = CalculateVoucherDiscountAmount(
                        voucher.DiscountType,
                        voucher.DiscountValue,
                        eligibleSubtotal,
                        eligibleShipping,
                        voucher.MaxDiscountValue);

                    if (discountAmount > 0)
                    {
                        voucher.UsedCount += 1;
                        DistributeVoucherDiscount(voucher, eligibleOrders, discountAmount, globalVatRate);

                        foreach (var o in eligibleOrders)
                        {
                            o.VoucherId = voucher.Id;
                        }
                    }
                }
            }
        }

        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);
        _db.Orders.AddRange(orders);
        _db.CartItems.RemoveRange(selectedItems);
        await _db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        // Best-effort cleanup selection state.
        if (selection.Initialized)
        {
            foreach (var removed in selectedItems)
            {
                selection.DeselectedIds.Remove(removed.Id);
            }

            HttpContext.Session.SetJson(SessionCartSelectionKey, selection);
        }

        var orderCodes = orders
            .Select(o => o.OrderCode)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // If Wallet payment or combination payments
        if ((paymentMethod.Equals("Wallet", StringComparison.OrdinalIgnoreCase) ||
             paymentMethod.Equals("WalletCOD", StringComparison.OrdinalIgnoreCase) ||
             paymentMethod.Equals("WalletVNPay", StringComparison.OrdinalIgnoreCase)) && orders.Count > 0)
        {
            var totalWalletAmt = orders.Sum(o => o.FinalAmount);
            var combinedCodes = string.Join(", ", orderCodes);
            var walletBalance = await _walletService.GetBalanceAsync(accountId.Value, cancellationToken);

            // Trường hợp 1: Đủ số dư hoặc chọn Wallet 100%
            if (paymentMethod.Equals("Wallet", StringComparison.OrdinalIgnoreCase) || walletBalance >= totalWalletAmt)
            {
                var (walletOk, walletMsg) = await _walletService.PayWithWalletAsync(
                    accountId.Value, totalWalletAmt, orders[0].OrderCode,
                    $"Thanh toan don hang {combinedCodes}", cancellationToken);

                if (!walletOk)
                {
                    _db.Orders.RemoveRange(orders);
                    await _db.SaveChangesAsync(cancellationToken);
                    var vmWalletFail = await BuildCheckoutVmAsync(accountId.Value, preferredAddressId: address.Id, paymentMethod,
                        req.VoucherCode, req.ShippingVoucherCode, req.DiscountVoucherCode, cancellationToken);
                    vmWalletFail.ErrorMessage = walletMsg;
                    return View("Index", vmWalletFail);
                }

                foreach (var walletOrder in orders)
                {
                    walletOrder.PaymentMethod = "Wallet";
                    walletOrder.PaymentStatus = "Paid";
                }
                await _db.SaveChangesAsync(cancellationToken);

                var walletCodesQuery = Uri.EscapeDataString(string.Join(",", orderCodes));
                return Redirect($"/checkout/success?codes={walletCodesQuery}");
            }

            // Trường hợp 2: Wallet + COD
            if (paymentMethod.Equals("WalletCOD", StringComparison.OrdinalIgnoreCase))
            {
                if (walletBalance > 0)
                {
                    var (walletOk, walletMsg) = await _walletService.PayWithWalletAsync(
                        accountId.Value, walletBalance, orders[0].OrderCode,
                        $"Thanh toan mot phan don hang {combinedCodes} (Con lai COD)", cancellationToken);

                    if (!walletOk)
                    {
                        _db.Orders.RemoveRange(orders);
                        await _db.SaveChangesAsync(cancellationToken);
                        var vmWalletFail = await BuildCheckoutVmAsync(accountId.Value, preferredAddressId: address.Id, paymentMethod,
                            req.VoucherCode, req.ShippingVoucherCode, req.DiscountVoucherCode, cancellationToken);
                        vmWalletFail.ErrorMessage = walletMsg;
                        return View("Index", vmWalletFail);
                    }
                }

                foreach (var walletOrder in orders)
                {
                    walletOrder.PaymentMethod = "WalletCOD";
                    walletOrder.PaymentStatus = "PartiallyPaid";
                }
                await _db.SaveChangesAsync(cancellationToken);

                var walletCodesQuery = Uri.EscapeDataString(string.Join(",", orderCodes));
                return Redirect($"/checkout/success?codes={walletCodesQuery}");
            }

            // Trường hợp 3: Wallet + VNPay
            if (paymentMethod.Equals("WalletVNPay", StringComparison.OrdinalIgnoreCase))
            {
                if (walletBalance > 0)
                {
                    var (walletOk, walletMsg) = await _walletService.PayWithWalletAsync(
                        accountId.Value, walletBalance, orders[0].OrderCode,
                        $"Thanh toan mot phan don hang {combinedCodes} (Con lai VNPay)", cancellationToken);

                    if (!walletOk)
                    {
                        _db.Orders.RemoveRange(orders);
                        await _db.SaveChangesAsync(cancellationToken);
                        var vmWalletFail = await BuildCheckoutVmAsync(accountId.Value, preferredAddressId: address.Id, paymentMethod,
                            req.VoucherCode, req.ShippingVoucherCode, req.DiscountVoucherCode, cancellationToken);
                        vmWalletFail.ErrorMessage = walletMsg;
                        return View("Index", vmWalletFail);
                    }
                }

                foreach (var walletOrder in orders)
                {
                    walletOrder.PaymentMethod = "WalletVNPay";
                    walletOrder.PaymentStatus = "Pending";
                }
                await _db.SaveChangesAsync(cancellationToken);

                var remainingAmt = totalWalletAmt - walletBalance;
                var txnRef = orders[0].OrderCode;
                var orderInfo = $"Thanh toan phan con lai don hang {combinedCodes}";
                var ipAddress = HttpContext.Connection.RemoteIpAddress?.MapToIPv4().ToString() ?? "127.0.0.1";
                var paymentUrl = _vnPay.CreatePaymentUrl(txnRef, remainingAmt, orderInfo, ipAddress);

                HttpContext.Session.SetString("vnpay_order_codes", string.Join(",", orderCodes));
                return Redirect(paymentUrl);
            }
        }

        // If BankTransfer, redirect to VNPay (use first order as txn ref, total of all orders)
        if (paymentMethod.Equals("BankTransfer", StringComparison.OrdinalIgnoreCase) && orders.Count > 0)
        {
            var totalAmount = orders.Sum(o => o.FinalAmount);
            var txnRef = orders[0].OrderCode;
            var orderInfo = $"Thanh toan don hang {string.Join(", ", orderCodes)}";

            var ipAddress = HttpContext.Connection.RemoteIpAddress?.MapToIPv4().ToString() ?? "127.0.0.1";
            var paymentUrl = _vnPay.CreatePaymentUrl(txnRef, totalAmount, orderInfo, ipAddress);

            HttpContext.Session.SetString("vnpay_order_codes", string.Join(",", orderCodes));

            return Redirect(paymentUrl);
        }

        var codesQuery = Uri.EscapeDataString(string.Join(",", orderCodes));
        return Redirect($"/checkout/success?codes={codesQuery}");
    }

    /// <summary>
    /// Builds a VNPay redirect for a single order.
    /// </summary>
    private IActionResult RedirectToVnPay(Order order)
    {
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.MapToIPv4().ToString() ?? "127.0.0.1";
        var orderInfo = $"Thanh toan don hang {order.OrderCode}";
        var paymentUrl = _vnPay.CreatePaymentUrl(order.OrderCode, order.FinalAmount, orderInfo, ipAddress);

        HttpContext.Session.SetString("vnpay_order_codes", order.OrderCode);

        return Redirect(paymentUrl);
    }

    /// <summary>
    /// VNPay Return URL – the user is redirected here after completing payment on VNPay.
    /// </summary>
    [HttpGet("vnpay-return")]
    public async Task<IActionResult> VnPayReturn(CancellationToken cancellationToken)
    {
        var accountId = GetSignedInAccountId();
        if (!accountId.HasValue)
        {
            return Redirect("/my-account");
        }

        var isValid = _vnPay.ValidateSignature(Request.Query);
        var returnData = _vnPay.ParseReturnData(Request.Query);

        // Retrieve the order codes from session
        var orderCodesRaw = HttpContext.Session.GetString("vnpay_order_codes") ?? returnData.TxnRef;
        var orderCodes = orderCodesRaw
            .Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (!isValid)
        {
            _logger.LogWarning("VNPay return: invalid signature. TxnRef={TxnRef}", returnData.TxnRef);
            TempData["PaymentError"] = "Chữ ký không hợp lệ. Giao dịch bị nghi ngờ giả mạo.";

            var invalidFirstOrder = await _db.Orders
                .AsNoTracking()
                .Where(o => o.CustomerId == accountId.Value && orderCodes.Contains(o.OrderCode))
                .OrderBy(o => o.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);

            var invalidDeadline = invalidFirstOrder != null
                ? invalidFirstOrder.CreatedAt.Add(VnPayExpiredOrderCleaner.PaymentTimeout)
                : DateTime.Now.AddMinutes(15);

            return View("PaymentFailed", new VnPayFailedVm
            {
                ResponseCode = "97",
                Message = "Chữ ký không hợp lệ. Vui lòng thử thanh toán lại.",
                OrderCodes = orderCodes,
                PaymentDeadlineUtc = invalidDeadline
            });
        }

        if (returnData.IsSuccess)
        {
            // Update payment status in database
            var orders = await _db.Orders
                .Where(o => o.CustomerId == accountId.Value && orderCodes.Contains(o.OrderCode))
                .ToListAsync(cancellationToken);

            foreach (var order in orders)
            {
                order.PaymentStatus = "Paid";
                order.UpdatedAt = DateTime.UtcNow;
            }

            await _db.SaveChangesAsync(cancellationToken);

            // Clear session
            HttpContext.Session.Remove("vnpay_order_codes");

            var codesQuery = Uri.EscapeDataString(string.Join(",", orderCodes));
            return Redirect($"/checkout/success?codes={codesQuery}");
        }
        else
        {
            _logger.LogWarning("VNPay return: payment failed. TxnRef={TxnRef}, ResponseCode={Code}", returnData.TxnRef, returnData.ResponseCode);

            var errorMessage = returnData.ResponseCode switch
            {
                "07" => "Trừ tiền thành công nhưng giao dịch bị nghi ngờ gian lận.",
                "09" => "Thẻ/Tài khoản chưa đăng ký dịch vụ InternetBanking.",
                "10" => "Xác thực thông tin thẻ/tài khoản không đúng quá 3 lần.",
                "11" => "Đã hết hạn chờ thanh toán. Vui lòng thực hiện lại.",
                "12" => "Thẻ/Tài khoản bị khóa.",
                "13" => "Nhập sai mật khẩu xác thực (OTP). Vui lòng thực hiện lại.",
                "24" => "Bạn đã hủy giao dịch thanh toán.",
                "51" => "Tài khoản không đủ số dư để thực hiện giao dịch.",
                "65" => "Tài khoản đã vượt quá hạn mức giao dịch trong ngày.",
                "75" => "Ngân hàng thanh toán đang bảo trì.",
                "79" => "Nhập sai mật khẩu thanh toán quá số lần quy định.",
                _ => "Giao dịch không thành công. Vui lòng thử lại."
            };

            // Calculate remaining time for retry
            var firstOrder = await _db.Orders
                .AsNoTracking()
                .Where(o => o.CustomerId == accountId.Value && orderCodes.Contains(o.OrderCode))
                .OrderBy(o => o.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);

            var deadline = firstOrder != null
                ? firstOrder.CreatedAt.Add(VnPayExpiredOrderCleaner.PaymentTimeout)
                : DateTime.Now.AddMinutes(15);

            return View("PaymentFailed", new VnPayFailedVm
            {
                ResponseCode = returnData.ResponseCode,
                Message = errorMessage,
                OrderCodes = orderCodes,
                PaymentDeadlineUtc = deadline
            });
        }
    }

    /// <summary>
    /// Retry VNPay payment for orders that are still awaiting payment.
    /// </summary>
    [HttpGet("vnpay-retry")]
    public async Task<IActionResult> VnPayRetry([FromQuery] string? codes, CancellationToken cancellationToken)
    {
        var accountId = GetSignedInAccountId();
        if (!accountId.HasValue)
        {
            return Redirect("/my-account");
        }

        if (string.IsNullOrWhiteSpace(codes))
        {
            TempData["AccountError"] = "Không tìm thấy đơn hàng để thanh toán lại.";
            return Redirect("/my-orders");
        }

        var orderCodes = codes
            .Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var orders = await _db.Orders
            .Where(o => o.CustomerId == accountId.Value
                && orderCodes.Contains(o.OrderCode)
                && o.PaymentMethod == "BankTransfer"
                && o.PaymentStatus == "AwaitingPayment")
            .ToListAsync(cancellationToken);

        if (orders.Count == 0)
        {
            TempData["AccountError"] = "Đơn hàng không tồn tại, đã thanh toán hoặc đã hết hạn.";
            return Redirect("/my-orders");
        }

        // Check if still within the payment timeout
        var oldestOrder = orders.OrderBy(o => o.CreatedAt).First();
        var deadline = oldestOrder.CreatedAt.Add(VnPayExpiredOrderCleaner.PaymentTimeout);

        if (DateTime.Now > deadline)
        {
            // Already expired, cancel them
            foreach (var o in orders)
            {
                o.PaymentStatus = "Expired";
                o.OrderStatus = "Cancelled";
                o.UpdatedAt = DateTime.UtcNow;
            }
            await _db.SaveChangesAsync(cancellationToken);

            TempData["AccountError"] = "Đơn hàng đã hết hạn thanh toán và bị hủy tự động. Vui lòng đặt lại.";
            return Redirect("/my-orders");
        }

        // Build a new VNPay payment URL
        var totalAmount = orders.Sum(o => o.FinalAmount);
        var txnRef = orders[0].OrderCode;
        var orderInfo = $"Thanh toan don hang {string.Join(", ", orders.Select(o => o.OrderCode))}";

        var ipAddress = HttpContext.Connection.RemoteIpAddress?.MapToIPv4().ToString() ?? "127.0.0.1";
        var paymentUrl = _vnPay.CreatePaymentUrl(txnRef, totalAmount, orderInfo, ipAddress);

        HttpContext.Session.SetString("vnpay_order_codes", string.Join(",", orders.Select(o => o.OrderCode)));

        return Redirect(paymentUrl);
    }

    /// <summary>
    /// VNPay IPN URL – server-to-server callback to confirm payment.
    /// </summary>
    [HttpGet("vnpay-ipn")]
    public async Task<IActionResult> VnPayIpn(CancellationToken cancellationToken)
    {
        try
        {
            var isValid = _vnPay.ValidateSignature(Request.Query);
            if (!isValid)
            {
                return Json(new { RspCode = "97", Message = "Invalid signature" });
            }

            var returnData = _vnPay.ParseReturnData(Request.Query);
            var txnRef = returnData.TxnRef;

            var order = await _db.Orders
                .FirstOrDefaultAsync(o => o.OrderCode == txnRef, cancellationToken);

            if (order is null)
            {
                return Json(new { RspCode = "01", Message = "Order not found" });
            }

            // Check amount (VNPay returns amount * 100, we already divided in ParseReturnData)
            if (order.FinalAmount != returnData.Amount)
            {
                // For multi-order scenarios, check session-stored total
                _logger.LogWarning("VNPay IPN: amount mismatch. OrderAmount={OrderAmount}, VnPayAmount={VnPayAmount}", order.FinalAmount, returnData.Amount);
                return Json(new { RspCode = "04", Message = "Invalid amount" });
            }

            if (order.PaymentStatus == "Paid")
            {
                return Json(new { RspCode = "02", Message = "Order already confirmed" });
            }

            if (returnData.IsSuccess)
            {
                order.PaymentStatus = "Paid";
                order.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(cancellationToken);
            }
            else
            {
                order.PaymentStatus = "Failed";
                order.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(cancellationToken);
            }

            return Json(new { RspCode = "00", Message = "Confirm Success" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "VNPay IPN error");
            return Json(new { RspCode = "99", Message = "Unknown error" });
        }
    }
}

public sealed class VnPayFailedVm
{
    public string ResponseCode { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public List<string> OrderCodes { get; set; } = new();
    public DateTime PaymentDeadlineUtc { get; set; } = DateTime.UtcNow.AddMinutes(15);
}
