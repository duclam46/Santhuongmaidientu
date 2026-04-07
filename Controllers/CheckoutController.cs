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
        ILogger<CheckoutController> logger)
    {
        _db = db;
        _ghn = ghn;
        _ghnOptions = ghnOptions.Value;
        _logger = logger;
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

    private Task<CheckoutPageVm> BuildCheckoutVmAsync(int accountId, int? preferredAddressId, string? paymentMethod, CancellationToken cancellationToken)
        => BuildCheckoutVmAsync(accountId, preferredAddressId, paymentMethod, buyNowVariantId: null, buyNowQuantity: null, cancellationToken);

    private async Task<CheckoutPageVm> BuildCheckoutVmAsync(int accountId, int? preferredAddressId, string? paymentMethod, int? buyNowVariantId, int? buyNowQuantity, CancellationToken cancellationToken)
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
                ShopId = v.Product.ShopId,
                ProductName = v.Product.Name,
                ProductSlug = v.Product.Slug,
                ImageUrl = imageUrl,
                Color = v.Color?.Name,
                Size = v.Size?.Name,
                UnitPrice = unitPrice,
                Quantity = qty,
                LineTotal = lineTotal
            });

            vm.SelectedItemCount = qty;
            vm.SelectedSubTotal = lineTotal;

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

            vm.FinalAmount = vm.SelectedSubTotal + vm.ShippingFee;
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
        var nowUtc = DateTime.UtcNow;

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
                ShopId = p.ShopId,
                ProductName = p.Name,
                ProductSlug = p.Slug,
                ImageUrl = imageUrl,
                Color = v.Color?.Name,
                Size = v.Size?.Name,
                UnitPrice = unitPrice,
                Quantity = item.Quantity,
                LineTotal = lineTotal
            });
        }

        vm.SelectedItemCount = vm.Items.Sum(i => i.Quantity);
        vm.SelectedSubTotal = vm.Items.Sum(i => i.LineTotal);

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

        vm.FinalAmount = vm.SelectedSubTotal + vm.ShippingFee;

        return vm;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index([FromQuery] int? addressId, [FromQuery] int? variantId, [FromQuery] int? quantity, CancellationToken cancellationToken)
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
            cancellationToken);

        if (vmFinal.Address is null)
        {
            vmFinal.ErrorMessage = "Vui lòng thêm địa chỉ giao hàng trước khi thanh toán.";
        }

        return View(vmFinal);
    }

    [HttpGet("summary")]
    public async Task<IActionResult> Summary([FromQuery] int? addressId, [FromQuery] int? variantId, [FromQuery] int? quantity, CancellationToken cancellationToken)
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
            cancellationToken);

        return Ok(new
        {
            isBuyNow = vm.IsBuyNow,
            buyNowVariantId = vm.BuyNowVariantId,
            buyNowQuantity = vm.BuyNowQuantity,
            selectedItemCount = vm.SelectedItemCount,
            selectedSubTotal = vm.SelectedSubTotal,
            shippingFee = vm.ShippingFee,
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

        if (address is null)
        {
            var vmMissingAddress = await BuildCheckoutVmAsync(accountId.Value, preferredAddressId: null, paymentMethod, cancellationToken);
            vmMissingAddress.ErrorMessage = "Địa chỉ không hợp lệ. Vui lòng chọn lại.";
            return View("Index", vmMissingAddress);
        }

        var nowUtc = DateTime.UtcNow;

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
                var vmBad = await BuildCheckoutVmAsync(accountId.Value, preferredAddressId: address.Id, paymentMethod, cancellationToken);
                vmBad.ErrorMessage = "Sản phẩm không hợp lệ hoặc đã ngừng bán.";
                return View("Index", vmBad);
            }

            if (v.StockQuantity <= 0)
            {
                var vmOos = await BuildCheckoutVmAsync(accountId.Value, preferredAddressId: address.Id, paymentMethod, cancellationToken);
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
                var vmNoSender = await BuildCheckoutVmAsync(accountId.Value, preferredAddressId: address.Id, paymentMethod, cancellationToken);
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
                var vmFeeFail = await BuildCheckoutVmAsync(accountId.Value, preferredAddressId: address.Id, paymentMethod, cancellationToken);
                vmFeeFail.ErrorMessage = ex.Message;
                return View("Index", vmFeeFail);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "GHN fee request failed (POST /checkout buy-now). ShopId={ShopId}", _ghnOptions.ShopId);
                var vmFeeFail = await BuildCheckoutVmAsync(accountId.Value, preferredAddressId: address.Id, paymentMethod, cancellationToken);
                vmFeeFail.ErrorMessage = "Không tính được phí vận chuyển GHN. Vui lòng kiểm tra ShopId/Token và địa chỉ (District/Ward), hoặc thử lại.";
                return View("Index", vmFeeFail);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error while calculating GHN fee (POST /checkout buy-now). ShopId={ShopId}", _ghnOptions.ShopId);
                var vmFeeFail = await BuildCheckoutVmAsync(accountId.Value, preferredAddressId: address.Id, paymentMethod, cancellationToken);
                vmFeeFail.ErrorMessage = "Không tính được phí vận chuyển GHN. Vui lòng thử lại.";
                return View("Index", vmFeeFail);
            }

            var unitPrice = GetEffectiveUnitPrice(v, nowUtc);
            var subTotal = unitPrice * buyQty;

            var order = new Order
            {
                CustomerId = accountId.Value,
                ShopId = v.Product.ShopId,
                OrderCode = GenerateOrderCode(nowUtc),
                SubTotal = subTotal,
                ShippingFee = shippingFee,
                DiscountAmount = 0m,
                FinalAmount = subTotal + shippingFee,
                OrderStatus = "Pending",
                PaymentMethod = paymentMethod,
                PaymentStatus = "Pending",
                RecipientName = address.FullName,
                RecipientPhone = address.PhoneNumber,
                ShippingAddress = ComposeAddress(address),
                ShippingProvinceId = address.ProvinceId,
                ShippingDistrictId = address.DistrictId,
                ShippingWardCode = address.WardCode,
                ShippingStreetAddress = address.StreetAddress,
                CreatedAt = nowUtc,
                UpdatedAt = nowUtc,
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
            return Redirect("/my-orders");
        }

        // Default: checkout selected items from cart
        var cartId = await _db.Carts
            .AsNoTracking()
            .Where(c => c.AccountId == accountId.Value)
            .Select(c => (int?)c.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (!cartId.HasValue)
        {
            var vmEmpty = await BuildCheckoutVmAsync(accountId.Value, preferredAddressId: address.Id, paymentMethod, cancellationToken);
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
            var vmNone = await BuildCheckoutVmAsync(accountId.Value, preferredAddressId: address.Id, paymentMethod, cancellationToken);
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
                var vmNoSender = await BuildCheckoutVmAsync(accountId.Value, preferredAddressId: address.Id, paymentMethod, cancellationToken);
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
                var vmFeeFail = await BuildCheckoutVmAsync(accountId.Value, preferredAddressId: address.Id, paymentMethod, cancellationToken);
                vmFeeFail.ErrorMessage = ex.Message;
                return View("Index", vmFeeFail);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "GHN fee request failed (POST /checkout). ShopId={ShopId}", _ghnOptions.ShopId);
                var vmFeeFail = await BuildCheckoutVmAsync(accountId.Value, preferredAddressId: address.Id, paymentMethod, cancellationToken);
                vmFeeFail.ErrorMessage = "Không tính được phí vận chuyển GHN. Vui lòng kiểm tra ShopId/Token và địa chỉ (District/Ward), hoặc thử lại.";
                return View("Index", vmFeeFail);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error while calculating GHN fee (POST /checkout). ShopId={ShopId}", _ghnOptions.ShopId);
                var vmFeeFail = await BuildCheckoutVmAsync(accountId.Value, preferredAddressId: address.Id, paymentMethod, cancellationToken);
                vmFeeFail.ErrorMessage = "Không tính được phí vận chuyển GHN. Vui lòng thử lại.";
                return View("Index", vmFeeFail);
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
                RecipientName = address.FullName,
                RecipientPhone = address.PhoneNumber,
                ShippingAddress = ComposeAddress(address),
                ShippingProvinceId = address.ProvinceId,
                ShippingDistrictId = address.DistrictId,
                ShippingWardCode = address.WardCode,
                ShippingStreetAddress = address.StreetAddress,
                CreatedAt = nowUtc,
                UpdatedAt = nowUtc,
            };

            foreach (var item in grp)
            {
                var v = item.Variant!;
                var p = v.Product!;

                var unitPrice = GetEffectiveUnitPrice(v, nowUtc);
                var lineTotal = unitPrice * item.Quantity;
                subTotal += lineTotal;

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
            order.FinalAmount = subTotal + order.ShippingFee - order.DiscountAmount;
            orders.Add(order);
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

        return Redirect("/my-orders");
    }
}
