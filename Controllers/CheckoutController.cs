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
    private readonly WalletService _wallet;
    private readonly VnPayService _vnPay;

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
        WalletService wallet,
        VnPayService vnPay)
    {
        _db = db;
        _ghn = ghn;
        _ghnOptions = ghnOptions.Value;
        _logger = logger;
        _wallet = wallet;
        _vnPay = vnPay;
    }

    private int? GetSignedInAccountId()
    {
        if (!(User?.Identity?.IsAuthenticated ?? false)) return null;

        var idRaw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(idRaw, out var id) ? id : null;
    }

    private CartSelectionState GetCartSelectionState()
        => HttpContext.Session.GetJson<CartSelectionState>(SessionCartSelectionKey) ?? new CartSelectionState();

    private static decimal GetEffectiveUnitPrice(ProductVariant variant, DateTime nowUtc)
    {
        var inSaleWindow = IsVariantInFlashSale(variant, nowUtc);
        return inSaleWindow ? variant.SalePrice!.Value : variant.Price;
    }

    private static bool IsVariantInFlashSale(ProductVariant variant, DateTime nowUtc)
    {
        if (!variant.SalePrice.HasValue || variant.SalePrice.Value <= 0 || variant.SalePrice.Value >= variant.Price)
            return false;

        bool isMultiTier = variant.OptionValue1Id.HasValue || variant.OptionValue2Id.HasValue;
        if (isMultiTier && (!variant.SaleStartDate.HasValue || !variant.SaleEndDate.HasValue))
            return false;

        var nowLocal = DateTime.Now;
        return (!variant.SaleStartDate.HasValue || variant.SaleStartDate <= nowLocal)
            && (!variant.SaleEndDate.HasValue || variant.SaleEndDate >= nowLocal);
    }

    private static HashSet<int> ParseCsvIdSet(string? csv)
    {
        var set = new HashSet<int>();
        if (string.IsNullOrWhiteSpace(csv)) return set;
        foreach (var part in csv.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (int.TryParse(part, out var id) && id > 0)
            {
                set.Add(id);
            }
        }
        return set;
    }

    private static int ResolveRootCategoryId(int catId, IReadOnlyDictionary<int, Category> catMap)
    {
        var cur = catId;
        var guard = 0;
        while (guard++ < 10 && catMap.TryGetValue(cur, out var node) && node.ParentId.HasValue && catMap.ContainsKey(node.ParentId.Value))
        {
            cur = node.ParentId.Value;
        }
        return cur;
    }

    private static (decimal EligibleSubtotal, bool IsEligible, string? IneligibleReason, string ScopeLabel, string SponsorLabel) EvaluateVoucherForItems(
        Voucher v,
        IReadOnlyList<CheckoutItemVm> items,
        decimal totalShippingFee,
        int customerOrderCountAll,
        IReadOnlyDictionary<int, int> customerOrdersByShop,
        IReadOnlyDictionary<int, int> customerVoucherUsage,
        IReadOnlyDictionary<int, Category> catMap,
        IReadOnlyDictionary<int, string> shopNames)
    {
        bool isShopVoucher = v.ShopId.HasValue;
        string sponsorLabel = isShopVoucher
            ? (shopNames.TryGetValue(v.ShopId!.Value, out var sName) ? $"Shop tài trợ: {sName}" : $"Voucher Gian hàng #{v.ShopId.Value}")
            : "Sàn Sellzy tài trợ (Quỹ chiết khấu sàn)";

        var scopeType = string.IsNullOrWhiteSpace(v.ScopeType) ? "All" : v.ScopeType.Trim();
        var targetCatIds = ParseCsvIdSet(v.TargetCategoryIds);
        var targetProdIds = ParseCsvIdSet(v.TargetProductIds);

        string scopeLabel = scopeType switch
        {
            "MallOnly" => "Chỉ gian hàng Sellzy Mall",
            "ByCategory" => isShopVoucher ? "Danh mục chọn lọc của Shop" : "Ngành hàng chỉ định toàn sàn",
            "SpecificProducts" => $"Sản phẩm chỉ định ({targetProdIds.Count} SP)",
            _ => isShopVoucher ? "Toàn bộ sản phẩm của Shop" : "Toàn sàn Sellzy"
        };

        if (v.ExcludeFlashSaleItems)
        {
            scopeLabel += " • Không kèm Flash Sale";
        }

        // 1. Check per-user usage limit
        if (v.MaxUsagePerUser > 0 && customerVoucherUsage.TryGetValue(v.Id, out var usedByCustomer) && usedByCustomer >= v.MaxUsagePerUser)
        {
            return (0m, false, $"Bạn đã dùng hết lượt ({v.MaxUsagePerUser} lần/tài khoản)", scopeLabel, sponsorLabel);
        }

        // 2. Check target audience (NewBuyerOnly)
        var audience = string.IsNullOrWhiteSpace(v.TargetAudience) ? "Public" : v.TargetAudience.Trim();
        if (string.Equals(audience, "NewBuyerOnly", StringComparison.OrdinalIgnoreCase))
        {
            if (isShopVoucher)
            {
                if (customerOrdersByShop.TryGetValue(v.ShopId!.Value, out var priorShopOrders) && priorShopOrders > 0)
                {
                    return (0m, false, "Chỉ dành cho khách hàng mới lần đầu mua tại Shop", scopeLabel, sponsorLabel);
                }
            }
            else if (customerOrderCountAll > 0)
            {
                return (0m, false, "Chỉ dành cho khách hàng mới lần đầu mua trên Sellzy", scopeLabel, sponsorLabel);
            }
        }

        // 3. Filter items by ShopId
        var shopFiltered = isShopVoucher
            ? items.Where(i => i.ShopId == v.ShopId!.Value).ToList()
            : items.ToList();

        if (shopFiltered.Count == 0)
        {
            return (0m, false, "Giỏ hàng không có sản phẩm của Shop phát hành mã này", scopeLabel, sponsorLabel);
        }

        // 4. Filter by ExcludeFlashSaleItems
        var nonFlashFiltered = v.ExcludeFlashSaleItems
            ? shopFiltered.Where(i => !i.IsFlashSaleItem).ToList()
            : shopFiltered;

        if (nonFlashFiltered.Count == 0)
        {
            return (0m, false, "Mã này không áp dụng chồng lên sản phẩm đang chạy Flash Sale", scopeLabel, sponsorLabel);
        }

        // 5. Filter by ScopeType
        List<CheckoutItemVm> scopeFiltered;
        if (string.Equals(scopeType, "MallOnly", StringComparison.OrdinalIgnoreCase))
        {
            scopeFiltered = nonFlashFiltered.Where(i => i.IsMallShop).ToList();
            if (scopeFiltered.Count == 0)
            {
                return (0m, false, "Mã chỉ áp dụng cho sản phẩm thuộc gian hàng chính hãng Sellzy Mall", scopeLabel, sponsorLabel);
            }
        }
        else if (string.Equals(scopeType, "ByCategory", StringComparison.OrdinalIgnoreCase))
        {
            scopeFiltered = nonFlashFiltered
                .Where(i => targetCatIds.Contains(i.CategoryId) || targetCatIds.Contains(i.RootCategoryId))
                .ToList();
            if (scopeFiltered.Count == 0)
            {
                return (0m, false, "Sản phẩm trong đơn không thuộc ngành hàng áp dụng của mã này", scopeLabel, sponsorLabel);
            }
        }
        else if (string.Equals(scopeType, "SpecificProducts", StringComparison.OrdinalIgnoreCase))
        {
            scopeFiltered = nonFlashFiltered
                .Where(i => targetProdIds.Contains(i.ProductId))
                .ToList();
            if (scopeFiltered.Count == 0)
            {
                return (0m, false, "Mã chỉ áp dụng cho một số sản phẩm chỉ định của Shop", scopeLabel, sponsorLabel);
            }
        }
        else
        {
            scopeFiltered = nonFlashFiltered;
        }

        decimal eligibleSubtotal = scopeFiltered.Sum(i => i.LineTotal);
        if (eligibleSubtotal < v.MinOrderValue)
        {
            return (eligibleSubtotal, false, $"Tổng sản phẩm hợp lệ tối thiểu {v.MinOrderValue:N0}đ (hiện tại {eligibleSubtotal:N0}đ)", scopeLabel, sponsorLabel);
        }

        if (v.DiscountType == "FreeShipping" && totalShippingFee <= 0)
        {
            return (eligibleSubtotal, false, "Đơn hàng không phát sinh phí vận chuyển", scopeLabel, sponsorLabel);
        }

        return (eligibleSubtotal, true, null, scopeLabel, sponsorLabel);
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
            ServiceTypeId = weight >= 20000 ? 5 : serviceType,
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

    private async Task<decimal> CalculateShippingFeeForVariantsAsync(
        UserAddress shopAddress,
        UserAddress customerAddress,
        IEnumerable<(ProductVariant Variant, int Quantity)> items,
        CancellationToken cancellationToken)
    {
        var list = items.ToList();
        var defaultWeight = _ghnOptions.DefaultWeightGram <= 0 ? 300 : _ghnOptions.DefaultWeightGram;
        var defaultLen = _ghnOptions.DefaultLengthCm <= 0 ? 15 : _ghnOptions.DefaultLengthCm;
        var defaultWid = _ghnOptions.DefaultWidthCm <= 0 ? 15 : _ghnOptions.DefaultWidthCm;
        var defaultHei = _ghnOptions.DefaultHeightCm <= 0 ? 10 : _ghnOptions.DefaultHeightCm;

        var totalWeight = list.Sum(x =>
            (x.Variant.WeightGram > 0 ? x.Variant.WeightGram : defaultWeight) * Math.Max(1, x.Quantity));
        totalWeight = Math.Clamp(totalWeight, 1, 1_600_000);

        var length = list.Any() ? Math.Clamp(list.Max(x => x.Variant.LengthCm > 0 ? x.Variant.LengthCm : defaultLen), 1, 200) : defaultLen;
        var width = list.Any() ? Math.Clamp(list.Max(x => x.Variant.WidthCm > 0 ? x.Variant.WidthCm : defaultWid), 1, 200) : defaultWid;
        var height = list.Any() ? Math.Clamp(list.Sum(x => (x.Variant.HeightCm > 0 ? x.Variant.HeightCm : defaultHei) * Math.Max(1, x.Quantity)), 1, 200) : defaultHei;

        var serviceType = totalWeight >= 20000 ? 5 : (_ghnOptions.ServiceTypeId <= 0 ? 2 : _ghnOptions.ServiceTypeId);

        var req = new GhnCalculateFeeRequest
        {
            ServiceTypeId = serviceType,
            FromDistrictId = shopAddress.DistrictId,
            FromWardCode = shopAddress.WardCode,
            ToDistrictId = customerAddress.DistrictId,
            ToWardCode = customerAddress.WardCode,
            Weight = totalWeight,
            Length = length,
            Width = width,
            Height = height,
            InsuranceValue = 0,
            Items = serviceType == 5
                ? list.Select(x => new GhnCreateOrderItem
                {
                    Name = x.Variant.Product?.Name ?? "San pham",
                    Quantity = Math.Max(1, x.Quantity),
                    Price = (int)Math.Max(0, Math.Round(x.Variant.Price, MidpointRounding.AwayFromZero)),
                    Weight = x.Variant.WeightGram > 0 ? x.Variant.WeightGram : defaultWeight,
                    Length = x.Variant.LengthCm > 0 ? x.Variant.LengthCm : defaultLen,
                    Width = x.Variant.WidthCm > 0 ? x.Variant.WidthCm : defaultWid,
                    Height = x.Variant.HeightCm > 0 ? x.Variant.HeightCm : defaultHei
                }).ToList()
                : null
        };

        var fee = await _ghn.CalculateFeeAsync(req, shopId: null, cancellationToken);
        return fee.Total;
    }

    private async Task<(Dictionary<int, Category> CategoryMap, decimal DefaultCommissionPercent)> LoadCommissionContextAsync(CancellationToken cancellationToken)
    {
        var categoryMap = await _db.Categories.AsNoTracking().ToDictionaryAsync(c => c.Id, cancellationToken);
        var defaultCommSetting = await _db.SystemSettings.AsNoTracking()
            .Where(s => s.Id == "Fee_Commission_Default")
            .Select(s => s.Value)
            .FirstOrDefaultAsync(cancellationToken);

        decimal defaultRate = 8m;
        if (!string.IsNullOrWhiteSpace(defaultCommSetting) &&
            decimal.TryParse(defaultCommSetting, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
        {
            defaultRate = parsed;
        }
        return (categoryMap, defaultRate);
    }

    private async Task CompleteAndDeliverDigitalOrderAsync(Order order, CancellationToken cancellationToken)
    {
        if (!order.IsDigitalOrder || order.PaymentStatus != "Paid") return;

        order.OrderStatus = "Completed";
        order.UpdatedAt = DateTime.UtcNow;

        var variantIds = order.Items.Select(i => i.VariantId).Distinct().ToList();
        var digitalProducts = await _db.ProductVariants
            .AsNoTracking()
            .Include(v => v.Product)
            .Where(v => variantIds.Contains(v.Id) && v.Product != null)
            .Select(v => new { v.Id, v.Product!.Name, v.Product.DigitalContent })
            .ToListAsync(cancellationToken);

        var codesList = new List<string>();
        foreach (var dp in digitalProducts)
        {
            var code = !string.IsNullOrWhiteSpace(dp.DigitalContent)
                ? dp.DigitalContent.Trim()
                : $"EVOUCHER-{order.OrderCode}-{dp.Id}";
            codesList.Add($"[{dp.Name}]: {code}");
        }

        var combinedCodes = codesList.Count > 0
            ? string.Join(" | ", codesList)
            : $"EVOUCHER-{order.OrderCode}";

        _db.OrderTrackingLogs.Add(new OrderTrackingLog
        {
            OrderId = order.Id,
            Status = "Completed",
            Note = $"[BÀN GIAO SẢN PHẨM SỐ / E-VOUCHER TỰ ĐỘNG] Mã kích hoạt / Link sản phẩm của bạn: {combinedCodes}",
            CreatedAt = DateTime.UtcNow
        });

        _db.SystemNotifications.Add(new SystemNotification
        {
            Title = $"Mã E-Voucher / Sản phẩm số đơn #{order.OrderCode}",
            Content = $"Đơn hàng sản phẩm số #{order.OrderCode} đã thanh toán và hoàn tất tự động. Nội dung bàn giao: {combinedCodes}",
            TargetType = "Account",
            TargetAccountId = order.CustomerId,
            SentByAdminId = order.ShopId,
            CreatedAt = DateTime.UtcNow
        });
    }

    private Task<CheckoutPageVm> BuildCheckoutVmAsync(int accountId, int? preferredAddressId, string? paymentMethod, string? voucherCode, CancellationToken cancellationToken)
        => BuildCheckoutVmAsync(accountId, preferredAddressId, paymentMethod, buyNowVariantId: null, buyNowQuantity: null, voucherCode, null, null, cancellationToken);

    private Task<CheckoutPageVm> BuildCheckoutVmAsync(int accountId, int? preferredAddressId, string? paymentMethod, string? voucherCode, string? shippingVoucherCode, string? discountVoucherCode, CancellationToken cancellationToken)
        => BuildCheckoutVmAsync(accountId, preferredAddressId, paymentMethod, buyNowVariantId: null, buyNowQuantity: null, voucherCode, shippingVoucherCode, discountVoucherCode, cancellationToken);

    private async Task<CheckoutPageVm> BuildCheckoutVmAsync(int accountId, int? preferredAddressId, string? paymentMethod, int? buyNowVariantId, int? buyNowQuantity, string? voucherCode, string? shippingVoucherCode, string? discountVoucherCode, CancellationToken cancellationToken)
    {
        var vm = new CheckoutPageVm
        {
            IsSignedIn = true,
            PaymentMethod = string.IsNullOrWhiteSpace(paymentMethod) ? "COD" : paymentMethod.Trim(),
            IsBuyNow = buyNowVariantId.HasValue && buyNowVariantId.Value > 0,
            BuyNowVariantId = buyNowVariantId.GetValueOrDefault(0),
            BuyNowQuantity = Math.Clamp(buyNowQuantity.GetValueOrDefault(1), 1, 99)
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

        var allCategoriesMap = await _db.Categories.AsNoTracking().ToDictionaryAsync(c => c.Id, cancellationToken);
        var mallShopIdSet = (await _db.UserProfiles.AsNoTracking()
            .Where(p => p.IsMall)
            .Select(p => p.AccountId)
            .ToListAsync(cancellationToken))
            .ToHashSet();

        if (vm.IsBuyNow && vm.BuyNowVariantId > 0)
        {
            var qty = vm.BuyNowQuantity <= 0 ? 1 : vm.BuyNowQuantity;
            qty = Math.Clamp(qty, 1, 99);

            var buyNowUtc = DateTime.UtcNow;
            var v = await _db.ProductVariants
                .AsNoTracking()
                .Where(x => x.Id == vm.BuyNowVariantId && x.IsActive && !x.IsDeleted)
                .Include(x => x.Product)!.ThenInclude(p => p!.Images)
                .Include(x => x.Images)
                .Include(x => x.OptionValue1)
                .Include(x => x.OptionValue2)
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

            var unitPrice = GetEffectiveUnitPrice(v, buyNowUtc);
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
                ProductId = v.Product.Id,
                CategoryId = v.Product.CategoryId,
                RootCategoryId = ResolveRootCategoryId(v.Product.CategoryId, allCategoriesMap),
                ShopId = v.Product.ShopId,
                IsMallShop = mallShopIdSet.Contains(v.Product.ShopId),
                IsFlashSaleItem = IsVariantInFlashSale(v, buyNowUtc),
                ProductName = v.Product.Name,
                ProductSlug = v.Product.Slug,
                ImageUrl = imageUrl,
                Color = v.OptionValue1?.Value,
                Size = v.OptionValue2?.Value,
                UnitPrice = unitPrice,
                Quantity = qty,
                LineTotal = lineTotal,
                IsDigital = v.Product.IsDigital
            });

            vm.SelectedItemCount = qty;
            vm.SelectedSubTotal = lineTotal;
            vm.IsAllDigital = v.Product.IsDigital;

            // Shipping fee for buy-now
            if (v.Product.IsDigital)
            {
                vm.ShippingFee = 0m;
            }
            else if (vm.Address is not null)
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
                            vm.ShippingFee = await CalculateShippingFeeForVariantsAsync(shopAddr, customerAddress, new[] { (v, qty) }, cancellationToken);
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

            vm.FinalAmount = vm.SelectedSubTotal + vm.ShippingFee;
        }
        else
        {
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
            var nowUtc = DateTime.UtcNow;

            var cartItems = await _db.CartItems
                .AsNoTracking()
                .Where(i => i.CartId == cartId.Value)
                .Include(i => i.Variant)!.ThenInclude(v => v!.Product)!.ThenInclude(p => p!.Images)
                .Include(i => i.Variant)!.ThenInclude(v => v!.Images)
                .Include(i => i.Variant)!.ThenInclude(v => v!.OptionValue1)
                .Include(i => i.Variant)!.ThenInclude(v => v!.OptionValue2)
                .OrderByDescending(i => i.Id)
                .ToListAsync(cancellationToken);

            var selectedCartEntities = new List<CartItem>();
            foreach (var item in cartItems)
            {
                var isSelected = selection.Initialized ? !selection.DeselectedIds.Contains(item.Id) : true;
                if (!isSelected) continue;

                var v = item.Variant;
                var p = v?.Product;
                if (v is null || p is null) continue;
                selectedCartEntities.Add(item);

                var unitPrice = GetEffectiveUnitPrice(v, nowUtc);
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
                    ProductId = p.Id,
                    CategoryId = p.CategoryId,
                    RootCategoryId = ResolveRootCategoryId(p.CategoryId, allCategoriesMap),
                    ShopId = p.ShopId,
                    IsMallShop = mallShopIdSet.Contains(p.ShopId),
                    IsFlashSaleItem = IsVariantInFlashSale(v, nowUtc),
                    ProductName = p.Name,
                    ProductSlug = p.Slug,
                    ImageUrl = imageUrl,
                    Color = v.OptionValue1?.Value,
                    Size = v.OptionValue2?.Value,
                    UnitPrice = unitPrice,
                    Quantity = item.Quantity,
                    LineTotal = lineTotal,
                    IsDigital = p.IsDigital
                });
            }

            vm.SelectedItemCount = vm.Items.Sum(i => i.Quantity);
            vm.SelectedSubTotal = vm.Items.Sum(i => i.LineTotal);
            vm.IsAllDigital = vm.Items.Count > 0 && vm.Items.All(i => i.IsDigital);

            // Shipping fee: sum per shop using GHN fee API.
            if (vm.IsAllDigital)
            {
                vm.ShippingFee = 0m;
            }
            else if (vm.Address is not null && vm.Items.Count > 0)
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
                                if (grp.All(i => i.IsDigital))
                                {
                                    continue;
                                }

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

                                var shopVariantItems = selectedCartEntities
                                    .Where(x => x.Variant!.Product!.ShopId == grp.Key && !x.Variant.Product.IsDigital)
                                    .Select(x => (x.Variant!, x.Quantity));
                                shippingTotal += await CalculateShippingFeeForVariantsAsync(shopAddr, customerAddress, shopVariantItems, cancellationToken);
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

            vm.FinalAmount = vm.SelectedSubTotal + vm.ShippingFee;
        }

        var shopIdsInOrder = vm.Items.Select(i => i.ShopId).Distinct().ToList();
        var evalTime = DateTime.Now;

        var activeVouchers = await _db.Vouchers
            .AsNoTracking()
            .Where(v => v.IsActive && v.StartDate <= evalTime && v.EndDate >= evalTime && (v.UsageLimit == 0 || v.UsedCount < v.UsageLimit))
            .Where(v => v.ShopId == null || shopIdsInOrder.Contains(v.ShopId.Value))
            .ToListAsync(cancellationToken);

        var voucherShopIds = activeVouchers.Where(v => v.ShopId.HasValue).Select(v => v.ShopId!.Value).Distinct().ToList();
        var shopNamesMap = await _db.UserProfiles
            .AsNoTracking()
            .Where(p => voucherShopIds.Contains(p.AccountId) || shopIdsInOrder.Contains(p.AccountId))
            .ToDictionaryAsync(p => p.AccountId, p => p.ShopName ?? p.FullName ?? $"Shop #{p.AccountId}", cancellationToken);

        var customerPastOrders = await _db.Orders
            .AsNoTracking()
            .Where(o => o.CustomerId == accountId && o.OrderStatus != "Cancelled" && o.OrderStatus != "Rejected")
            .Select(o => new { o.ShopId, o.VoucherId })
            .ToListAsync(cancellationToken);

        int customerOrderCountAll = customerPastOrders.Count;
        var customerOrdersByShop = customerPastOrders
            .GroupBy(o => o.ShopId)
            .ToDictionary(g => g.Key, g => g.Count());
        var customerVoucherUsage = customerPastOrders
            .Where(o => o.VoucherId.HasValue)
            .GroupBy(o => o.VoucherId!.Value)
            .ToDictionary(g => g.Key, g => g.Count());

        var savedVoucherCodes = (await _db.SavedVouchers
            .AsNoTracking()
            .Where(sv => sv.AccountId == accountId)
            .Select(sv => sv.Voucher != null ? sv.Voucher.Code : null)
            .Where(c => c != null)
            .ToListAsync(cancellationToken))
            .Select(c => c!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        bool noExplicitVoucherRequested = string.IsNullOrWhiteSpace(voucherCode)
            && string.IsNullOrWhiteSpace(shippingVoucherCode)
            && string.IsNullOrWhiteSpace(discountVoucherCode);

        if (noExplicitVoucherRequested)
        {
            if (Request.Cookies.TryGetValue("sellzy_claimed_voucher", out var claimedCookie) && !string.IsNullOrWhiteSpace(claimedCookie))
            {
                savedVoucherCodes.Add(claimedCookie.Trim());
            }
        }

        var typedCodes = new HashSet<string>(savedVoucherCodes, StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(voucherCode)) typedCodes.Add(voucherCode.Trim());
        if (!string.IsNullOrWhiteSpace(shippingVoucherCode)) typedCodes.Add(shippingVoucherCode.Trim());
        if (!string.IsNullOrWhiteSpace(discountVoucherCode)) typedCodes.Add(discountVoucherCode.Trim());

        Voucher? bestAutoShipVoucher = null;
        decimal bestAutoShipSaving = 0m;
        Voucher? bestAutoDiscVoucher = null;
        decimal bestAutoDiscSaving = 0m;

        foreach (var v in activeVouchers)
        {
            // PrivateChatOnly vouchers stay hidden from public list unless the buyer saved or explicitly typed/selected its code
            if (string.Equals(v.TargetAudience, "PrivateChatOnly", StringComparison.OrdinalIgnoreCase) && !typedCodes.Contains(v.Code))
            {
                continue;
            }

            var eval = EvaluateVoucherForItems(
                v,
                vm.Items,
                vm.ShippingFee,
                customerOrderCountAll,
                customerOrdersByShop,
                customerVoucherUsage,
                allCategoriesMap,
                shopNamesMap);

            vm.AvailableVouchers.Add(new CheckoutVoucherVm
            {
                Id = v.Id,
                Code = v.Code,
                DiscountType = v.DiscountType,
                DiscountValue = v.DiscountValue,
                MinOrderValue = v.MinOrderValue,
                MaxDiscountValue = v.MaxDiscountValue,
                EndDate = v.EndDate,
                IsShopVoucher = v.ShopId.HasValue,
                SponsorLabel = eval.SponsorLabel,
                ScopeLabel = eval.ScopeLabel,
                IsEligible = eval.IsEligible,
                IneligibleReason = eval.IneligibleReason
            });

            if (noExplicitVoucherRequested && eval.IsEligible && vm.Items.Count > 0)
            {
                bool isUserSaved = savedVoucherCodes.Contains(v.Code);
                if (v.DiscountType == "FreeShipping" && vm.ShippingFee > 0)
                {
                    var saving = Math.Min(v.DiscountValue, vm.ShippingFee);
                    if (saving > bestAutoShipSaving || (saving == bestAutoShipSaving && saving > 0 && isUserSaved && (bestAutoShipVoucher == null || !savedVoucherCodes.Contains(bestAutoShipVoucher.Code))))
                    {
                        bestAutoShipSaving = saving;
                        bestAutoShipVoucher = v;
                    }
                }
                else if (v.DiscountType != "FreeShipping")
                {
                    decimal saving = 0m;
                    if (v.DiscountType == "Percent")
                    {
                        saving = eval.EligibleSubtotal * (v.DiscountValue / 100m);
                        if (v.MaxDiscountValue.HasValue && saving > v.MaxDiscountValue.Value)
                            saving = v.MaxDiscountValue.Value;
                    }
                    else if (v.DiscountType == "FixedAmount")
                    {
                        saving = Math.Min(v.DiscountValue, eval.EligibleSubtotal);
                    }

                    if (saving > bestAutoDiscSaving || (saving == bestAutoDiscSaving && saving > 0 && isUserSaved && (bestAutoDiscVoucher == null || !savedVoucherCodes.Contains(bestAutoDiscVoucher.Code))))
                    {
                        bestAutoDiscSaving = saving;
                        bestAutoDiscVoucher = v;
                    }
                }
            }
        }

        string? resolvedShipCode = shippingVoucherCode;
        string? resolvedDiscCode = discountVoucherCode;

        if (noExplicitVoucherRequested)
        {
            if (bestAutoShipVoucher != null) resolvedShipCode = bestAutoShipVoucher.Code;
            if (bestAutoDiscVoucher != null) resolvedDiscCode = bestAutoDiscVoucher.Code;
            if (bestAutoShipVoucher != null || bestAutoDiscVoucher != null)
            {
                ViewData["AutoAppliedVoucherHint"] = true;
            }
        }

        if (!string.IsNullOrWhiteSpace(voucherCode))
        {
            var code = voucherCode.Trim().ToUpper();
            var v = activeVouchers.FirstOrDefault(x => x.Code.ToUpper() == code);
            if (v != null)
            {
                if (v.DiscountType == "FreeShipping")
                {
                    resolvedShipCode = v.Code;
                }
                else
                {
                    resolvedDiscCode = v.Code;
                }
            }
            else
            {
                vm.ErrorMessage = "Mã giảm giá không tồn tại, hết hạn hoặc không áp dụng cho đơn này.";
            }
        }

        decimal shippingDiscount = 0m;
        decimal discountDiscount = 0m;

        if (!string.IsNullOrWhiteSpace(resolvedShipCode))
        {
            var code = resolvedShipCode.Trim().ToUpper();
            var v = activeVouchers.FirstOrDefault(x => x.Code.ToUpper() == code && x.DiscountType == "FreeShipping");
            if (v != null)
            {
                var eval = EvaluateVoucherForItems(
                    v,
                    vm.Items,
                    vm.ShippingFee,
                    customerOrderCountAll,
                    customerOrdersByShop,
                    customerVoucherUsage,
                    allCategoriesMap,
                    shopNamesMap);

                if (eval.IsEligible)
                {
                    shippingDiscount = v.DiscountValue;
                    if (shippingDiscount > vm.ShippingFee) shippingDiscount = vm.ShippingFee;

                    vm.AppliedShippingVoucherCode = v.Code;
                    vm.ShippingVoucherDiscountAmount = shippingDiscount;
                }
                else
                {
                    vm.ErrorMessage = $"Mã {v.Code}: {eval.IneligibleReason}";
                }
            }
            else if (string.IsNullOrWhiteSpace(vm.ErrorMessage))
            {
                vm.ErrorMessage = "Mã vận chuyển không hợp lệ hoặc đã hết hạn.";
            }
        }

        if (!string.IsNullOrWhiteSpace(resolvedDiscCode))
        {
            var code = resolvedDiscCode.Trim().ToUpper();
            var v = activeVouchers.FirstOrDefault(x => x.Code.ToUpper() == code && x.DiscountType != "FreeShipping");
            if (v != null)
            {
                var eval = EvaluateVoucherForItems(
                    v,
                    vm.Items,
                    vm.ShippingFee,
                    customerOrderCountAll,
                    customerOrdersByShop,
                    customerVoucherUsage,
                    allCategoriesMap,
                    shopNamesMap);

                if (eval.IsEligible)
                {
                    var eligibleSubtotal = eval.EligibleSubtotal;
                    if (v.DiscountType == "Percent")
                    {
                        discountDiscount = eligibleSubtotal * (v.DiscountValue / 100m);
                        if (v.MaxDiscountValue.HasValue && discountDiscount > v.MaxDiscountValue.Value)
                            discountDiscount = v.MaxDiscountValue.Value;
                    }
                    else if (v.DiscountType == "FixedAmount")
                    {
                        discountDiscount = v.DiscountValue;
                        if (discountDiscount > eligibleSubtotal) discountDiscount = eligibleSubtotal;
                    }

                    vm.AppliedDiscountVoucherCode = v.Code;
                    vm.DiscountVoucherDiscountAmount = discountDiscount;
                }
                else
                {
                    vm.ErrorMessage = $"Mã {v.Code}: {eval.IneligibleReason}";
                }
            }
            else if (string.IsNullOrWhiteSpace(vm.ErrorMessage))
            {
                vm.ErrorMessage = "Mã giảm giá không hợp lệ hoặc đã hết hạn.";
            }
        }

        vm.VatAmount = Math.Round(vm.SelectedSubTotal * vm.GlobalVatRate);
        vm.FinalAmount = vm.SelectedSubTotal + vm.ShippingFee - shippingDiscount - discountDiscount;
        if (vm.FinalAmount < 0) vm.FinalAmount = 0;

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

        if (vmFinal.Address is null && !vmFinal.IsAllDigital)
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
            appliedVoucherCode = vm.AppliedVoucherCode,
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
                lineTotal = i.LineTotal
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

        var paymentMethod = string.IsNullOrWhiteSpace(req.PaymentMethod) ? "COD" : req.PaymentMethod.Trim();
        if (paymentMethod.Length > 30) paymentMethod = paymentMethod[..30];

        var address = await _db.UserAddresses
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == req.AddressId && a.AccountId == accountId.Value, cancellationToken);

        var customerAccount = await _db.Accounts
            .AsNoTracking()
            .Include(a => a.Profile)
            .FirstOrDefaultAsync(a => a.Id == accountId.Value, cancellationToken);

        var nowUtc = DateTime.UtcNow;

        // Buy-now mode: do NOT touch cart.
        if (req.BuyNowVariantId.HasValue && req.BuyNowVariantId.Value > 0)
        {
            var buyQty = Math.Clamp(req.BuyNowQuantity.GetValueOrDefault(1), 1, 99);
            var v = await _db.ProductVariants
                .Where(x => x.Id == req.BuyNowVariantId.Value && x.IsActive && !x.IsDeleted)
                .Include(x => x.Product)!
                .Include(x => x.OptionValue1)
                .Include(x => x.OptionValue2)
                .FirstOrDefaultAsync(cancellationToken);

            if (v?.Product is null)
            {
                var vmBad = await BuildCheckoutVmAsync(accountId.Value, preferredAddressId: address?.Id, paymentMethod, req.VoucherCode, cancellationToken);
                vmBad.ErrorMessage = "Sản phẩm không hợp lệ hoặc đã ngừng bán.";
                return View("Index", vmBad);
            }

            bool isDigitalBuyNow = v.Product.IsDigital;

            if (!isDigitalBuyNow && address is null)
            {
                var vmMissingAddress = await BuildCheckoutVmAsync(accountId.Value, preferredAddressId: null, paymentMethod, req.VoucherCode, cancellationToken);
                vmMissingAddress.ErrorMessage = "Địa chỉ không hợp lệ. Vui lòng chọn lại.";
                return View("Index", vmMissingAddress);
            }

            if (isDigitalBuyNow && (paymentMethod == "COD" || paymentMethod == "WalletCOD"))
            {
                var vmCodBlock = await BuildCheckoutVmAsync(accountId.Value, preferredAddressId: address?.Id, paymentMethod, req.BuyNowVariantId, req.BuyNowQuantity, req.VoucherCode, req.ShippingVoucherCode, req.DiscountVoucherCode, cancellationToken);
                vmCodBlock.ErrorMessage = "Sản phẩm số / E-Voucher chỉ hỗ trợ thanh toán trực tuyến qua Ví Sellzy hoặc VNPay (không áp dụng COD).";
                return View("Index", vmCodBlock);
            }

            if (v.StockQuantity <= 0)
            {
                var vmOos = await BuildCheckoutVmAsync(accountId.Value, preferredAddressId: address?.Id, paymentMethod, req.VoucherCode, cancellationToken);
                vmOos.ErrorMessage = "Sản phẩm đã hết hàng.";
                return View("Index", vmOos);
            }

            if (buyQty > v.StockQuantity)
            {
                buyQty = v.StockQuantity;
            }

            decimal shippingFee = 0m;
            if (!isDigitalBuyNow)
            {
                var senderAddress = await _db.UserAddresses
                    .AsNoTracking()
                    .Where(a => a.AccountId == v.Product.ShopId)
                    .OrderByDescending(a => a.AddressType == AddressType.Shop)
                    .ThenByDescending(a => a.IsDefault)
                    .ThenByDescending(a => a.UpdatedAt)
                    .FirstOrDefaultAsync(cancellationToken);

                if (senderAddress is null)
                {
                    var vmNoSender = await BuildCheckoutVmAsync(accountId.Value, preferredAddressId: address!.Id, paymentMethod, req.VoucherCode, cancellationToken);
                    vmNoSender.ErrorMessage = "Thiếu địa chỉ người gửi của shop. Không thể tính phí vận chuyển.";
                    return View("Index", vmNoSender);
                }

                try
                {
                    shippingFee = await CalculateShippingFeeForVariantsAsync(senderAddress, address!, new[] { (v, buyQty) }, cancellationToken);
                }
                catch (InvalidOperationException ex)
                {
                    _logger.LogWarning(ex, "GHN fee not configured or invalid (POST /checkout buy-now). ShopId={ShopId}", _ghnOptions.ShopId);
                    var vmFeeFail = await BuildCheckoutVmAsync(accountId.Value, preferredAddressId: address!.Id, paymentMethod, req.VoucherCode, cancellationToken);
                    vmFeeFail.ErrorMessage = ex.Message;
                    return View("Index", vmFeeFail);
                }
                catch (HttpRequestException ex)
                {
                    _logger.LogWarning(ex, "GHN fee request failed (POST /checkout buy-now). ShopId={ShopId}", _ghnOptions.ShopId);
                    var vmFeeFail = await BuildCheckoutVmAsync(accountId.Value, preferredAddressId: address!.Id, paymentMethod, req.VoucherCode, cancellationToken);
                    vmFeeFail.ErrorMessage = "Không tính được phí vận chuyển GHN. Vui lòng kiểm tra ShopId/Token và địa chỉ (District/Ward), hoặc thử lại.";
                    return View("Index", vmFeeFail);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unexpected error while calculating GHN fee (POST /checkout buy-now). ShopId={ShopId}", _ghnOptions.ShopId);
                    var vmFeeFail = await BuildCheckoutVmAsync(accountId.Value, preferredAddressId: address!.Id, paymentMethod, req.VoucherCode, cancellationToken);
                    vmFeeFail.ErrorMessage = "Không tính được phí vận chuyển GHN. Vui lòng thử lại.";
                    return View("Index", vmFeeFail);
                }
            }

            var unitPrice = GetEffectiveUnitPrice(v, nowUtc);
            var subTotal = unitPrice * buyQty;

            var (catMap, defCommRate) = await LoadCommissionContextAsync(cancellationToken);
            var isBuyNowMall = await _db.UserProfiles.AsNoTracking()
                .AnyAsync(up => up.AccountId == v.Product.ShopId && up.IsMall, cancellationToken);

            var customerPastOrders = await _db.Orders
                .AsNoTracking()
                .Where(o => o.CustomerId == accountId.Value && o.OrderStatus != "Cancelled" && o.OrderStatus != "Rejected")
                .Select(o => new { o.ShopId, o.VoucherId })
                .ToListAsync(cancellationToken);
            int customerOrderCountAll = customerPastOrders.Count;
            var customerOrdersByShop = customerPastOrders.GroupBy(o => o.ShopId).ToDictionary(g => g.Key, g => g.Count());
            var customerVoucherUsage = customerPastOrders.Where(o => o.VoucherId.HasValue).GroupBy(o => o.VoucherId!.Value).ToDictionary(g => g.Key, g => g.Count());
            var emptyShopNames = new Dictionary<int, string>();

            var buyNowEvalItems = new List<CheckoutItemVm>
            {
                new CheckoutItemVm
                {
                    VariantId = v.Id,
                    ProductId = v.Product.Id,
                    CategoryId = v.Product.CategoryId,
                    RootCategoryId = ResolveRootCategoryId(v.Product.CategoryId, catMap),
                    ShopId = v.Product.ShopId,
                    IsMallShop = isBuyNowMall,
                    IsFlashSaleItem = IsVariantInFlashSale(v, nowUtc),
                    UnitPrice = unitPrice,
                    Quantity = buyQty,
                    LineTotal = subTotal
                }
            };

            var evalTime = DateTime.Now;
            string? buyNowShipCode = req.ShippingVoucherCode;
            string? buyNowDiscCode = req.DiscountVoucherCode;

            if (!string.IsNullOrWhiteSpace(req.VoucherCode))
            {
                var code = req.VoucherCode.Trim().ToUpper();
                var voucher = await _db.Vouchers
                    .Where(x => x.Code.ToUpper() == code && x.IsActive && x.StartDate <= evalTime && x.EndDate >= evalTime && (x.UsageLimit == 0 || x.UsedCount < x.UsageLimit))
                    .Where(x => x.ShopId == null || x.ShopId == v.Product.ShopId)
                    .FirstOrDefaultAsync(cancellationToken);
                if (voucher != null)
                {
                    if (voucher.DiscountType == "FreeShipping")
                        buyNowShipCode = voucher.Code;
                    else
                        buyNowDiscCode = voucher.Code;
                }
            }

            decimal shippingDiscount = 0m;
            decimal discountDiscount = 0m;
            decimal shopDiscountAmount = 0m;
            decimal platformDiscountAmount = 0m;
            int? appliedVoucherId = null;

            if (!string.IsNullOrWhiteSpace(buyNowShipCode))
            {
                var code = buyNowShipCode.Trim().ToUpper();
                var voucher = await _db.Vouchers
                    .Where(x => x.Code.ToUpper() == code && x.IsActive && x.StartDate <= evalTime && x.EndDate >= evalTime && (x.UsageLimit == 0 || x.UsedCount < x.UsageLimit))
                    .Where(x => x.ShopId == null || x.ShopId == v.Product.ShopId)
                    .Where(x => x.DiscountType == "FreeShipping")
                    .FirstOrDefaultAsync(cancellationToken);

                if (voucher != null)
                {
                    var eval = EvaluateVoucherForItems(voucher, buyNowEvalItems, shippingFee, customerOrderCountAll, customerOrdersByShop, customerVoucherUsage, catMap, emptyShopNames);
                    if (eval.IsEligible)
                    {
                        shippingDiscount = voucher.DiscountValue;
                        if (shippingDiscount > shippingFee) shippingDiscount = shippingFee;

                        if (shippingDiscount > 0)
                        {
                            appliedVoucherId = voucher.Id;
                            voucher.UsedCount += 1;
                            if (voucher.ShopId.HasValue) shopDiscountAmount += shippingDiscount;
                            else platformDiscountAmount += shippingDiscount;
                        }
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(buyNowDiscCode))
            {
                var code = buyNowDiscCode.Trim().ToUpper();
                var voucher = await _db.Vouchers
                    .Where(x => x.Code.ToUpper() == code && x.IsActive && x.StartDate <= evalTime && x.EndDate >= evalTime && (x.UsageLimit == 0 || x.UsedCount < x.UsageLimit))
                    .Where(x => x.ShopId == null || x.ShopId == v.Product.ShopId)
                    .Where(x => x.DiscountType != "FreeShipping")
                    .FirstOrDefaultAsync(cancellationToken);

                if (voucher != null)
                {
                    var eval = EvaluateVoucherForItems(voucher, buyNowEvalItems, shippingFee, customerOrderCountAll, customerOrdersByShop, customerVoucherUsage, catMap, emptyShopNames);
                    if (eval.IsEligible)
                    {
                        var eligibleSub = eval.EligibleSubtotal;
                        if (voucher.DiscountType == "Percent")
                        {
                            discountDiscount = eligibleSub * (voucher.DiscountValue / 100m);
                            if (voucher.MaxDiscountValue.HasValue && discountDiscount > voucher.MaxDiscountValue.Value)
                                discountDiscount = voucher.MaxDiscountValue.Value;
                        }
                        else if (voucher.DiscountType == "FixedAmount")
                        {
                            discountDiscount = voucher.DiscountValue;
                            if (discountDiscount > eligibleSub) discountDiscount = eligibleSub;
                        }

                        if (discountDiscount > 0)
                        {
                            appliedVoucherId = voucher.Id;
                            voucher.UsedCount += 1;
                            if (voucher.ShopId.HasValue) shopDiscountAmount += discountDiscount;
                            else platformDiscountAmount += discountDiscount;
                        }
                    }
                }
            }

            var totalDiscountAmount = shippingDiscount + discountDiscount;
            var finalAmt = subTotal + shippingFee - totalDiscountAmount;
            if (finalAmt < 0) finalAmt = 0;

            var walletBalance = await _wallet.GetBalanceAsync(accountId.Value, cancellationToken);

            if (paymentMethod == "Wallet" && walletBalance < finalAmt)
            {
                var vmBad = await BuildCheckoutVmAsync(accountId.Value, preferredAddressId: address?.Id, paymentMethod, req.VoucherCode, cancellationToken);
                vmBad.ErrorMessage = $"Số dư ví không đủ ({walletBalance:N0}đ) để thanh toán cho đơn hàng ({finalAmt:N0}đ). Vui lòng chọn phương thức kết hợp.";
                return View("Index", vmBad);
            }

            var order = new Order
            {
                CustomerId = accountId.Value,
                ShopId = v.Product.ShopId,
                OrderCode = GenerateOrderCode(nowUtc),
                SubTotal = subTotal,
                ShippingFee = shippingFee,
                VoucherId = appliedVoucherId,
                DiscountAmount = totalDiscountAmount,
                ShopDiscountAmount = shopDiscountAmount,
                PlatformDiscountAmount = platformDiscountAmount,
                FinalAmount = finalAmt,
                OrderStatus = "Pending",
                PaymentMethod = paymentMethod,
                PaymentStatus = "Pending",
                IsDigitalOrder = isDigitalBuyNow,
                RequiresVatInvoice = req.RequiresVatInvoice,
                CompanyName = req.RequiresVatInvoice ? req.CompanyName?.Trim() : null,
                TaxCode = req.RequiresVatInvoice ? req.TaxCode?.Trim() : null,
                CompanyAddress = req.RequiresVatInvoice ? req.CompanyAddress?.Trim() : null,
                RecipientName = address?.FullName ?? customerAccount?.Profile?.FullName ?? customerAccount?.Email ?? "Khách hàng",
                RecipientPhone = address?.PhoneNumber ?? customerAccount?.Profile?.PhoneNumber ?? "0000000000",
                ShippingAddress = address != null ? ComposeAddress(address) : "Giao mã tự động qua hệ thống (Sản phẩm số / E-Voucher)",
                ShippingProvinceId = address?.ProvinceId ?? 0,
                ShippingDistrictId = address?.DistrictId ?? 0,
                ShippingWardCode = address?.WardCode,
                ShippingStreetAddress = address?.StreetAddress,
                CreatedAt = nowUtc,
                UpdatedAt = nowUtc,
            };

            var buyNowPolicy = CommissionHelper.ResolveCategoryPolicy(v.Product.CategoryId, catMap, defCommRate);
            var rawBuyNowCommission = CommissionHelper.CalculateItemCommission(
                unitPrice, buyQty, buyNowPolicy.CommissionRatePercent, buyNowPolicy.MaxCommissionAmount, isBuyNowMall);
            var buyNowEffectiveBase = Math.Max(0m, subTotal - shopDiscountAmount);
            var buyNowCommission = subTotal > 0m
                ? Math.Round(rawBuyNowCommission * (buyNowEffectiveBase / subTotal), 0)
                : rawBuyNowCommission;

            order.Items.Add(new OrderItem
            {
                VariantId = v.Id,
                ProductName = v.Product.Name,
                ColorName = v.OptionValue1?.Value,
                SizeName = v.OptionValue2?.Value,
                VariantDescription = v.DisplayVariantName,
                CommissionAmount = buyNowCommission,
                UnitCostPrice = v.CostPrice,
                UnitPrice = unitPrice,
                Quantity = buyQty,
                LineTotal = subTotal
            });

            decimal walletDeductAmt = 0m;
            decimal vnPayAmt = 0m;

            if (paymentMethod == "Wallet")
            {
                walletDeductAmt = finalAmt;
                order.PaymentStatus = "Paid";
            }
            else if (paymentMethod == "WalletCOD")
            {
                walletDeductAmt = Math.Min(walletBalance, finalAmt);
                order.PaymentStatus = "Pending";
            }
            else if (paymentMethod == "WalletVNPay")
            {
                walletDeductAmt = Math.Min(walletBalance, finalAmt);
                vnPayAmt = finalAmt - walletDeductAmt;
                if (vnPayAmt > 0)
                {
                    order.PaymentStatus = "AwaitingPayment";
                }
                else
                {
                    order.PaymentStatus = "Paid";
                }
            }
            else if (paymentMethod == "BankTransfer")
            {
                vnPayAmt = finalAmt;
                order.PaymentStatus = "AwaitingPayment";
            }

            await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                _db.Orders.Add(order);
                _db.SystemNotifications.Add(new SystemNotification
                {
                    Title = $"Đơn hàng mới #{order.OrderCode} ({order.FinalAmount:N0}₫)",
                    Content = $"Gian hàng của bạn vừa nhận được đơn hàng mới #{order.OrderCode} từ khách hàng {order.RecipientName} ({order.Items.Sum(i => i.Quantity)} sản phẩm, tổng thanh toán {order.FinalAmount:N0}₫ - {order.PaymentMethod}). Vui lòng kiểm tra và xác nhận đơn hàng.",
                    TargetType = "Account",
                    TargetAccountId = order.ShopId,
                    SentByAdminId = accountId.Value,
                    CreatedAt = DateTime.UtcNow
                });
                await _db.SaveChangesAsync(cancellationToken);

                if (walletDeductAmt > 0)
                {
                    var (payOk, payMsg) = await _wallet.PayWithWalletAsync(
                        accountId.Value,
                        walletDeductAmt,
                        order.OrderCode,
                        $"Thanh toán đơn hàng #{order.OrderCode}",
                        cancellationToken);

                    if (!payOk)
                    {
                        await tx.RollbackAsync(cancellationToken);
                        var vmBad = await BuildCheckoutVmAsync(accountId.Value, preferredAddressId: address?.Id, paymentMethod, req.VoucherCode, cancellationToken);
                        vmBad.ErrorMessage = $"Thanh toán ví lỗi: {payMsg}";
                        return View("Index", vmBad);
                    }
                }

                if (order.IsDigitalOrder && order.PaymentStatus == "Paid")
                {
                    await CompleteAndDeliverDigitalOrderAsync(order, cancellationToken);
                    await _db.SaveChangesAsync(cancellationToken);
                }

                await tx.CommitAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync(cancellationToken);
                _logger.LogError(ex, "Failed to place order in Buy-Now mode.");
                throw;
            }

            if (vnPayAmt > 0)
            {
                var txnRef = $"PAY-{order.OrderCode}-{DateTime.Now:yyyyMMddHHmmss}";
                var orderInfo = $"Thanh toan don hang #{order.OrderCode}";
                var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
                var scheme = Request.Scheme;
                var host = Request.Host.ToUriComponent();
                var callbackUrl = $"{scheme}://{host}/checkout/vnpay-return";
                var payUrl = _vnPay.CreatePaymentUrl(txnRef, vnPayAmt, orderInfo, ipAddress, customReturnUrl: callbackUrl);
                return Redirect(payUrl);
            }

            var codes = Uri.EscapeDataString(order.OrderCode);
            return Redirect($"/checkout/success?codes={codes}");
        }
        else
        {
            // Default: checkout selected items from cart
            var cartId = await _db.Carts
            .AsNoTracking()
            .Where(c => c.AccountId == accountId.Value)
            .Select(c => (int?)c.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (!cartId.HasValue)
        {
            var vmEmpty = await BuildCheckoutVmAsync(accountId.Value, preferredAddressId: address?.Id, paymentMethod, req.VoucherCode, cancellationToken);
            vmEmpty.ErrorMessage = "Giỏ hàng trống.";
            return View("Index", vmEmpty);
        }

        var selection = GetCartSelectionState();

        var cartItems = await _db.CartItems
            .Where(i => i.CartId == cartId.Value)
            .Include(i => i.Variant)!.ThenInclude(v => v!.Product)!
            .Include(i => i.Variant)!.ThenInclude(v => v!.OptionValue1)
            .Include(i => i.Variant)!.ThenInclude(v => v!.OptionValue2)
            .ToListAsync(cancellationToken);

        var selectedItems = cartItems
            .Where(i => !selection.Initialized || !selection.DeselectedIds.Contains(i.Id))
            .Where(i => i.Variant != null && i.Variant.Product != null)
            .ToList();

        if (selectedItems.Count == 0)
        {
            var vmNone = await BuildCheckoutVmAsync(accountId.Value, preferredAddressId: address?.Id, paymentMethod, req.VoucherCode, cancellationToken);
            vmNone.ErrorMessage = "Vui lòng chọn ít nhất 1 sản phẩm để thanh toán.";
            return View("Index", vmNone);
        }

        bool isAllCartDigital = selectedItems.All(i => i.Variant!.Product!.IsDigital);
        bool hasAnyDigital = selectedItems.Any(i => i.Variant!.Product!.IsDigital);

        if (!isAllCartDigital && address is null)
        {
            var vmMissingAddress = await BuildCheckoutVmAsync(accountId.Value, preferredAddressId: null, paymentMethod, req.VoucherCode, cancellationToken);
            vmMissingAddress.ErrorMessage = "Địa chỉ không hợp lệ. Vui lòng chọn lại.";
            return View("Index", vmMissingAddress);
        }

        if (hasAnyDigital && (paymentMethod == "COD" || paymentMethod == "WalletCOD"))
        {
            var vmCodBlock = await BuildCheckoutVmAsync(accountId.Value, preferredAddressId: address?.Id, paymentMethod, req.VoucherCode, req.ShippingVoucherCode, req.DiscountVoucherCode, cancellationToken);
            vmCodBlock.ErrorMessage = "Đơn hàng có chứa Sản phẩm số / E-Voucher chỉ hỗ trợ thanh toán trực tuyến qua Ví Sellzy hoặc VNPay (không áp dụng COD).";
            return View("Index", vmCodBlock);
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

        var (cartCatMap, cartDefCommRate) = await LoadCommissionContextAsync(cancellationToken);
        var orders = new List<Order>();
        foreach (var grp in selectedItems.GroupBy(i => i.Variant!.Product!.ShopId))
        {
            var subTotal = 0m;
            var shippingFee = 0m;
            bool isShopOrderDigital = grp.All(x => x.Variant!.Product!.IsDigital);

            if (!isShopOrderDigital)
            {
                if (!senderByShop.TryGetValue(grp.Key, out var senderAddress))
                {
                    var vmNoSender = await BuildCheckoutVmAsync(accountId.Value, preferredAddressId: address!.Id, paymentMethod, req.VoucherCode, cancellationToken);
                    vmNoSender.ErrorMessage = "Thiếu địa chỉ người gửi của shop. Không thể tính phí vận chuyển.";
                    return View("Index", vmNoSender);
                }

                try
                {
                    shippingFee = await CalculateShippingFeeForVariantsAsync(
                        senderAddress,
                        address!,
                        grp.Where(x => !x.Variant!.Product!.IsDigital).Select(x => (x.Variant!, x.Quantity)),
                        cancellationToken);
                }
                catch (InvalidOperationException ex)
                {
                    _logger.LogWarning(ex, "GHN fee not configured or invalid (POST /checkout). ShopId={ShopId}", _ghnOptions.ShopId);
                    var vmFeeFail = await BuildCheckoutVmAsync(accountId.Value, preferredAddressId: address!.Id, paymentMethod, req.VoucherCode, cancellationToken);
                    vmFeeFail.ErrorMessage = ex.Message;
                    return View("Index", vmFeeFail);
                }
                catch (HttpRequestException ex)
                {
                    _logger.LogWarning(ex, "GHN fee request failed (POST /checkout). ShopId={ShopId}", _ghnOptions.ShopId);
                    var vmFeeFail = await BuildCheckoutVmAsync(accountId.Value, preferredAddressId: address!.Id, paymentMethod, req.VoucherCode, cancellationToken);
                    vmFeeFail.ErrorMessage = "Không tính được phí vận chuyển GHN. Vui lòng kiểm tra ShopId/Token và địa chỉ (District/Ward), hoặc thử lại.";
                    return View("Index", vmFeeFail);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unexpected error while calculating GHN fee (POST /checkout). ShopId={ShopId}", _ghnOptions.ShopId);
                    var vmFeeFail = await BuildCheckoutVmAsync(accountId.Value, preferredAddressId: address!.Id, paymentMethod, req.VoucherCode, cancellationToken);
                    vmFeeFail.ErrorMessage = "Không tính được phí vận chuyển GHN. Vui lòng thử lại.";
                    return View("Index", vmFeeFail);
                }
            }

            var order = new Order
            {
                CustomerId = accountId.Value,
                ShopId = grp.Key,
                OrderCode = GenerateOrderCode(nowUtc),
                SubTotal = 0m,
                ShippingFee = shippingFee,
                DiscountAmount = 0m,
                FinalAmount = 0m,
                OrderStatus = "Pending",
                PaymentMethod = paymentMethod,
                PaymentStatus = "Pending",
                IsDigitalOrder = isShopOrderDigital,
                RequiresVatInvoice = req.RequiresVatInvoice,
                CompanyName = req.RequiresVatInvoice ? req.CompanyName?.Trim() : null,
                TaxCode = req.RequiresVatInvoice ? req.TaxCode?.Trim() : null,
                CompanyAddress = req.RequiresVatInvoice ? req.CompanyAddress?.Trim() : null,
                RecipientName = address?.FullName ?? customerAccount?.Profile?.FullName ?? customerAccount?.Email ?? "Khách hàng",
                RecipientPhone = address?.PhoneNumber ?? customerAccount?.Profile?.PhoneNumber ?? "0000000000",
                ShippingAddress = (address != null && !isShopOrderDigital) ? ComposeAddress(address) : "Giao mã tự động qua hệ thống (Sản phẩm số / E-Voucher)",
                ShippingProvinceId = address?.ProvinceId ?? 0,
                ShippingDistrictId = address?.DistrictId ?? 0,
                ShippingWardCode = address?.WardCode,
                ShippingStreetAddress = address?.StreetAddress,
                CreatedAt = nowUtc,
                UpdatedAt = nowUtc,
            };

            var isGroupShopMall = await _db.UserProfiles.AsNoTracking()
                .AnyAsync(up => up.AccountId == order.ShopId && up.IsMall, cancellationToken);

            foreach (var item in grp)
            {
                var v = item.Variant!;
                var p = v.Product!;

                var unitPrice = GetEffectiveUnitPrice(v, nowUtc);
                var lineTotal = unitPrice * item.Quantity;
                subTotal += lineTotal;

                var itemPolicy = CommissionHelper.ResolveCategoryPolicy(p.CategoryId, cartCatMap, cartDefCommRate);
                var itemComm = CommissionHelper.CalculateItemCommission(
                    unitPrice, item.Quantity, itemPolicy.CommissionRatePercent, itemPolicy.MaxCommissionAmount, isGroupShopMall);

                order.Items.Add(new OrderItem
                {
                    VariantId = item.VariantId,
                    ProductName = p.Name,
                    ColorName = v.OptionValue1?.Value,
                    SizeName = v.OptionValue2?.Value,
                    VariantDescription = v.DisplayVariantName,
                    CommissionAmount = itemComm,
                    UnitCostPrice = v.CostPrice,
                    UnitPrice = unitPrice,
                    Quantity = item.Quantity,
                    LineTotal = lineTotal
                });
            }

            order.SubTotal = subTotal;
            order.FinalAmount = subTotal + order.ShippingFee;
            orders.Add(order);
        }

        string? placeShipCode = req.ShippingVoucherCode;
        string? placeDiscCode = req.DiscountVoucherCode;

        var shopIdsInOrder = orders.Select(o => o.ShopId).Distinct().ToList();
        var mallShopIdSet = (await _db.UserProfiles.AsNoTracking()
            .Where(p => shopIdsInOrder.Contains(p.AccountId) && p.IsMall)
            .Select(p => p.AccountId)
            .ToListAsync(cancellationToken))
            .ToHashSet();

        var customerPastOrders = await _db.Orders
            .AsNoTracking()
            .Where(o => o.CustomerId == accountId.Value && o.OrderStatus != "Cancelled" && o.OrderStatus != "Rejected")
            .Select(o => new { o.ShopId, o.VoucherId })
            .ToListAsync(cancellationToken);
        int customerOrderCountAll = customerPastOrders.Count;
        var customerOrdersByShop = customerPastOrders.GroupBy(o => o.ShopId).ToDictionary(g => g.Key, g => g.Count());
        var customerVoucherUsage = customerPastOrders.Where(o => o.VoucherId.HasValue).GroupBy(o => o.VoucherId!.Value).ToDictionary(g => g.Key, g => g.Count());
        var emptyShopNames = new Dictionary<int, string>();

        var cartEvalItems = selectedItems.Select(item =>
        {
            var v = item.Variant!;
            var p = v.Product!;
            var unitPrice = GetEffectiveUnitPrice(v, nowUtc);
            return new CheckoutItemVm
            {
                CartItemId = item.Id,
                VariantId = item.VariantId,
                ProductId = p.Id,
                CategoryId = p.CategoryId,
                RootCategoryId = ResolveRootCategoryId(p.CategoryId, cartCatMap),
                ShopId = p.ShopId,
                IsMallShop = mallShopIdSet.Contains(p.ShopId),
                IsFlashSaleItem = IsVariantInFlashSale(v, nowUtc),
                UnitPrice = unitPrice,
                Quantity = item.Quantity,
                LineTotal = unitPrice * item.Quantity
            };
        }).ToList();

        decimal totalCartShippingFee = orders.Sum(o => o.ShippingFee);

        if (!string.IsNullOrWhiteSpace(req.VoucherCode))
        {
            var code = req.VoucherCode.Trim().ToUpper();
            var evalTime = DateTime.Now;
            var voucher = await _db.Vouchers
                .Where(v => v.Code.ToUpper() == code && v.IsActive && v.StartDate <= evalTime && v.EndDate >= evalTime && (v.UsageLimit == 0 || v.UsedCount < v.UsageLimit))
                .Where(v => v.ShopId == null || shopIdsInOrder.Contains(v.ShopId.Value))
                .FirstOrDefaultAsync(cancellationToken);
            if (voucher != null)
            {
                if (voucher.DiscountType == "FreeShipping")
                    placeShipCode = voucher.Code;
                else
                    placeDiscCode = voucher.Code;
            }
        }

        if (!string.IsNullOrWhiteSpace(placeShipCode))
        {
            var code = placeShipCode.Trim().ToUpper();
            var evalTime = DateTime.Now;
            var voucher = await _db.Vouchers
                .Where(v => v.Code.ToUpper() == code && v.IsActive && v.StartDate <= evalTime && v.EndDate >= evalTime && (v.UsageLimit == 0 || v.UsedCount < v.UsageLimit))
                .Where(v => v.ShopId == null || shopIdsInOrder.Contains(v.ShopId.Value))
                .Where(v => v.DiscountType == "FreeShipping")
                .FirstOrDefaultAsync(cancellationToken);

            if (voucher != null)
            {
                var eval = EvaluateVoucherForItems(voucher, cartEvalItems, totalCartShippingFee, customerOrderCountAll, customerOrdersByShop, customerVoucherUsage, cartCatMap, emptyShopNames);
                if (eval.IsEligible)
                {
                    var eligibleOrders = voucher.ShopId == null
                        ? orders.Where(o => o.ShippingFee > 0).ToList()
                        : orders.Where(o => o.ShopId == voucher.ShopId && o.ShippingFee > 0).ToList();

                    if (eligibleOrders.Count == 0)
                    {
                        eligibleOrders = voucher.ShopId == null ? orders : orders.Where(o => o.ShopId == voucher.ShopId).ToList();
                    }

                    var eligibleShipping = eligibleOrders.Sum(o => o.ShippingFee);
                    decimal discountAmount = voucher.DiscountValue;
                    if (discountAmount > eligibleShipping) discountAmount = eligibleShipping;

                    if (discountAmount > 0 && eligibleOrders.Count > 0)
                    {
                        voucher.UsedCount += 1;
                        decimal remainingDiscount = discountAmount;

                        for (int i = 0; i < eligibleOrders.Count; i++)
                        {
                            var o = eligibleOrders[i];
                            o.VoucherId = voucher.Id;

                            decimal orderDiscount = 0;
                            if (i == eligibleOrders.Count - 1)
                            {
                                orderDiscount = remainingDiscount;
                            }
                            else
                            {
                                decimal weight = eligibleShipping > 0 ? o.ShippingFee / eligibleShipping : 0;
                                orderDiscount = Math.Round(discountAmount * weight);
                                if (orderDiscount > remainingDiscount) orderDiscount = remainingDiscount;
                            }

                            o.DiscountAmount += orderDiscount;
                            if (voucher.ShopId.HasValue) o.ShopDiscountAmount += orderDiscount;
                            else o.PlatformDiscountAmount += orderDiscount;

                            remainingDiscount -= orderDiscount;
                        }
                    }
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(placeDiscCode))
        {
            var code = placeDiscCode.Trim().ToUpper();
            var evalTime = DateTime.Now;
            var voucher = await _db.Vouchers
                .Where(v => v.Code.ToUpper() == code && v.IsActive && v.StartDate <= evalTime && v.EndDate >= evalTime && (v.UsageLimit == 0 || v.UsedCount < v.UsageLimit))
                .Where(v => v.ShopId == null || shopIdsInOrder.Contains(v.ShopId.Value))
                .Where(v => v.DiscountType != "FreeShipping")
                .FirstOrDefaultAsync(cancellationToken);

            if (voucher != null)
            {
                var eval = EvaluateVoucherForItems(voucher, cartEvalItems, totalCartShippingFee, customerOrderCountAll, customerOrdersByShop, customerVoucherUsage, cartCatMap, emptyShopNames);
                if (eval.IsEligible && eval.EligibleSubtotal > 0)
                {
                    var eligibleSubtotal = eval.EligibleSubtotal;
                    decimal discountAmount = 0;
                    if (voucher.DiscountType == "Percent")
                    {
                        discountAmount = eligibleSubtotal * (voucher.DiscountValue / 100m);
                        if (voucher.MaxDiscountValue.HasValue && discountAmount > voucher.MaxDiscountValue.Value)
                            discountAmount = voucher.MaxDiscountValue.Value;
                    }
                    else if (voucher.DiscountType == "FixedAmount")
                    {
                        discountAmount = voucher.DiscountValue;
                        if (discountAmount > eligibleSubtotal) discountAmount = eligibleSubtotal;
                    }

                    if (discountAmount > 0)
                    {
                        // Determine each order's eligible subtotal under this voucher's scope
                        var orderEligibleList = new List<(Order Order, decimal EligibleSub)>();
                        foreach (var candidateOrder in orders)
                        {
                            var orderEvalItems = cartEvalItems.Where(x => x.ShopId == candidateOrder.ShopId).ToList();
                            var subEval = EvaluateVoucherForItems(voucher, orderEvalItems, candidateOrder.ShippingFee, customerOrderCountAll, customerOrdersByShop, customerVoucherUsage, cartCatMap, emptyShopNames);
                            if (subEval.EligibleSubtotal > 0)
                            {
                                orderEligibleList.Add((candidateOrder, subEval.EligibleSubtotal));
                            }
                        }

                        if (orderEligibleList.Count == 0)
                        {
                            var fallbackOrders = voucher.ShopId == null ? orders : orders.Where(o => o.ShopId == voucher.ShopId).ToList();
                            orderEligibleList = fallbackOrders.Select(o => (o, o.SubTotal)).ToList();
                        }

                        var totalEligibleAcrossOrders = orderEligibleList.Sum(x => x.EligibleSub);
                        voucher.UsedCount += 1;
                        decimal remainingDiscount = discountAmount;

                        for (int i = 0; i < orderEligibleList.Count; i++)
                        {
                            var (o, orderEligibleSub) = orderEligibleList[i];
                            o.VoucherId = voucher.Id;

                            decimal orderDiscount = 0;
                            if (i == orderEligibleList.Count - 1)
                            {
                                orderDiscount = remainingDiscount;
                            }
                            else
                            {
                                decimal weight = totalEligibleAcrossOrders > 0 ? orderEligibleSub / totalEligibleAcrossOrders : 0;
                                orderDiscount = Math.Round(discountAmount * weight);
                                if (orderDiscount > remainingDiscount) orderDiscount = remainingDiscount;
                            }

                            o.DiscountAmount += orderDiscount;
                            if (voucher.ShopId.HasValue) o.ShopDiscountAmount += orderDiscount;
                            else o.PlatformDiscountAmount += orderDiscount;

                            remainingDiscount -= orderDiscount;
                        }
                    }
                }
            }
        }

        foreach (var o in orders)
        {
            if (o.ShopDiscountAmount > 0m && o.SubTotal > 0m)
            {
                var scale = Math.Max(0m, o.SubTotal - o.ShopDiscountAmount) / o.SubTotal;
                foreach (var item in o.Items)
                {
                    item.CommissionAmount = Math.Round(item.CommissionAmount * scale, 0);
                }
            }

            o.FinalAmount = o.SubTotal + o.ShippingFee - o.DiscountAmount;
            if (o.FinalAmount < 0) o.FinalAmount = 0;
            o.PaymentMethod = paymentMethod;
            o.PaymentStatus = "Pending";
        }

        var totalFinalAmt = orders.Sum(o => o.FinalAmount);
        var walletBalance = await _wallet.GetBalanceAsync(accountId.Value, cancellationToken);

        if (paymentMethod == "Wallet" && walletBalance < totalFinalAmt)
        {
            var vmBad = await BuildCheckoutVmAsync(accountId.Value, preferredAddressId: address?.Id, paymentMethod, req.VoucherCode, cancellationToken);
            vmBad.ErrorMessage = $"Số dư ví không đủ ({walletBalance:N0}đ) để thanh toán cho các đơn hàng ({totalFinalAmt:N0}đ). Vui lòng chọn phương thức kết hợp.";
            return View("Index", vmBad);
        }

        var remainingWallet = walletBalance;
        var vnPayAmt = 0m;
        var vnPayOrderCodes = new List<string>();
        var walletDeductions = new Dictionary<string, decimal>();

        foreach (var order in orders)
        {
            decimal orderWalletDeduct = 0m;
            decimal orderVnPayAmt = 0m;

            if (paymentMethod == "Wallet")
            {
                orderWalletDeduct = order.FinalAmount;
                order.PaymentStatus = "Paid";
            }
            else if (paymentMethod == "WalletCOD")
            {
                orderWalletDeduct = Math.Min(remainingWallet, order.FinalAmount);
                remainingWallet -= orderWalletDeduct;
                order.PaymentStatus = "Pending";
            }
            else if (paymentMethod == "WalletVNPay")
            {
                orderWalletDeduct = Math.Min(remainingWallet, order.FinalAmount);
                remainingWallet -= orderWalletDeduct;
                orderVnPayAmt = order.FinalAmount - orderWalletDeduct;

                if (orderVnPayAmt > 0)
                {
                    order.PaymentStatus = "AwaitingPayment";
                    vnPayAmt += orderVnPayAmt;
                    vnPayOrderCodes.Add(order.OrderCode);
                }
                else
                {
                    order.PaymentStatus = "Paid";
                }
            }
            else if (paymentMethod == "BankTransfer")
            {
                orderVnPayAmt = order.FinalAmount;
                order.PaymentStatus = "AwaitingPayment";
                vnPayAmt += orderVnPayAmt;
                vnPayOrderCodes.Add(order.OrderCode);
            }

            if (orderWalletDeduct > 0)
            {
                walletDeductions[order.OrderCode] = orderWalletDeduct;
            }
        }

        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            _db.Orders.AddRange(orders);
            foreach (var newOrd in orders)
            {
                _db.SystemNotifications.Add(new SystemNotification
                {
                    Title = $"Đơn hàng mới #{newOrd.OrderCode} ({newOrd.FinalAmount:N0}₫)",
                    Content = $"Gian hàng của bạn vừa nhận được đơn hàng mới #{newOrd.OrderCode} từ khách hàng {newOrd.RecipientName} ({newOrd.Items.Sum(i => i.Quantity)} sản phẩm, tổng thanh toán {newOrd.FinalAmount:N0}₫ - {newOrd.PaymentMethod}). Vui lòng kiểm tra và xác nhận đơn hàng.",
                    TargetType = "Account",
                    TargetAccountId = newOrd.ShopId,
                    SentByAdminId = accountId.Value,
                    CreatedAt = DateTime.UtcNow
                });
            }

            // Emit low-stock alert notifications (<= 5 remaining units) to Seller
            foreach (var cartItem in selectedItems)
            {
                var v = cartItem.Variant;
                var p = v?.Product;
                if (v == null || p == null) continue;
                int estRemaining = Math.Max(0, v.StockQuantity - v.ReservedStock - cartItem.Quantity);
                if (estRemaining <= 5)
                {
                    var varLbl = string.IsNullOrWhiteSpace(v.DisplayVariantName) ? (v.Sku ?? "Mặc định") : v.DisplayVariantName;
                    _db.SystemNotifications.Add(new SystemNotification
                    {
                        Title = $"⚠️ Sắp hết hàng: {p.Name} (Còn {estRemaining})",
                        Content = $"Phân loại [{varLbl}] của sản phẩm \"{p.Name}\" chỉ còn {estRemaining} sản phẩm khả dụng trong kho. Hãy nhập thêm tồn kho để tránh gián đoạn doanh số!",
                        TargetType = "Account",
                        TargetAccountId = p.ShopId,
                        SentByAdminId = accountId.Value,
                        CreatedAt = DateTime.UtcNow
                    });
                }
            }
            _db.CartItems.RemoveRange(selectedItems);
            await _db.SaveChangesAsync(cancellationToken);

            foreach (var kv in walletDeductions)
            {
                var (payOk, payMsg) = await _wallet.PayWithWalletAsync(
                    accountId.Value,
                    kv.Value,
                    kv.Key,
                    $"Thanh toán đơn hàng #{kv.Key}",
                    cancellationToken);

                if (!payOk)
                {
                    await tx.RollbackAsync(cancellationToken);
                    var vmBad = await BuildCheckoutVmAsync(accountId.Value, preferredAddressId: address?.Id, paymentMethod, req.VoucherCode, cancellationToken);
                    vmBad.ErrorMessage = $"Thanh toán ví lỗi cho đơn {kv.Key}: {payMsg}";
                    return View("Index", vmBad);
                }
            }

            foreach (var ord in orders)
            {
                if (ord.IsDigitalOrder && ord.PaymentStatus == "Paid")
                {
                    await CompleteAndDeliverDigitalOrderAsync(ord, cancellationToken);
                }
            }
            await _db.SaveChangesAsync(cancellationToken);

            if (selection.Initialized)
            {
                foreach (var removed in selectedItems)
                {
                    selection.DeselectedIds.Remove(removed.Id);
                }

                HttpContext.Session.SetJson(SessionCartSelectionKey, selection);
            }

            await tx.CommitAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(cancellationToken);
            _logger.LogError(ex, "Failed to place orders in Cart mode.");
            throw;
        }

        if (vnPayAmt > 0)
        {
            var txnRef = $"PAY-{string.Join("_", vnPayOrderCodes)}-{DateTime.Now:yyyyMMddHHmmss}";
            var orderInfo = $"Thanh toan cac don hang #{string.Join(", #", vnPayOrderCodes)}";
            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
            var scheme = Request.Scheme;
            var host = Request.Host.ToUriComponent();
            var callbackUrl = $"{scheme}://{host}/checkout/vnpay-return";
            var payUrl = _vnPay.CreatePaymentUrl(txnRef, vnPayAmt, orderInfo, ipAddress, customReturnUrl: callbackUrl);
            return Redirect(payUrl);
        }

        var orderCodes = orders
            .Select(o => o.OrderCode)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var codesQuery = Uri.EscapeDataString(string.Join(",", orderCodes));
        return Redirect($"/checkout/success?codes={codesQuery}");
        }
    }

    [HttpGet("vnpay-return")]
    public async Task<IActionResult> VnPayReturn(CancellationToken cancellationToken)
    {
        var accountId = GetSignedInAccountId();
        if (!accountId.HasValue)
        {
            return Redirect("/my-account");
        }

        if (!_vnPay.ValidateSignature(Request.Query))
        {
            TempData["CheckoutError"] = "Chữ ký VNPAY không hợp lệ.";
            return Redirect("/my-orders");
        }

        var data = _vnPay.ParseReturnData(Request.Query);

        if (!data.TxnRef.StartsWith("PAY-", StringComparison.OrdinalIgnoreCase))
        {
            TempData["CheckoutError"] = "Giao dịch không hợp lệ.";
            return Redirect("/my-orders");
        }

        var withoutPay = data.TxnRef[4..];
        var lastDashIndex = withoutPay.LastIndexOf('-');
        if (lastDashIndex <= 0)
        {
            TempData["CheckoutError"] = "Mã giao dịch không đúng định dạng.";
            return Redirect("/my-orders");
        }

        var codesPart = withoutPay[..lastDashIndex];
        var orderCodes = codesPart.Split('_').ToList();

        if (orderCodes.Count == 0)
        {
            TempData["CheckoutError"] = "Không tìm thấy mã đơn hàng.";
            return Redirect("/my-orders");
        }

        if (!data.IsSuccess)
        {
            var codesQuery = Uri.EscapeDataString(string.Join(",", orderCodes));
            return Redirect($"/checkout/payment-failed?codes={codesQuery}&vnp_ResponseCode={data.ResponseCode}");
        }

        var orders = await _db.Orders
            .Include(o => o.Items)
            .Where(o => orderCodes.Contains(o.OrderCode))
            .ToListAsync(cancellationToken);

        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            foreach (var order in orders)
            {
                if (order.PaymentStatus != "Paid")
                {
                    order.PaymentStatus = "Paid";
                    order.UpdatedAt = DateTime.UtcNow;

                    _db.OrderTrackingLogs.Add(new OrderTrackingLog
                    {
                        OrderId = order.Id,
                        Status = order.OrderStatus,
                        Note = $"Thanh toán VNPay thành công. Số tiền: {data.Amount:N0}đ. Mã GD VNPay: {data.TransactionNo}",
                        CreatedAt = DateTime.UtcNow
                    });

                    if (order.IsDigitalOrder)
                    {
                        await CompleteAndDeliverDigitalOrderAsync(order, cancellationToken);
                    }
                }
            }

            await _db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(cancellationToken);
            _logger.LogError(ex, "Failed to update order payment status on VNPay return.");
            TempData["CheckoutError"] = "Có lỗi xảy ra khi cập nhật trạng thái thanh toán.";
            return Redirect("/my-orders");
        }

        var successCodesQuery = Uri.EscapeDataString(string.Join(",", orderCodes));
        return Redirect($"/checkout/success?codes={successCodesQuery}");
    }

    [HttpGet("vnpay-ipn")]
    public async Task<IActionResult> VnPayIpn(CancellationToken cancellationToken)
    {
        if (!_vnPay.ValidateSignature(Request.Query))
        {
            _logger.LogWarning("Checkout IPN: invalid signature");
            return Ok(new { RspCode = "97", Message = "Invalid checksum" });
        }

        var data = _vnPay.ParseReturnData(Request.Query);

        if (!data.TxnRef.StartsWith("PAY-", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Checkout IPN: invalid txnRef prefix");
            return Ok(new { RspCode = "01", Message = "Invalid TxnRef format" });
        }

        var withoutPay = data.TxnRef[4..];
        var lastDashIndex = withoutPay.LastIndexOf('-');
        if (lastDashIndex <= 0)
        {
            _logger.LogWarning("Checkout IPN: invalid txnRef format");
            return Ok(new { RspCode = "01", Message = "Invalid TxnRef format" });
        }

        var codesPart = withoutPay[..lastDashIndex];
        var orderCodes = codesPart.Split('_').ToList();

        if (orderCodes.Count == 0)
        {
            _logger.LogWarning("Checkout IPN: empty order codes");
            return Ok(new { RspCode = "01", Message = "Order not found" });
        }

        var orders = await _db.Orders
            .Include(o => o.Items)
            .Where(o => orderCodes.Contains(o.OrderCode))
            .ToListAsync(cancellationToken);

        if (orders.Count == 0)
        {
            _logger.LogWarning("Checkout IPN: orders not found in DB");
            return Ok(new { RspCode = "01", Message = "Order not found" });
        }

        if (!data.IsSuccess)
        {
            _logger.LogInformation("Checkout IPN: payment not success. TxnRef={TxnRef}", data.TxnRef);
            return Ok(new { RspCode = "00", Message = "Confirm success (not paid)" });
        }

        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            foreach (var order in orders)
            {
                if (order.PaymentStatus != "Paid")
                {
                    order.PaymentStatus = "Paid";
                    order.UpdatedAt = DateTime.UtcNow;

                    _db.OrderTrackingLogs.Add(new OrderTrackingLog
                    {
                        OrderId = order.Id,
                        Status = order.OrderStatus,
                        Note = $"Thanh toán VNPay thành công (IPN). Số tiền: {data.Amount:N0}đ. Mã GD VNPay: {data.TransactionNo}",
                        CreatedAt = DateTime.UtcNow
                    });

                    if (order.IsDigitalOrder)
                    {
                        await CompleteAndDeliverDigitalOrderAsync(order, cancellationToken);
                    }
                }
            }

            await _db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(cancellationToken);
            _logger.LogError(ex, "Failed to update order payment status on VNPay IPN.");
            return Ok(new { RspCode = "99", Message = "Internal error" });
        }

        return Ok(new { RspCode = "00", Message = "Confirm success" });
    }

    [HttpGet("vnpay-retry")]
    public async Task<IActionResult> VnPayRetry([FromQuery] string codes, CancellationToken cancellationToken)
    {
        var accountId = GetSignedInAccountId();
        if (!accountId.HasValue)
        {
            return Redirect("/my-account");
        }

        if (string.IsNullOrWhiteSpace(codes))
        {
            TempData["CheckoutError"] = "Mã đơn hàng không hợp lệ.";
            return Redirect("/my-orders");
        }

        var orderCodes = codes.Split(',').Select(c => c.Trim()).ToList();
        var orders = await _db.Orders
            .Where(o => orderCodes.Contains(o.OrderCode) && o.CustomerId == accountId.Value)
            .ToListAsync(cancellationToken);

        if (orders.Count == 0)
        {
            TempData["CheckoutError"] = "Không tìm thấy đơn hàng.";
            return Redirect("/my-orders");
        }

        var vnPayAmt = 0m;
        var vnPayOrderCodes = new List<string>();

        foreach (var order in orders)
        {
            if (order.PaymentStatus == "AwaitingPayment")
            {
                var wallet = await _db.Wallets.FirstOrDefaultAsync(w => w.UserId == accountId.Value, cancellationToken);
                var walletDeduction = 0m;
                if (wallet != null)
                {
                    var totalDeducted = await _db.WalletTransactions
                        .Where(t => t.WalletId == wallet.WalletId && t.ReferenceId == order.OrderCode && t.TransactionType == WalletService.TypePayment)
                        .Select(t => t.Amount)
                        .SumAsync(cancellationToken);

                    walletDeduction = Math.Abs(totalDeducted);
                }

                var remainingAmt = order.FinalAmount - walletDeduction;
                if (remainingAmt > 0)
                {
                    vnPayAmt += remainingAmt;
                    vnPayOrderCodes.Add(order.OrderCode);
                }
            }
        }

        if (vnPayAmt <= 0)
        {
            TempData["CheckoutError"] = "Đơn hàng đã được thanh toán hoặc không cần thanh toán qua VNPay.";
            return Redirect("/my-orders");
        }

        var txnRef = $"PAY-{string.Join("_", vnPayOrderCodes)}-{DateTime.Now:yyyyMMddHHmmss}";
        var orderInfo = $"Thanh toan lai cac don hang #{string.Join(", #", vnPayOrderCodes)}";
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
        var scheme = Request.Scheme;
        var host = Request.Host.ToUriComponent();
        var callbackUrl = $"{scheme}://{host}/checkout/vnpay-return";
        var payUrl = _vnPay.CreatePaymentUrl(txnRef, vnPayAmt, orderInfo, ipAddress, customReturnUrl: callbackUrl);
        return Redirect(payUrl);
    }

    [HttpGet("payment-failed")]
    public async Task<IActionResult> PaymentFailed([FromQuery] string codes, CancellationToken cancellationToken)
    {
        var accountId = GetSignedInAccountId();
        if (!accountId.HasValue)
        {
            return Redirect("/my-account");
        }

        if (string.IsNullOrWhiteSpace(codes))
        {
            return Redirect("/my-orders");
        }

        var orderCodes = codes.Split(',').Select(c => c.Trim()).ToList();
        var orders = await _db.Orders
            .Where(o => orderCodes.Contains(o.OrderCode) && o.CustomerId == accountId.Value)
            .ToListAsync(cancellationToken);

        if (orders.Count == 0)
        {
            return Redirect("/my-orders");
        }

        var earliestOrderTime = orders.Min(o => o.CreatedAt);
        var deadline = earliestOrderTime.AddMinutes(15);

        var responseCode = Request.Query["vnp_ResponseCode"].ToString();
        if (string.IsNullOrWhiteSpace(responseCode)) responseCode = "99";

        var vm = new VnPayFailedVm
        {
            OrderCodes = orderCodes,
            PaymentDeadlineUtc = deadline,
            ResponseCode = responseCode,
            Message = "Giao dịch không thành công hoặc đã bị hủy từ phía khách hàng."
        };

        return View(vm);
    }
}
