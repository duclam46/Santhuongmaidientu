using System.Security.Claims;
using System.Diagnostics;
using DOAN_BANHANG.Models;
using DOAN_BANHANG.Services;
using DOAN_BANHANG.ViewModels;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using DOAN_BANHANG.Helpers;

namespace DOAN_BANHANG.Controllers;

public class AccountController : Controller
{
    private readonly DataContext _db;
    private readonly IPasswordHasher<Account> _passwordHasher;
    private readonly IWebHostEnvironment _env;
    private readonly GhnClient _ghn;
    private readonly GhnOptions _ghnOptions;
    private readonly WalletService _walletService;

    [ActivatorUtilitiesConstructor]
    public AccountController(DataContext db, IPasswordHasher<Account> passwordHasher, IWebHostEnvironment env, GhnClient ghn, IOptions<GhnOptions> ghnOptions, WalletService walletService)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _env = env;
        _ghn = ghn;
        _ghnOptions = ghnOptions.Value;
        _walletService = walletService;
    }

    private static UserAddress? PickBestSenderAddress(IEnumerable<UserAddress> addrs)
        => addrs
            .OrderByDescending(x => x.AddressType == AddressType.Shop)
            .ThenByDescending(x => x.IsDefault)
            .ThenByDescending(x => x.UpdatedAt)
            .FirstOrDefault();

    private static string? SanitizeCustomerTrackingNote(string? note)
    {
        if (string.IsNullOrWhiteSpace(note)) return note;

        // Hide simulator metadata from customer UI.
        // Format: SIM|i=3|e=arrive - <human note>
        if (note.TrimStart().StartsWith("SIM|", StringComparison.OrdinalIgnoreCase))
        {
            var separators = new[] { " - ", " – ", " — " }; // hyphen, en dash, em dash
            var splitIdx = -1;
            var sepLen = 0;
            foreach (var sep in separators)
            {
                var i = note.IndexOf(sep, StringComparison.Ordinal);
                if (i >= 0)
                {
                    splitIdx = i;
                    sepLen = sep.Length;
                    break;
                }
            }

            if (splitIdx >= 0 && splitIdx + sepLen < note.Length)
            {
                note = note[(splitIdx + sepLen)..].Trim();
            }
        }

        // Remove seller-internal pickup shift info from customer UI.
        const string marker = "Ca lấy:";
        var idx = note.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return note;

        // Also trim a preceding punctuation/space if present (e.g. ". Ca lấy: ...").
        var left = note[..idx].TrimEnd();
        if (left.EndsWith(".", StringComparison.Ordinal)) left = left[..^1].TrimEnd();
        return string.IsNullOrWhiteSpace(left) ? null : left;
    }

    private static string ResolveEffectiveOrderStatus(string? rawOrderStatus, bool hasSimPickupDepart)
    {
        var s = (rawOrderStatus ?? string.Empty).Trim();
        var norm = s.Replace(" ", string.Empty).ToLowerInvariant();

        // Never override terminal / final states.
        var isTerminal = norm is (
            "cancelled" or "canceled" or "cancel" or "đãhủy" or "dahuy" or
            "returned" or "return" or "refund" or "returnrequest" or "returning" or "trahang" or "trảhàng" or
            "completed" or "success" or "hoàntất" or "hoantat" or
            "delivered" or "deliverysuccess" or "dagiao" or "đãgiao");

        if (isTerminal) return rawOrderStatus ?? string.Empty;

        // If simulator indicates pickup depart, treat as in-transit.
        if (hasSimPickupDepart) return "Shipping";

        return rawOrderStatus ?? string.Empty;
    }

    private static bool TryExtractSimShipper(string? note, out string? shipperName, out string? shipperPhone)
    {
        shipperName = null;
        shipperPhone = null;

        if (string.IsNullOrWhiteSpace(note)) return false;

        // Head: SIM|i=...|e=...|shipper=...|phone=...
        var separators = new[] { " - ", " – ", " — " }; // hyphen, en dash, em dash
        var splitIdx = -1;
        foreach (var sep in separators)
        {
            var i = note.IndexOf(sep, StringComparison.Ordinal);
            if (i >= 0)
            {
                splitIdx = i;
                break;
            }
        }

        var head = (splitIdx >= 0 ? note[..splitIdx] : note).Trim();
        if (!head.StartsWith("SIM|", StringComparison.OrdinalIgnoreCase)) return false;

        var parts = head.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2) return false;

        foreach (var part in parts)
        {
            if (part.Equals("SIM", StringComparison.OrdinalIgnoreCase)) continue;
            var eq = part.IndexOf('=');
            if (eq <= 0) continue;

            var key = part[..eq].Trim();
            var valRaw = part[(eq + 1)..].Trim();
            if (string.IsNullOrWhiteSpace(valRaw)) continue;

            string val;
            try
            {
                val = Uri.UnescapeDataString(valRaw);
            }
            catch
            {
                val = valRaw;
            }

            if (key.Equals("shipper", StringComparison.OrdinalIgnoreCase))
            {
                shipperName = val;
            }
            else if (key.Equals("phone", StringComparison.OrdinalIgnoreCase))
            {
                shipperPhone = val;
            }
        }

        return !string.IsNullOrWhiteSpace(shipperName) || !string.IsNullOrWhiteSpace(shipperPhone);
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? tab, int? reviewsPage, CancellationToken cancellationToken)
    {
        if (!(User?.Identity?.IsAuthenticated ?? false))
        {
            return View(new MyAccountPageVm { IsSignedIn = false });
        }

        var idRaw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(idRaw, out var accountId) || accountId <= 0)
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return View(new MyAccountPageVm { IsSignedIn = false });
        }

        // Auto-cancel any expired bank transfer orders for this customer first
        await CancelExpiredOrdersForCustomerAsync(accountId, cancellationToken);

        var account = await _db.Accounts
            .Include(a => a.Profile)
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == accountId && !a.IsDeleted, cancellationToken);

        if (account is null)
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return View(new MyAccountPageVm { IsSignedIn = false });
        }

        var addresses = await _db.UserAddresses
            .AsNoTracking()
            .Where(x => x.AccountId == account.Id)
            .OrderByDescending(x => x.IsDefault)
            .ThenByDescending(x => x.UpdatedAt)
            .ToListAsync(cancellationToken);

        var orders = await _db.Orders
            .AsNoTracking()
            .Where(o => o.CustomerId == account.Id)
            .OrderByDescending(o => o.CreatedAt)
            .Select(o => new OrderSummaryVm
            {
                Id = o.Id,
                OrderCode = o.OrderCode,
                OrderStatus = ResolveEffectiveOrderStatus(
                    o.OrderStatus,
                    _db.OrderTrackingLogs.Any(l => l.OrderId == o.Id
                        && l.Note != null
                        && (EF.Functions.Like(l.Note, "SIM|i=1|e=depart%")
                            || EF.Functions.Like(l.Note, "SIM|i=1|e=leave%")))),
                PaymentStatus = o.PaymentStatus,
                PaymentMethod = o.PaymentMethod,
                PaymentDeadlineUtc = o.CreatedAt.AddMinutes(15),
                FinalAmount = o.FinalAmount,
                ShippingFee = o.ShippingFee,
                ItemCount = o.Items.Count,
                FirstProductSlug = o.Items.OrderBy(i => i.Id).Select(i => i.Variant!.Product!.Slug).FirstOrDefault() ?? string.Empty,
                CreatedAt = o.CreatedAt,
                UpdatedAt = o.UpdatedAt,
                HasReviewed = _db.ProductReviews.Any(r => r.AccountId == o.CustomerId
                    && o.Items.Any(i => i.Variant != null && i.Variant.ProductId == r.ProductId)),
                FirstProductId = o.Items.OrderBy(i => i.Id).Select(i => i.Variant != null ? i.Variant.ProductId : 0).FirstOrDefault(),
                FirstProductName = o.Items.OrderBy(i => i.Id).Select(i => i.ProductName).FirstOrDefault() ?? string.Empty,
                FirstProductImageUrl = string.Empty,
                FirstProductVariantName = o.Items.OrderBy(i => i.Id).Select(i =>
                    !string.IsNullOrEmpty(i.VariantDescription)
                        ? i.VariantDescription
                        : (i.ColorName != null ? i.ColorName + (i.SizeName != null ? " - " + i.SizeName : "") : i.SizeName ?? string.Empty))
                    .FirstOrDefault(),
                IsReceivedByCustomer = _db.OrderTrackingLogs.Any(l => l.OrderId == o.Id && l.Note == "Khách hàng xác nhận đã nhận hàng"),
                CanReturn = !o.Items.Any(i => i.Variant != null && i.Variant.Product != null && !i.Variant.Product.IsReturnable)
            })
            .ToListAsync(cancellationToken);

        // Post-process FirstProductImageUrl in memory to avoid complex SQL translation on subquery collections
        var firstProductIds = orders.Select(o => o.FirstProductId).Where(id => id > 0).Distinct().ToList();
        if (firstProductIds.Count > 0)
        {
            var productImages = await _db.ProductImages
                .AsNoTracking()
                .Where(img => firstProductIds.Contains(img.ProductId))
                .OrderByDescending(img => img.IsPrimary)
                .ThenBy(img => img.Id)
                .Select(img => new { img.ProductId, img.ImageUrl })
                .ToListAsync(cancellationToken);

            var productImageUrlMap = productImages
                .GroupBy(img => img.ProductId)
                .ToDictionary(g => g.Key, g => g.First().ImageUrl);

            foreach (var order in orders)
            {
                if (productImageUrlMap.TryGetValue(order.FirstProductId, out var imageUrl))
                {
                    order.FirstProductImageUrl = imageUrl;
                }
                else
                {
                    order.FirstProductImageUrl = "/assets/images/placeholder.png";
                }
            }
        }

        const int reviewsPageSize = 5;
        var effectiveReviewsPage = Math.Max(1, reviewsPage ?? 1);

        var baseReviewsQuery = _db.ProductReviews
            .AsNoTracking()
            .Where(r => r.AccountId == account.Id)
            .Where(r => r.Product != null && r.Product.Status == "Active" && !r.Product.IsDeleted)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new ReviewSummaryVm
            {
                Id = r.Id,
                ProductId = r.ProductId,
                ProductName = r.Product != null ? r.Product.Name : string.Empty,
                ProductSlug = r.Product != null ? r.Product.Slug : string.Empty,
                ProductImageUrl = r.Product != null
                    ? r.Product.Images
                        .OrderByDescending(i => i.IsPrimary)
                        .ThenBy(i => i.SortOrder)
                        .ThenBy(i => i.Id)
                        .Select(i => i.ImageUrl)
                        .FirstOrDefault()
                    : null,
                Rating = r.Rating,
                Comment = r.Comment,
                CreatedAt = r.CreatedAt
            });

        var reviewsTotalCount = await baseReviewsQuery.CountAsync(cancellationToken);
        var reviews = await baseReviewsQuery
            .Skip((effectiveReviewsPage - 1) * reviewsPageSize)
            .Take(reviewsPageSize)
            .ToListAsync(cancellationToken);

        var recentReviews = await baseReviewsQuery
            .Take(3)
            .ToListAsync(cancellationToken);

        var vm = new MyAccountVm
        {
            AccountId = account.Id,
            Email = account.Email,
            HasPassword = !string.IsNullOrWhiteSpace(account.PasswordHash),
            Role = account.Role,
            Status = account.Status,
            AuthProvider = account.AuthProvider,
            CreatedAt = account.CreatedAt,
            LastLoginAt = account.LastLoginAt,
            FullName = account.Profile?.FullName,
            PhoneNumber = account.Profile?.PhoneNumber,
            Address = account.Profile?.Address,
            AvatarUrl = account.Profile?.AvatarUrl,
            Addresses = addresses
                .Select(a => new UserAddressVm
                {
                    Id = a.Id,
                    FullName = a.FullName,
                    PhoneNumber = a.PhoneNumber,
                    AddressType = (int)a.AddressType,
                    ProvinceId = a.ProvinceId,
                    ProvinceName = a.ProvinceName,
                    DistrictId = a.DistrictId,
                    DistrictName = a.DistrictName,
                    WardCode = a.WardCode,
                    WardName = a.WardName,
                    StreetAddress = a.StreetAddress,
                    IsDefault = a.IsDefault
                })
                .ToList(),
            Orders = orders,
            Reviews = reviews,
            RecentReviews = recentReviews,
            ReviewsTotalCount = reviewsTotalCount,
            ReviewsPage = effectiveReviewsPage,
            ReviewsPageSize = reviewsPageSize
        };

        ViewData["ActiveAccountTab"] = tab;
        return View(new MyAccountPageVm { IsSignedIn = true, Account = vm });
    }

    [HttpGet]
    public async Task<IActionResult> Orders(CancellationToken cancellationToken)
    {
        if (!(User?.Identity?.IsAuthenticated ?? false))
        {
            return View("Orders", new MyAccountPageVm { IsSignedIn = false });
        }

        var idRaw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(idRaw, out var accountId) || accountId <= 0)
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return View("Orders", new MyAccountPageVm { IsSignedIn = false });
        }

        // Auto-cancel any expired bank transfer orders for this customer first
        await CancelExpiredOrdersForCustomerAsync(accountId, cancellationToken);

        var account = await _db.Accounts
            .Include(a => a.Profile)
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == accountId && !a.IsDeleted, cancellationToken);

        if (account is null)
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return View("Orders", new MyAccountPageVm { IsSignedIn = false });
        }

        var orders = await _db.Orders
            .AsNoTracking()
            .Where(o => o.CustomerId == account.Id)
            .OrderByDescending(o => o.CreatedAt)
            .Select(o => new OrderSummaryVm
            {
                Id = o.Id,
                OrderCode = o.OrderCode,
                OrderStatus = ResolveEffectiveOrderStatus(
                    o.OrderStatus,
                    _db.OrderTrackingLogs.Any(l => l.OrderId == o.Id
                        && l.Note != null
                        && (EF.Functions.Like(l.Note, "SIM|i=1|e=depart%")
                            || EF.Functions.Like(l.Note, "SIM|i=1|e=leave%")))),
                PaymentStatus = o.PaymentStatus,
                PaymentMethod = o.PaymentMethod,
                PaymentDeadlineUtc = o.CreatedAt.AddMinutes(15),
                FinalAmount = o.FinalAmount,
                ShippingFee = o.ShippingFee,
                ItemCount = o.Items.Count,
                FirstProductSlug = o.Items.OrderBy(i => i.Id).Select(i => i.Variant!.Product!.Slug).FirstOrDefault() ?? string.Empty,
                CreatedAt = o.CreatedAt,
                UpdatedAt = o.UpdatedAt,
                HasReviewed = _db.ProductReviews.Any(r => r.AccountId == o.CustomerId
                    && o.Items.Any(i => i.Variant != null && i.Variant.ProductId == r.ProductId)),
                FirstProductId = o.Items.OrderBy(i => i.Id).Select(i => i.Variant != null ? i.Variant.ProductId : 0).FirstOrDefault(),
                FirstProductName = o.Items.OrderBy(i => i.Id).Select(i => i.ProductName).FirstOrDefault() ?? string.Empty,
                FirstProductImageUrl = string.Empty,
                FirstProductVariantName = o.Items.OrderBy(i => i.Id).Select(i =>
                    !string.IsNullOrEmpty(i.VariantDescription)
                        ? i.VariantDescription
                        : (i.ColorName != null ? i.ColorName + (i.SizeName != null ? " - " + i.SizeName : "") : i.SizeName ?? string.Empty))
                    .FirstOrDefault(),
                IsReceivedByCustomer = _db.OrderTrackingLogs.Any(l => l.OrderId == o.Id && l.Note == "Khách hàng xác nhận đã nhận hàng"),
                CanReturn = !o.Items.Any(i => i.Variant != null && i.Variant.Product != null && !i.Variant.Product.IsReturnable)
            })
            .ToListAsync(cancellationToken);

        // Post-process FirstProductImageUrl in memory to avoid complex SQL translation on subquery collections
        var firstProductIds = orders.Select(o => o.FirstProductId).Where(id => id > 0).Distinct().ToList();
        if (firstProductIds.Count > 0)
        {
            var productImages = await _db.ProductImages
                .AsNoTracking()
                .Where(img => firstProductIds.Contains(img.ProductId))
                .OrderByDescending(img => img.IsPrimary)
                .ThenBy(img => img.Id)
                .Select(img => new { img.ProductId, img.ImageUrl })
                .ToListAsync(cancellationToken);

            var productImageUrlMap = productImages
                .GroupBy(img => img.ProductId)
                .ToDictionary(g => g.Key, g => g.First().ImageUrl);

            foreach (var order in orders)
            {
                if (productImageUrlMap.TryGetValue(order.FirstProductId, out var imageUrl))
                {
                    order.FirstProductImageUrl = imageUrl;
                }
                else
                {
                    order.FirstProductImageUrl = "/assets/images/placeholder.png";
                }
            }
        }

        var vm = new MyAccountVm
        {
            AccountId = account.Id,
            Email = account.Email,
            HasPassword = !string.IsNullOrWhiteSpace(account.PasswordHash),
            Role = account.Role,
            Status = account.Status,
            AuthProvider = account.AuthProvider,
            CreatedAt = account.CreatedAt,
            LastLoginAt = account.LastLoginAt,
            FullName = account.Profile?.FullName,
            PhoneNumber = account.Profile?.PhoneNumber,
            Address = account.Profile?.Address,
            AvatarUrl = account.Profile?.AvatarUrl,
            Orders = orders
        };

        return View("Orders", new MyAccountPageVm { IsSignedIn = true, Account = vm });
    }

    public sealed record CancelOrderRequest(int Id, string? ReturnUrl);

    private static bool IsCustomerCancelableStatus(string? rawStatus)
    {
        var s = (rawStatus ?? string.Empty).Trim();
        var norm = s.Replace(" ", string.Empty).ToLowerInvariant();

        return norm switch
        {
            "pendingconfirmation" or "waitconfirm" or "waitingconfirm" or "confirming" or "pending" or "confirmationpending" or "processing" or "inprocess" or "inprocessing" or "chờxácnhận" or "choxacnhan" => true,
            "awaitingpickup" or "waitpickup" or "waitingpickup" or "readytopick" or "pickup" or "chờlấyhàng" or "cholayhang" or "confirmed" or "accepted" or "đãxácnhận" or "daxacnhan" => true,
            _ => false
        };
    }

    [HttpPost("/my-orders/cancel")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelOrder([FromForm] CancelOrderRequest req, CancellationToken cancellationToken)
    {
        var fallbackReturnUrl = Url.RouteUrl("my_orders") ?? "/my-orders";
        var returnUrl = !string.IsNullOrWhiteSpace(req.ReturnUrl) && Url.IsLocalUrl(req.ReturnUrl)
            ? req.ReturnUrl!
            : fallbackReturnUrl;

        if (!(User?.Identity?.IsAuthenticated ?? false))
        {
            return Redirect(returnUrl);
        }

        var idRaw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(idRaw, out var accountId) || accountId <= 0)
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Redirect(returnUrl);
        }

        if (req.Id <= 0)
        {
            TempData["AccountError"] = "Đơn hàng không hợp lệ.";
            return Redirect(returnUrl);
        }

        var order = await _db.Orders
            .FirstOrDefaultAsync(o => o.Id == req.Id && o.CustomerId == accountId, cancellationToken);

        if (order is null)
        {
            TempData["AccountError"] = "Không tìm thấy đơn hàng.";
            return Redirect(returnUrl);
        }

        if (!IsCustomerCancelableStatus(order.OrderStatus))
        {
            TempData["AccountError"] = "Chỉ có thể hủy khi đơn đang chờ xác nhận hoặc chờ lấy hàng.";
            return Redirect(returnUrl);
        }

        if (!string.IsNullOrWhiteSpace(order.TrackingCode))
        {
            try
            {
                var results = await _ghn.CancelShippingOrdersAsync(new[] { order.TrackingCode }, shopId: null, cancellationToken);
                var r = results.FirstOrDefault(x => string.Equals(x.OrderCode, order.TrackingCode, StringComparison.OrdinalIgnoreCase));
                if (r is null)
                {
                    TempData["AccountError"] = "GHN không trả về kết quả hủy vận đơn.";
                    return Redirect(returnUrl);
                }

                if (!r.Result)
                {
                    TempData["AccountError"] = string.IsNullOrWhiteSpace(r.Message)
                        ? "GHN từ chối hủy vận đơn (có thể đã lấy hàng/đang giao)."
                        : $"GHN: {r.Message}";
                    return Redirect(returnUrl);
                }
            }
            catch (Exception ex)
            {
                TempData["AccountError"] = $"Hủy vận đơn GHN thất bại: {ex.Message}";
                return Redirect(returnUrl);
            }
        }

        order.OrderStatus = "Cancelled";
        if (order.PaymentStatus == "AwaitingPayment")
        {
            order.PaymentStatus = "Expired";
        }
        order.UpdatedAt = DateTime.UtcNow;

        _db.OrderTrackingLogs.Add(new OrderTrackingLog
        {
            OrderId = order.Id,
            Status = order.OrderStatus,
            Note = "Người mua hủy đơn hàng",
            CreatedAt = DateTime.UtcNow
        });

        _db.SystemNotifications.Add(new SystemNotification
        {
            Title = $"Khách hàng đã hủy đơn #{order.OrderCode}",
            Content = $"Khách hàng {order.RecipientName} đã hủy đơn hàng #{order.OrderCode} (Giá trị: {order.FinalAmount:N0}₫).",
            TargetType = "Account",
            TargetAccountId = order.ShopId,
            SentByAdminId = accountId,
            CreatedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync(cancellationToken);

        // Hoàn tiền ví linh hoạt theo phương thức thanh toán bổ trợ
        bool isWalletRefunded = false;
        decimal refundAmount = 0m;

        if (order.PaymentMethod == "Wallet" && order.PaymentStatus == "Paid" && order.FinalAmount > 0)
        {
            refundAmount = order.FinalAmount;
            isWalletRefunded = true;
        }
        else if (order.PaymentMethod == "WalletVNPay" && order.PaymentStatus == "Paid" && order.FinalAmount > 0)
        {
            // Trả qua VNPay xong rồi + Ví => Hoàn tổng số tiền về ví
            refundAmount = order.FinalAmount;
            isWalletRefunded = true;
        }
        else if (order.PaymentMethod == "WalletCOD" || order.PaymentMethod == "WalletVNPay")
        {
            // Chỉ hoàn số tiền ví thực tế đã trừ
            var wallet = await _db.Wallets.FirstOrDefaultAsync(w => w.UserId == accountId, cancellationToken);
            if (wallet != null)
            {
                var totalDeducted = await _db.WalletTransactions
                    .Where(t => t.WalletId == wallet.WalletId && t.ReferenceId == order.OrderCode && t.TransactionType == "PAYMENT")
                    .Select(t => t.Amount)
                    .SumAsync(cancellationToken);
                
                refundAmount = Math.Abs(totalDeducted);
                if (refundAmount > 0)
                {
                    isWalletRefunded = true;
                }
            }
        }

        if (isWalletRefunded && refundAmount > 0)
        {
            await _walletService.RefundAsync(
                accountId,
                refundAmount,
                order.OrderCode,
                $"Ho\u00e0n ti\u1ec1n \u0111\u01a1n h\u00e0ng {order.OrderCode} b\u1ecb h\u1ee7y (Ph\u01b0\u01a1ng th\u1ee9c: {order.PaymentMethod})",
                cancellationToken);
            TempData["AccountSuccess"] = $"\u0110\u00e3 h\u1ee7y \u0111\u01a1n h\u00e0ng v\u00e0 ho\u00e0n {refundAmount:N0}\u0111 v\u00e0o s\u1ed1 d\u01b0 t\u00e0i kho\u1ea3n.";
        }
        else
        {
            TempData["AccountSuccess"] = "\u0110\u00e3 h\u1ee7y \u0111\u01a1n h\u00e0ng.";
        }

        return Redirect(returnUrl);
    }

    [HttpPost("/my-orders/received")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmReceived([FromForm] int id, [FromForm] string? returnUrl, CancellationToken cancellationToken)
    {
        var fallbackReturnUrl = Url.RouteUrl("my_orders") ?? "/my-orders";
        var rUrl = !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl)
            ? returnUrl
            : fallbackReturnUrl;

        if (!(User?.Identity?.IsAuthenticated ?? false))
        {
            return Redirect(rUrl);
        }

        var idRaw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(idRaw, out var accountId) || accountId <= 0)
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Redirect(rUrl);
        }

        if (id <= 0)
        {
            TempData["AccountError"] = "Đơn hàng không hợp lệ.";
            return Redirect(rUrl);
        }

        var order = await _db.Orders
            .Include(o => o.Voucher)
            .Include(o => o.Items)
                .ThenInclude(i => i.Variant)
                    .ThenInclude(v => v!.Product)
            .FirstOrDefaultAsync(o => o.Id == id && o.CustomerId == accountId, cancellationToken);

        if (order is null)
        {
            TempData["AccountError"] = "Không tìm thấy đơn hàng.";
            return Redirect(rUrl);
        }

        var currentStatus = (order.OrderStatus ?? string.Empty).Trim().Replace(" ", string.Empty).ToLowerInvariant();
        var isDelivering = currentStatus is "shipping" or "delivering" or "intransit" or "đanggiao" or "danggiao" or "delivered" or "deliverysuccess" or "dagiao" or "đãgiao";

        if (!isDelivering)
        {
            TempData["AccountError"] = "Đơn hàng không ở trạng thái giao hàng để xác nhận.";
            return Redirect(rUrl);
        }

        order.OrderStatus = "Completed";
        if (!string.Equals(order.PaymentStatus, "Paid", StringComparison.OrdinalIgnoreCase))
        {
            order.PaymentStatus = "Paid";
        }
        order.UpdatedAt = DateTime.UtcNow;

        // Tự động giải ngân tiền từ Escrow vào Ví Doanh thu của Shop ngay khi khách bấm "Đã nhận được hàng"
        string settleRef = $"SETTLE-{order.OrderCode}";
        var shopWallet = await _walletService.GetOrCreateWalletAsync(order.ShopId, cancellationToken);
        bool alreadySettled = await _db.WalletTransactions
            .AsNoTracking()
            .AnyAsync(t => t.WalletId == shopWallet.WalletId && (t.ReferenceId == settleRef || t.ReferenceId == order.OrderCode), cancellationToken);

        decimal settledNetPayout = 0m;
        if (!alreadySettled)
        {
            var isShopMall = await _db.UserProfiles.AsNoTracking()
                .AnyAsync(p => p.AccountId == order.ShopId && p.IsMall, cancellationToken);
            var allCategories = await _db.Categories.AsNoTracking().ToDictionaryAsync(c => c.Id, cancellationToken);

            decimal subTotal = order.SubTotal;
            decimal shopVoucherDisc = order.ShopDiscountAmount > 0m
                ? order.ShopDiscountAmount
                : (order.Voucher?.ShopId != null ? order.DiscountAmount : 0m);
            decimal effectiveSubTotal = Math.Max(0m, subTotal - shopVoucherDisc);
            decimal shopDiscountScale = subTotal > 0m ? (effectiveSubTotal / subTotal) : 1m;

            decimal comm = 0m;
            foreach (var item in order.Items)
            {
                if (item.CommissionAmount > 0)
                {
                    comm += item.CommissionAmount;
                }
                else
                {
                    var catId = item.Variant?.Product?.CategoryId ?? 0;
                    var policy = Helpers.CommissionHelper.ResolveCategoryPolicy(catId, allCategories, 8m);
                    var rawComm = Helpers.CommissionHelper.CalculateItemCommission(
                        item.UnitPrice, item.Quantity, policy.CommissionRatePercent, policy.MaxCommissionAmount, isShopMall);
                    comm += Math.Round(rawComm * shopDiscountScale, 0);
                }
            }

            decimal sellerTaxPct = isShopMall ? 0m : 1.5m;
            decimal tax = Math.Round(effectiveSubTotal * (sellerTaxPct / 100m), 0);
            settledNetPayout = Math.Max(0m, effectiveSubTotal - comm - tax);

            if (settledNetPayout > 0m)
            {
                shopWallet.Balance += settledNetPayout;
                shopWallet.UpdatedAt = DateTime.UtcNow;

                string voucherNote = shopVoucherDisc > 0 ? $", Voucher Shop {shopVoucherDisc:N0}₫" : "";
                _db.WalletTransactions.Add(new WalletTransaction
                {
                    WalletId = shopWallet.WalletId,
                    Amount = settledNetPayout,
                    TransactionType = "SETTLEMENT",
                    ReferenceId = settleRef,
                    Description = $"Giải ngân tự động đơn #{order.OrderCode} khi khách xác nhận Đã nhận hàng (Phí sàn {comm:N0}₫, Thuế {tax:N0}₫{voucherNote})",
                    BalanceAfter = shopWallet.Balance,
                    CreatedAt = DateTime.UtcNow
                });

                _db.SystemNotifications.Add(new SystemNotification
                {
                    Title = $"Giải ngân tự động +{settledNetPayout:N0}₫ đơn #{order.OrderCode}",
                    Content = $"Khách hàng đã xác nhận nhận hàng thành công cho đơn #{order.OrderCode}. Hệ thống đã tự động giải ngân +{settledNetPayout:N0}₫ từ Escrow vào Ví Doanh thu của Shop.",
                    TargetType = "Account",
                    TargetAccountId = order.ShopId,
                    SentByAdminId = accountId,
                    CreatedAt = DateTime.UtcNow
                });
            }
        }

        _db.OrderTrackingLogs.Add(new OrderTrackingLog
        {
            OrderId = order.Id,
            Status = order.OrderStatus,
            Note = settledNetPayout > 0
                ? $"Khách hàng xác nhận đã nhận hàng. Hệ thống đã giải ngân tự động +{settledNetPayout:N0}₫ vào Ví Doanh thu của Gian hàng."
                : "Khách hàng xác nhận đã nhận hàng.",
            CreatedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync(cancellationToken);

        TempData["AccountSuccess"] = "Đã xác nhận nhận hàng thành công! Cảm ơn bạn đã mua sắm trên Sellzy.";
        return Redirect(rUrl);
    }

    [HttpPost("/my-orders/repurchase")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RepurchaseOrder([FromForm] int id, string? returnUrl, CancellationToken cancellationToken)
    {
        var fallbackReturnUrl = Url.RouteUrl("my_orders") ?? "/my-orders";
        var finalReturnUrl = !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl)
            ? returnUrl
            : fallbackReturnUrl;

        if (!(User?.Identity?.IsAuthenticated ?? false))
        {
            return Redirect(finalReturnUrl);
        }

        var idRaw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(idRaw, out var accountId) || accountId <= 0)
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Redirect(finalReturnUrl);
        }

        if (id <= 0)
        {
            TempData["AccountError"] = "Đơn hàng không hợp lệ.";
            return Redirect(finalReturnUrl);
        }

        var order = await _db.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == id && o.CustomerId == accountId, cancellationToken);

        if (order is null)
        {
            TempData["AccountError"] = "Không tìm thấy đơn hàng.";
            return Redirect(finalReturnUrl);
        }

        var cart = await _db.Carts.FirstOrDefaultAsync(c => c.AccountId == accountId, cancellationToken);
        if (cart is null)
        {
            cart = new Cart
            {
                AccountId = accountId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _db.Carts.Add(cart);
            await _db.SaveChangesAsync(cancellationToken);
        }

        var nowUtc = DateTime.UtcNow;
        var addedCount = 0;
        var repurchasedVariantIds = new HashSet<int>();

        foreach (var oi in order.Items)
        {
            var variantExists = await _db.ProductVariants.AnyAsync(v => v.Id == oi.VariantId && v.IsActive && !v.IsDeleted, cancellationToken);
            if (!variantExists) continue;

            repurchasedVariantIds.Add(oi.VariantId);

            var item = await _db.CartItems
                .FirstOrDefaultAsync(i => i.CartId == cart.Id && i.VariantId == oi.VariantId, cancellationToken);

            if (item is null)
            {
                item = new CartItem
                {
                    CartId = cart.Id,
                    VariantId = oi.VariantId,
                    Quantity = oi.Quantity,
                    CreatedAt = nowUtc,
                    UpdatedAt = nowUtc
                };
                _db.CartItems.Add(item);
            }
            else
            {
                item.Quantity = oi.Quantity;
                item.UpdatedAt = nowUtc;
            }
            addedCount++;
        }
        
        if (addedCount > 0)
        {
            cart.UpdatedAt = nowUtc;
            await _db.SaveChangesAsync(cancellationToken);

            var allCartItems = await _db.CartItems.Where(i => i.CartId == cart.Id).ToListAsync(cancellationToken);
            var deselectedIds = new HashSet<int>();
            foreach (var ci in allCartItems)
            {
                if (!repurchasedVariantIds.Contains(ci.VariantId))
                {
                    deselectedIds.Add(ci.Id);
                }
            }

            HttpContext.Session.SetJson("cart_selected", new { Initialized = true, DeselectedIds = deselectedIds });
            
            return Redirect("/checkout");
        }
        else
        {
            TempData["AccountError"] = "Các sản phẩm trong đơn hàng này đã ngừng bán hoặc không còn tồn tại.";
            return Redirect(finalReturnUrl);
        }
    }

    [HttpGet]
    public async Task<IActionResult> OrderDetails(int id, CancellationToken cancellationToken)
    {
        if (id <= 0) return NotFound();

        if (!(User?.Identity?.IsAuthenticated ?? false))
        {
            return View(new MyOrderDetailsPageVm { IsSignedIn = false });
        }

        var idRaw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(idRaw, out var accountId) || accountId <= 0)
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return View(new MyOrderDetailsPageVm { IsSignedIn = false });
        }

        // Auto-cancel any expired bank transfer orders for this customer first
        await CancelExpiredOrdersForCustomerAsync(accountId, cancellationToken);

        var order = await _db.Orders
            .AsNoTracking()
            .Include(o => o.Items)
            .Include(o => o.Voucher)
            .FirstOrDefaultAsync(o => o.Id == id && o.CustomerId == accountId, cancellationToken);

        if (order is null) return NotFound();

        var logs = await _db.OrderTrackingLogs
            .AsNoTracking()
            .Where(x => x.OrderId == order.Id)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new CustomerOrderTrackingLogVm
            {
                Status = x.Status,
                Note = x.Note,
                CreatedAt = x.CreatedAt
            })
            .ToListAsync(cancellationToken);

        // Extract shipper info (last-mile simulator metadata) before sanitizing notes.
        string? shipperName = null;
        string? shipperPhone = null;
        foreach (var l in logs)
        {
            if (TryExtractSimShipper(l.Note, out var n, out var p))
            {
                shipperName = n;
                shipperPhone = p;
                break;
            }
        }

        var hasSimPickupDepart = await _db.OrderTrackingLogs
            .AsNoTracking()
            .Where(l => l.OrderId == order.Id && l.Note != null)
            .AnyAsync(l => EF.Functions.Like(l.Note!, "SIM|i=1|e=depart%")
                           || EF.Functions.Like(l.Note!, "SIM|i=1|e=leave%"), cancellationToken);

        foreach (var l in logs)
        {
            l.Note = SanitizeCustomerTrackingNote(l.Note);
        }

        DateTime? estimatedDeliveryAt = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(_ghnOptions.Token)
                && order.ShippingDistrictId.HasValue
                && order.ShippingDistrictId.Value > 0
                && !string.IsNullOrWhiteSpace(order.ShippingWardCode))
            {
                var senderAddresses = await _db.UserAddresses
                    .AsNoTracking()
                    .Where(a => a.AccountId == order.ShopId)
                    .ToListAsync(cancellationToken);

                var sender = PickBestSenderAddress(senderAddresses);
                if (sender is not null && sender.DistrictId > 0 && !string.IsNullOrWhiteSpace(sender.WardCode))
                {
                    var services = await _ghn.GetAvailableServicesAsync(sender.DistrictId, order.ShippingDistrictId.Value, shopId: null, cancellationToken);
                    var preferredService = services
                        .OrderByDescending(s => s.ServiceTypeId == (_ghnOptions.ServiceTypeId <= 0 ? 2 : _ghnOptions.ServiceTypeId))
                        .ThenBy(s => s.ServiceId)
                        .FirstOrDefault();

                    if (preferredService is not null && preferredService.ServiceId > 0)
                    {
                        var lead = await _ghn.GetLeadTimeAsync(new GhnLeadTimeRequest
                        {
                            FromDistrictId = sender.DistrictId,
                            FromWardCode = sender.WardCode,
                            ToDistrictId = order.ShippingDistrictId.Value,
                            ToWardCode = order.ShippingWardCode,
                            ServiceId = preferredService.ServiceId
                        }, shopId: null, cancellationToken);

                        if (lead.Leadtime > 0)
                        {
                            estimatedDeliveryAt = DateTimeOffset.FromUnixTimeSeconds(lead.Leadtime).UtcDateTime;
                        }
                    }
                }
            }
        }
        catch
        {
            // Best-effort only: do not block order detail page.
        }

        decimal voucherDiscount = 0m;
        decimal shippingDiscount = 0m;

        if (order.DiscountAmount > 0)
        {
            if (order.Voucher is not null)
            {
                var v = order.Voucher;
                decimal calculatedVoucherDiscount = 0m;

                if (v.DiscountType == "Percent")
                {
                    calculatedVoucherDiscount = order.SubTotal * (v.DiscountValue / 100m);
                }
                else if (v.DiscountType == "FixedAmount")
                {
                    calculatedVoucherDiscount = v.DiscountValue;
                }
                else if (v.DiscountType == "FreeShipping")
                {
                    calculatedVoucherDiscount = v.DiscountValue;
                    if (calculatedVoucherDiscount <= 0 || calculatedVoucherDiscount > order.ShippingFee)
                    {
                        calculatedVoucherDiscount = order.ShippingFee;
                    }
                }

                if (calculatedVoucherDiscount > order.SubTotal && v.DiscountType != "FreeShipping")
                {
                    calculatedVoucherDiscount = order.SubTotal;
                }

                if (v.MaxDiscountValue.HasValue && calculatedVoucherDiscount > v.MaxDiscountValue.Value)
                {
                    calculatedVoucherDiscount = v.MaxDiscountValue.Value;
                }

                if (calculatedVoucherDiscount < 0) calculatedVoucherDiscount = 0m;

                if (v.DiscountType == "FreeShipping")
                {
                    shippingDiscount = calculatedVoucherDiscount;
                    voucherDiscount = order.DiscountAmount - shippingDiscount;
                }
                else
                {
                    voucherDiscount = calculatedVoucherDiscount;
                    shippingDiscount = order.DiscountAmount - voucherDiscount;
                }

                if (voucherDiscount < 0) voucherDiscount = 0m;
                if (shippingDiscount < 0) shippingDiscount = 0m;

                if (voucherDiscount + shippingDiscount > order.DiscountAmount)
                {
                    var excess = (voucherDiscount + shippingDiscount) - order.DiscountAmount;
                    if (shippingDiscount >= excess) shippingDiscount -= excess;
                    else shippingDiscount = 0m;
                }
            }
            else
            {
                voucherDiscount = order.DiscountAmount;
            }
        }

        var vm = new CustomerOrderDetailsVm
        {
            Id = order.Id,
            OrderCode = order.OrderCode,
            OrderStatus = ResolveEffectiveOrderStatus(order.OrderStatus, hasSimPickupDepart),
            PaymentMethod = order.PaymentMethod,
            PaymentStatus = order.PaymentStatus,
            IsReceivedByCustomer = await _db.OrderTrackingLogs.AnyAsync(l => l.OrderId == order.Id && l.Note == "Khách hàng xác nhận đã nhận hàng", cancellationToken),
            CanReturn = !await _db.OrderItems
                .AsNoTracking()
                .Where(i => i.OrderId == order.Id && i.Variant != null && i.Variant.Product != null && !i.Variant.Product.IsReturnable)
                .AnyAsync(cancellationToken),
            PaymentDeadlineUtc = order.CreatedAt.AddMinutes(15),
            SubTotal = order.SubTotal,
            ShippingFee = order.ShippingFee,
            DiscountAmount = order.DiscountAmount,
            FinalAmount = order.FinalAmount,
            VoucherCode = order.Voucher?.Code,
            VoucherDiscountType = order.Voucher?.DiscountType,
            VoucherDiscount = voucherDiscount,
            ShippingDiscount = shippingDiscount,
            RecipientName = order.RecipientName,
            RecipientPhone = order.RecipientPhone,
            ShippingAddress = order.ShippingAddress,
            TrackingCode = order.TrackingCode,
            ShipperName = shipperName,
            ShipperPhone = shipperPhone,
            EstimatedDeliveryAt = estimatedDeliveryAt,
            CreatedAt = order.CreatedAt,
            UpdatedAt = order.UpdatedAt,
            Items = order.Items
                .OrderBy(i => i.Id)
                .Select(i => new CustomerOrderItemVm
                {
                    ProductName = i.ProductName,
                    ColorName = i.ColorName,
                    SizeName = i.SizeName,
                    VariantDescription = i.VariantDescription,
                    IsReturnable = i.Variant?.Product?.IsReturnable ?? true,
                    UnitPrice = i.UnitPrice,
                    Quantity = i.Quantity,
                    LineTotal = i.LineTotal
                })
                .ToList(),
            TrackingLogs = logs,
            IsDigitalOrder = order.IsDigitalOrder,
            RequiresVatInvoice = order.RequiresVatInvoice,
            CompanyName = order.CompanyName,
            TaxCode = order.TaxCode,
            CompanyAddress = order.CompanyAddress
        };

        return View(new MyOrderDetailsPageVm { IsSignedIn = true, Order = vm });
    }

    [HttpGet]
    public async Task<IActionResult> Wishlist(CancellationToken cancellationToken)
    {
        if (!(User?.Identity?.IsAuthenticated ?? false))
        {
            return View("Wishlist", new MyAccountPageVm { IsSignedIn = false });
        }

        var idRaw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(idRaw, out var accountId) || accountId <= 0)
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return View("Wishlist", new MyAccountPageVm { IsSignedIn = false });
        }

        var account = await _db.Accounts
            .Include(a => a.Profile)
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == accountId && !a.IsDeleted, cancellationToken);

        if (account is null)
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return View("Wishlist", new MyAccountPageVm { IsSignedIn = false });
        }

        var items = await _db.WishlistItems
            .AsNoTracking()
            .Where(w => w.AccountId == account.Id)
            .Where(w => w.Product != null && w.Product.Status == "Active" && !w.Product.IsDeleted)
            .OrderByDescending(w => w.CreatedAt)
            .Select(w => new WishlistItemVm
            {
                ProductId = w.ProductId,
                ProductName = w.Product != null ? w.Product.Name : string.Empty,
                ProductSlug = w.Product != null ? w.Product.Slug : string.Empty,
                CategoryName = w.Product != null && w.Product.Category != null ? w.Product.Category.Name : null,
                ProductImageUrl = w.Product != null
                    ? w.Product.Images
                        .OrderByDescending(i => i.IsPrimary)
                        .ThenBy(i => i.SortOrder)
                        .ThenBy(i => i.Id)
                        .Select(i => i.ImageUrl)
                        .FirstOrDefault()
                    : null,
                VariantId = w.Product != null
                    ? (w.Product.Variants
                        .Where(v => v.IsActive && !v.IsDeleted)
                        .OrderByDescending(v => v.IsDefault)
                        .ThenBy(v => v.Id)
                        .Select(v => (int?)v.Id)
                        .FirstOrDefault() ?? 0)
                    : 0,
                Price = w.Product != null
                    ? (w.Product.Variants
                        .Where(v => v.IsActive && !v.IsDeleted)
                        .OrderByDescending(v => v.IsDefault)
                        .ThenBy(v => v.Id)
                        .Select(v => (decimal?)v.Price)
                        .FirstOrDefault() ?? 0m)
                    : 0m,
                SalePrice = w.Product != null
                    ? (w.Product.Variants
                        .Where(v => v.IsActive && !v.IsDeleted)
                        .OrderByDescending(v => v.IsDefault)
                        .ThenBy(v => v.Id)
                        .Select(v => v.SalePrice)
                        .FirstOrDefault())
                    : null,
                AvailableStock = w.Product != null
                    ? (w.Product.Variants
                        .Where(v => v.IsActive && !v.IsDeleted)
                        .OrderByDescending(v => v.IsDefault)
                        .ThenBy(v => v.Id)
                        .Select(v => (int?)(v.StockQuantity - v.ReservedStock))
                        .FirstOrDefault() ?? 0)
                    : 0,
                AverageRating = w.Product != null ? w.Product.AverageRating : 0m,
                ReviewCount = w.Product != null ? w.Product.ReviewCount : 0
            })
            .ToListAsync(cancellationToken);

        var vm = new MyAccountVm
        {
            AccountId = account.Id,
            Email = account.Email,
            HasPassword = !string.IsNullOrWhiteSpace(account.PasswordHash),
            Role = account.Role,
            Status = account.Status,
            AuthProvider = account.AuthProvider,
            CreatedAt = account.CreatedAt,
            LastLoginAt = account.LastLoginAt,
            FullName = account.Profile?.FullName,
            PhoneNumber = account.Profile?.PhoneNumber,
            Address = account.Profile?.Address,
            AvatarUrl = account.Profile?.AvatarUrl,
            WishlistItems = items
        };

        return View("Wishlist", new MyAccountPageVm { IsSignedIn = true, Account = vm });
    }

    [HttpPost("/my-wishlist/remove")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveWishlistItem([FromForm] int productId, CancellationToken cancellationToken)
    {
        if (!(User?.Identity?.IsAuthenticated ?? false))
        {
            return RedirectToRoute("my_wishlist");
        }

        var idRaw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(idRaw, out var accountId) || accountId <= 0)
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToRoute("my_wishlist");
        }

        if (productId <= 0)
        {
            return RedirectToRoute("my_wishlist");
        }

        var row = await _db.WishlistItems
            .FirstOrDefaultAsync(w => w.AccountId == accountId && w.ProductId == productId, cancellationToken);

        if (row is not null)
        {
            _db.WishlistItems.Remove(row);
            await _db.SaveChangesAsync(cancellationToken);
        }

        return RedirectToRoute("my_wishlist");
    }

    public sealed record ToggleWishlistRequest(int ProductId);

    [HttpGet("/my-wishlist/ids")]
    public async Task<IActionResult> WishlistIds(CancellationToken cancellationToken)
    {
        if (!(User?.Identity?.IsAuthenticated ?? false))
        {
            return Ok(new { items = Array.Empty<int>() });
        }

        var idRaw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(idRaw, out var accountId) || accountId <= 0)
        {
            return Ok(new { items = Array.Empty<int>() });
        }

        var items = await _db.WishlistItems
            .AsNoTracking()
            .Where(w => w.AccountId == accountId)
            .OrderByDescending(w => w.CreatedAt)
            .Select(w => w.ProductId)
            .ToListAsync(cancellationToken);

        return Ok(new { items });
    }

    [HttpPost("/my-wishlist/toggle")]
    public async Task<IActionResult> ToggleWishlist([FromBody] ToggleWishlistRequest req, CancellationToken cancellationToken)
    {
        if (!(User?.Identity?.IsAuthenticated ?? false))
        {
            return Unauthorized();
        }

        var idRaw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(idRaw, out var accountId) || accountId <= 0)
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Unauthorized();
        }

        var productId = req.ProductId;
        if (productId <= 0)
        {
            return BadRequest(new { message = "Invalid productId" });
        }

        var productExists = await _db.Products
            .AsNoTracking()
            .AnyAsync(p => p.Id == productId && p.Status == "Active" && !p.IsDeleted, cancellationToken);

        if (!productExists)
        {
            return NotFound();
        }

        var existing = await _db.WishlistItems
            .FirstOrDefaultAsync(w => w.AccountId == accountId && w.ProductId == productId, cancellationToken);

        if (existing is not null)
        {
            _db.WishlistItems.Remove(existing);
            await _db.SaveChangesAsync(cancellationToken);
            return Ok(new { isInWishlist = false });
        }

        _db.WishlistItems.Add(new WishlistItem
        {
            AccountId = accountId,
            ProductId = productId,
        });

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Unique index (AccountId, ProductId): treat as already wishlisted.
        }

        return Ok(new { isInWishlist = true });
    }

    public sealed record UpdateProfileRequest(string? FullName, string? PhoneNumber, string? Address, string? AvatarUrl, IFormFile? AvatarFile);

    private bool IsAjaxRequest()
    {
        if (Request?.Headers is null) return false;
        if (string.Equals(Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase)) return true;
        var accept = Request.Headers.Accept.ToString();
        return accept.Contains("application/json", StringComparison.OrdinalIgnoreCase);
    }

    [HttpPost("/my-account/update-profile")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateProfile([FromForm] UpdateProfileRequest req, CancellationToken cancellationToken)
    {
        if (!(User?.Identity?.IsAuthenticated ?? false))
        {
            return IsAjaxRequest() ? Unauthorized() : RedirectToRoute("my_account");
        }

        var idRaw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(idRaw, out var accountId) || accountId <= 0)
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return IsAjaxRequest() ? Unauthorized() : RedirectToRoute("my_account");
        }

        var account = await _db.Accounts
            .Include(a => a.Profile)
            .FirstOrDefaultAsync(a => a.Id == accountId && !a.IsDeleted, cancellationToken);

        if (account is null)
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return IsAjaxRequest() ? Unauthorized() : RedirectToRoute("my_account");
        }

        if (account.Profile is null)
        {
            account.Profile = new UserProfile
            {
                AccountId = account.Id,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _db.UserProfiles.Add(account.Profile);
        }

        var fullName = string.IsNullOrWhiteSpace(req.FullName) ? null : req.FullName.Trim();
        if (fullName is not null && fullName.Length > 150) fullName = fullName[..150];

        var phoneNumber = string.IsNullOrWhiteSpace(req.PhoneNumber) ? null : req.PhoneNumber.Trim();
        if (phoneNumber is not null && phoneNumber.Length > 20) phoneNumber = phoneNumber[..20];

        var address = string.IsNullOrWhiteSpace(req.Address) ? null : req.Address.Trim();
        if (address is not null && address.Length > 500) address = address[..500];

        var avatarUrl = string.IsNullOrWhiteSpace(req.AvatarUrl) ? null : req.AvatarUrl.Trim();
        if (avatarUrl is not null && avatarUrl.Length > 500) avatarUrl = avatarUrl[..500];

        if (req.AvatarFile is not null && req.AvatarFile.Length > 0)
        {
            var savedUrl = await TrySaveAvatarAsync(req.AvatarFile, cancellationToken);
            if (savedUrl is null)
            {
                if (IsAjaxRequest())
                {
                    return BadRequest(new { ok = false, message = "Ảnh đại diện không hợp lệ (chỉ nhận JPG/PNG/WEBP, tối đa 2MB)." });
                }

                TempData["AccountError"] = "Ảnh đại diện không hợp lệ (chỉ nhận JPG/PNG/WEBP, tối đa 2MB).";
                return RedirectToRoute("my_account");
            }

            avatarUrl = savedUrl;
        }

        account.Profile.FullName = fullName;
        account.Profile.PhoneNumber = phoneNumber;
        account.Profile.Address = address;

        if (avatarUrl is not null)
        {
            account.Profile.AvatarUrl = avatarUrl;
        }

        account.Profile.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);

        if (IsAjaxRequest())
        {
            return Ok(new
            {
                ok = true,
                message = "Đã cập nhật thông tin tài khoản.",
                fullName = account.Profile.FullName,
                phoneNumber = account.Profile.PhoneNumber
            });
        }

        TempData["AccountSuccess"] = "Đã cập nhật thông tin tài khoản.";
        return RedirectToRoute("my_account");
    }

    private async Task<string?> TrySaveAvatarAsync(IFormFile file, CancellationToken cancellationToken)
    {
        const long maxBytes = 2 * 1024 * 1024;
        if (file.Length <= 0 || file.Length > maxBytes)
        {
            return null;
        }

        var ext = Path.GetExtension(file.FileName)?.ToLowerInvariant() ?? string.Empty;
        if (ext is not ".jpg" and not ".jpeg" and not ".png" and not ".webp")
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(file.ContentType) && !file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var webRoot = string.IsNullOrWhiteSpace(_env.WebRootPath)
            ? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot")
            : _env.WebRootPath;

        var avatarsDir = Path.Combine(webRoot, "uploads", "avatars");
        Directory.CreateDirectory(avatarsDir);

        var fileName = $"{Guid.NewGuid():N}{ext}";
        var fullPath = Path.Combine(avatarsDir, fileName);

        await using (var fs = System.IO.File.Create(fullPath))
        {
            await file.CopyToAsync(fs, cancellationToken);
        }

        return $"/uploads/avatars/{fileName}";
    }

    public sealed record ChangePasswordRequest(string? CurrentPassword, string? NewPassword, string? ConfirmPassword);

    [HttpPost("/my-account/change-password")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword([FromForm] ChangePasswordRequest req, CancellationToken cancellationToken)
    {
        if (!(User?.Identity?.IsAuthenticated ?? false))
        {
            return IsAjaxRequest() ? Unauthorized() : RedirectToRoute("my_account");
        }

        var idRaw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(idRaw, out var accountId) || accountId <= 0)
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return IsAjaxRequest() ? Unauthorized() : RedirectToRoute("my_account");
        }

        var account = await _db.Accounts
            .FirstOrDefaultAsync(a => a.Id == accountId && !a.IsDeleted, cancellationToken);

        if (account is null)
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return IsAjaxRequest() ? Unauthorized() : RedirectToRoute("my_account");
        }

        var newPassword = req.NewPassword ?? string.Empty;
        var confirm = req.ConfirmPassword ?? string.Empty;

        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 6)
        {
            if (IsAjaxRequest()) return BadRequest(new { ok = false, message = "Mật khẩu mới phải có ít nhất 6 ký tự." });
            TempData["AccountError"] = "Mật khẩu mới phải có ít nhất 6 ký tự.";
            return RedirectToRoute("my_account");
        }

        if (!string.Equals(newPassword, confirm, StringComparison.Ordinal))
        {
            if (IsAjaxRequest()) return BadRequest(new { ok = false, message = "Mật khẩu xác nhận không khớp." });
            TempData["AccountError"] = "Mật khẩu xác nhận không khớp.";
            return RedirectToRoute("my_account");
        }

        if (!string.IsNullOrWhiteSpace(account.PasswordHash))
        {
            var currentPassword = req.CurrentPassword ?? string.Empty;
            if (string.IsNullOrWhiteSpace(currentPassword))
            {
                if (IsAjaxRequest()) return BadRequest(new { ok = false, message = "Vui lòng nhập mật khẩu hiện tại." });
                TempData["AccountError"] = "Vui lòng nhập mật khẩu hiện tại.";
                return RedirectToRoute("my_account");
            }

            var verify = _passwordHasher.VerifyHashedPassword(account, account.PasswordHash, currentPassword);
            if (verify == PasswordVerificationResult.Failed)
            {
                if (IsAjaxRequest()) return BadRequest(new { ok = false, message = "Mật khẩu hiện tại không đúng." });
                TempData["AccountError"] = "Mật khẩu hiện tại không đúng.";
                return RedirectToRoute("my_account");
            }
        }

        account.PasswordHash = _passwordHasher.HashPassword(account, newPassword);
        await _db.SaveChangesAsync(cancellationToken);

        if (IsAjaxRequest())
        {
            return Ok(new { ok = true, message = "Đã cập nhật mật khẩu." });
        }

        TempData["AccountSuccess"] = "Đã cập nhật mật khẩu.";
        return RedirectToRoute("my_account");
    }

    [HttpGet("/api/ghn/provinces")]
    public async Task<IActionResult> GetGhnProvinces(CancellationToken cancellationToken)
    {
        var logger = HttpContext.RequestServices.GetRequiredService<ILogger<AccountController>>();
        var sw = Stopwatch.StartNew();
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "?";
        var ua = Request.Headers.UserAgent.ToString();
        var referer = Request.Headers.Referer.ToString();
        logger.LogInformation("[GHN] provinces requested ip={IP} ua={UA} referer={Referer}", ip, ua, referer);

        try
        {
            var provinces = await _ghn.GetProvincesAsync(cancellationToken);
            var payload = provinces.Select(p => new { id = p.ProvinceId, name = p.ProvinceName }).ToList();
            logger.LogInformation("[GHN] provinces ok count={Count} elapsedMs={ElapsedMs}", payload.Count, sw.ElapsedMilliseconds);
            return Ok(payload);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[GHN] provinces failed elapsedMs={ElapsedMs}", sw.ElapsedMilliseconds);
            return Problem(ex.Message);
        }
    }

    [HttpGet("/api/ghn/districts")]
    public async Task<IActionResult> GetGhnDistricts([FromQuery] int provinceId, CancellationToken cancellationToken)
    {
        if (provinceId <= 0)
        {
            return BadRequest("provinceId is required");
        }

        var logger = HttpContext.RequestServices.GetRequiredService<ILogger<AccountController>>();
        var sw = Stopwatch.StartNew();
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "?";
        var ua = Request.Headers.UserAgent.ToString();
        var referer = Request.Headers.Referer.ToString();
        logger.LogInformation("[GHN] districts requested provinceId={ProvinceId} ip={IP} ua={UA} referer={Referer}", provinceId, ip, ua, referer);

        try
        {
            var districts = await _ghn.GetDistrictsAsync(provinceId, cancellationToken);
            var payload = districts.Select(d => new { id = d.DistrictId, name = d.DistrictName }).ToList();
            logger.LogInformation("[GHN] districts ok provinceId={ProvinceId} count={Count} elapsedMs={ElapsedMs}", provinceId, payload.Count, sw.ElapsedMilliseconds);
            return Ok(payload);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[GHN] districts failed provinceId={ProvinceId} elapsedMs={ElapsedMs}", provinceId, sw.ElapsedMilliseconds);
            return Problem(ex.Message);
        }
    }

    [HttpGet("/api/ghn/wards")]
    public async Task<IActionResult> GetGhnWards([FromQuery] int districtId, CancellationToken cancellationToken)
    {
        if (districtId <= 0)
        {
            return BadRequest("districtId is required");
        }

        var logger = HttpContext.RequestServices.GetRequiredService<ILogger<AccountController>>();
        var sw = Stopwatch.StartNew();
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "?";
        var ua = Request.Headers.UserAgent.ToString();
        var referer = Request.Headers.Referer.ToString();
        logger.LogInformation("[GHN] wards requested districtId={DistrictId} ip={IP} ua={UA} referer={Referer}", districtId, ip, ua, referer);

        try
        {
            var wards = await _ghn.GetWardsAsync(districtId, cancellationToken);
            var payload = wards.Select(w => new { code = w.WardCode, name = w.WardName }).ToList();
            logger.LogInformation("[GHN] wards ok districtId={DistrictId} count={Count} elapsedMs={ElapsedMs}", districtId, payload.Count, sw.ElapsedMilliseconds);
            return Ok(payload);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[GHN] wards failed districtId={DistrictId} elapsedMs={ElapsedMs}", districtId, sw.ElapsedMilliseconds);
            return Problem(ex.Message);
        }
    }

    public sealed record CreateAddressRequest(
        string? FullName,
        string? PhoneNumber,
        int ProvinceId,
        string? ProvinceName,
        int DistrictId,
        string? DistrictName,
        string? WardCode,
        string? WardName,
        string? StreetAddress,
        int? AddressType,
        bool IsDefault);

    [HttpPost("/my-account/address/create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateAddress([FromForm] CreateAddressRequest req, CancellationToken cancellationToken)
    {
        if (!(User?.Identity?.IsAuthenticated ?? false))
        {
            return RedirectToRoute("my_account");
        }

        var idRaw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(idRaw, out var accountId) || accountId <= 0)
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToRoute("my_account");
        }

        var normalized = NormalizeAddress(req.FullName, req.PhoneNumber, req.ProvinceId, req.ProvinceName, req.DistrictId, req.DistrictName,
            req.WardCode, req.WardName, req.StreetAddress, out var error);

        if (normalized is null)
        {
            if (IsAjaxRequest()) return BadRequest(new { ok = false, message = error });
            TempData["AccountError"] = error;
            return RedirectToRoute("my_account");
        }

        var addressType = req.AddressType.GetValueOrDefault(0) == 1
            ? AddressType.Shop
            : AddressType.Personal;

        if (req.IsDefault)
        {
            var others = await _db.UserAddresses
                .Where(x => x.AccountId == accountId && x.AddressType == addressType && x.IsDefault)
                .ToListAsync(cancellationToken);

            foreach (var o in others)
            {
                o.IsDefault = false;
                o.UpdatedAt = DateTime.UtcNow;
            }
        }

        var now = DateTime.UtcNow;
        var entity = new UserAddress
        {
            AccountId = accountId,
            FullName = normalized.FullName,
            PhoneNumber = normalized.PhoneNumber,
            ProvinceId = normalized.ProvinceId,
            ProvinceName = normalized.ProvinceName,
            DistrictId = normalized.DistrictId,
            DistrictName = normalized.DistrictName,
            WardCode = normalized.WardCode,
            WardName = normalized.WardName,
            StreetAddress = normalized.StreetAddress,
            AddressType = addressType,
            IsDefault = req.IsDefault,
            CreatedAt = now,
            UpdatedAt = now
        };

        _db.UserAddresses.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);

        if (IsAjaxRequest())
        {
            return Ok(new
            {
                ok = true,
                message = "Đã lưu địa chỉ.",
                reloadAddresses = true
            });
        }

        TempData["AccountSuccess"] = "Đã lưu địa chỉ.";
        return RedirectToRoute("my_account");
    }

    public sealed record UpdateAddressRequest(
        int Id,
        string? FullName,
        string? PhoneNumber,
        int ProvinceId,
        string? ProvinceName,
        int DistrictId,
        string? DistrictName,
        string? WardCode,
        string? WardName,
        string? StreetAddress,
        int? AddressType,
        bool IsDefault);

    [HttpPost("/my-account/address/update")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateAddress([FromForm] UpdateAddressRequest req, CancellationToken cancellationToken)
    {
        if (!(User?.Identity?.IsAuthenticated ?? false))
        {
            return RedirectToRoute("my_account");
        }

        var idRaw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(idRaw, out var accountId) || accountId <= 0)
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToRoute("my_account");
        }

        if (req.Id <= 0)
        {
            if (IsAjaxRequest()) return BadRequest(new { ok = false, message = "Địa chỉ không hợp lệ." });
            TempData["AccountError"] = "Địa chỉ không hợp lệ.";
            return RedirectToRoute("my_account");
        }

        var address = await _db.UserAddresses
            .FirstOrDefaultAsync(x => x.Id == req.Id && x.AccountId == accountId, cancellationToken);

        if (address is null)
        {
            if (IsAjaxRequest()) return BadRequest(new { ok = false, message = "Không tìm thấy địa chỉ." });
            TempData["AccountError"] = "Không tìm thấy địa chỉ.";
            return RedirectToRoute("my_account");
        }

        var normalized = NormalizeAddress(req.FullName, req.PhoneNumber, req.ProvinceId, req.ProvinceName, req.DistrictId, req.DistrictName,
            req.WardCode, req.WardName, req.StreetAddress, out var error);

        if (normalized is null)
        {
            if (IsAjaxRequest()) return BadRequest(new { ok = false, message = error });
            TempData["AccountError"] = error;
            return RedirectToRoute("my_account");
        }

        var addressType = req.AddressType.GetValueOrDefault((int)address.AddressType) == 1
            ? AddressType.Shop
            : AddressType.Personal;

        if (req.IsDefault)
        {
            var others = await _db.UserAddresses
                .Where(x => x.AccountId == accountId && x.AddressType == addressType && x.IsDefault && x.Id != address.Id)
                .ToListAsync(cancellationToken);

            foreach (var o in others)
            {
                o.IsDefault = false;
                o.UpdatedAt = DateTime.UtcNow;
            }
        }

        address.FullName = normalized.FullName;
        address.PhoneNumber = normalized.PhoneNumber;
        address.ProvinceId = normalized.ProvinceId;
        address.ProvinceName = normalized.ProvinceName;
        address.DistrictId = normalized.DistrictId;
        address.DistrictName = normalized.DistrictName;
        address.WardCode = normalized.WardCode;
        address.WardName = normalized.WardName;
        address.StreetAddress = normalized.StreetAddress;
        address.AddressType = addressType;
        address.IsDefault = req.IsDefault;
        address.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);

        if (IsAjaxRequest())
        {
            return Ok(new
            {
                ok = true,
                message = "Đã cập nhật địa chỉ.",
                reloadAddresses = true
            });
        }

        TempData["AccountSuccess"] = "Đã cập nhật địa chỉ.";
        return RedirectToRoute("my_account");
    }

    public sealed record DeleteAddressRequest(int Id);

    [HttpPost("/my-account/address/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteAddress([FromForm] DeleteAddressRequest req, CancellationToken cancellationToken)
    {
        if (!(User?.Identity?.IsAuthenticated ?? false))
        {
            return IsAjaxRequest() ? Unauthorized() : RedirectToRoute("my_account");
        }

        var idRaw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(idRaw, out var accountId) || accountId <= 0)
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return IsAjaxRequest() ? Unauthorized() : RedirectToRoute("my_account");
        }

        if (req.Id <= 0)
        {
            if (IsAjaxRequest()) return BadRequest(new { ok = false, message = "Địa chỉ không hợp lệ." });
            TempData["AccountError"] = "Địa chỉ không hợp lệ.";
            return RedirectToRoute("my_account");
        }

        var address = await _db.UserAddresses
            .FirstOrDefaultAsync(x => x.Id == req.Id && x.AccountId == accountId, cancellationToken);

        if (address is null)
        {
            if (IsAjaxRequest()) return NotFound(new { ok = false, message = "Không tìm thấy địa chỉ." });
            TempData["AccountError"] = "Không tìm thấy địa chỉ.";
            return RedirectToRoute("my_account");
        }

        _db.UserAddresses.Remove(address);
        await _db.SaveChangesAsync(cancellationToken);

        if (IsAjaxRequest())
        {
            return Ok(new { ok = true, message = "Đã xóa địa chỉ.", reloadAddresses = true });
        }

        TempData["AccountSuccess"] = "Đã xóa địa chỉ.";
        return RedirectToRoute("my_account");
    }

    [HttpGet("/api/my-account/addresses")]
    public async Task<IActionResult> GetMyAddresses(CancellationToken cancellationToken)
    {
        if (!(User?.Identity?.IsAuthenticated ?? false))
        {
            return Unauthorized();
        }

        var idRaw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(idRaw, out var accountId) || accountId <= 0)
        {
            return Unauthorized();
        }

        var addresses = await _db.UserAddresses
            .AsNoTracking()
            .Where(x => x.AccountId == accountId)
            .OrderByDescending(x => x.IsDefault)
            .ThenByDescending(x => x.UpdatedAt)
            .ToListAsync(cancellationToken);

        return Ok(addresses.Select(a => new
        {
            id = a.Id,
            fullName = a.FullName,
            phoneNumber = a.PhoneNumber,
            addressType = (int)a.AddressType,
            provinceId = a.ProvinceId,
            provinceName = a.ProvinceName,
            districtId = a.DistrictId,
            districtName = a.DistrictName,
            wardCode = a.WardCode,
            wardName = a.WardName,
            streetAddress = a.StreetAddress,
            isDefault = a.IsDefault
        }));
    }

    private static NormalizedAddress? NormalizeAddress(
        string? fullName,
        string? phoneNumber,
        int provinceId,
        string? provinceName,
        int districtId,
        string? districtName,
        string? wardCode,
        string? wardName,
        string? streetAddress,
        out string error)
    {
        error = string.Empty;

        fullName = string.IsNullOrWhiteSpace(fullName) ? null : fullName.Trim();
        phoneNumber = string.IsNullOrWhiteSpace(phoneNumber) ? null : phoneNumber.Trim();
        provinceName = string.IsNullOrWhiteSpace(provinceName) ? null : provinceName.Trim();
        districtName = string.IsNullOrWhiteSpace(districtName) ? null : districtName.Trim();
        wardCode = string.IsNullOrWhiteSpace(wardCode) ? null : wardCode.Trim();
        wardName = string.IsNullOrWhiteSpace(wardName) ? null : wardName.Trim();
        streetAddress = string.IsNullOrWhiteSpace(streetAddress) ? null : streetAddress.Trim();

        if (string.IsNullOrWhiteSpace(fullName))
        {
            error = "Vui lòng nhập họ tên.";
            return null;
        }

        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            error = "Vui lòng nhập số điện thoại.";
            return null;
        }

        if (provinceId <= 0 || string.IsNullOrWhiteSpace(provinceName))
        {
            error = "Vui lòng chọn Tỉnh/Thành phố.";
            return null;
        }

        if (districtId <= 0 || string.IsNullOrWhiteSpace(districtName))
        {
            error = "Vui lòng chọn Quận/Huyện.";
            return null;
        }

        if (string.IsNullOrWhiteSpace(wardCode) || string.IsNullOrWhiteSpace(wardName))
        {
            error = "Vui lòng chọn Phường/Xã.";
            return null;
        }

        if (string.IsNullOrWhiteSpace(streetAddress))
        {
            error = "Vui lòng nhập địa chỉ (số nhà, tên đường).";
            return null;
        }

        if (fullName.Length > 150) fullName = fullName[..150];
        if (phoneNumber.Length > 20) phoneNumber = phoneNumber[..20];
        if (provinceName.Length > 100) provinceName = provinceName[..100];
        if (districtName.Length > 100) districtName = districtName[..100];
        if (wardCode.Length > 50) wardCode = wardCode[..50];
        if (wardName.Length > 100) wardName = wardName[..100];
        if (streetAddress.Length > 500) streetAddress = streetAddress[..500];

        return new NormalizedAddress(fullName, phoneNumber, provinceId, provinceName, districtId, districtName, wardCode, wardName, streetAddress);
    }

    private async Task CancelExpiredOrdersForCustomerAsync(int accountId, CancellationToken cancellationToken)
    {
        var cutoff = DateTime.Now.AddMinutes(-15);
        var expiredOrders = await _db.Orders
            .Where(o => o.CustomerId == accountId
                && o.PaymentMethod == "BankTransfer"
                && o.PaymentStatus == "AwaitingPayment"
                && o.OrderStatus != "Cancelled"
                && o.CreatedAt <= cutoff)
            .ToListAsync(cancellationToken);

        if (expiredOrders.Count > 0)
        {
            foreach (var order in expiredOrders)
            {
                order.PaymentStatus = "Expired";
                order.OrderStatus = "Cancelled";
                order.UpdatedAt = DateTime.UtcNow;

                _db.OrderTrackingLogs.Add(new OrderTrackingLog
                {
                    OrderId = order.Id,
                    Status = "Cancelled",
                    Note = "Hệ thống tự động hủy do hết thời gian thanh toán",
                    CreatedAt = DateTime.UtcNow
                });
            }

            await _db.SaveChangesAsync(cancellationToken);
        }
    }

    [HttpPost("/my-account/submit-review")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SubmitReview(int productId, int rating, string comment, List<IFormFile>? reviewImages, CancellationToken cancellationToken)
    {
        if (!(User?.Identity?.IsAuthenticated ?? false))
        {
            return Json(new { success = false, error = "Bạn chưa đăng nhập." });
        }

        var idRaw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(idRaw, out var accountId) || accountId <= 0)
        {
            return Json(new { success = false, error = "Tài khoản không hợp lệ." });
        }

        if (rating < 1 || rating > 5)
        {
            return Json(new { success = false, error = "Số sao đánh giá phải từ 1 đến 5." });
        }

        comment = comment?.Trim() ?? "";

        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == productId, cancellationToken);
        if (product == null)
        {
            return Json(new { success = false, error = "Sản phẩm không tồn tại." });
        }

        // Verify if customer has bought this product and capture purchased variant label
        var purchasedOrder = await _db.Orders
            .AsNoTracking()
            .Include(o => o.Items)
                .ThenInclude(i => i.Variant)
                    .ThenInclude(v => v!.OptionValue1)
            .Include(o => o.Items)
                .ThenInclude(i => i.Variant)
                    .ThenInclude(v => v!.OptionValue2)
            .Where(o => o.CustomerId == accountId 
                && (o.OrderStatus == "Completed" || o.OrderStatus == "success" || o.OrderStatus == "delivered" || o.OrderStatus == "Đã giao" || o.OrderStatus == "Hoàn tất") 
                && o.Items.Any(i => i.Variant != null && i.Variant.ProductId == productId))
            .OrderByDescending(o => o.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (purchasedOrder == null)
        {
            return Json(new { success = false, error = "Bạn chỉ có thể đánh giá sản phẩm đã mua và giao hàng thành công." });
        }

        string? purchasedVariantLabel = null;
        var purchasedItem = purchasedOrder.Items.FirstOrDefault(i => i.Variant != null && i.Variant.ProductId == productId);
        if (purchasedItem?.Variant != null)
        {
            var optParts = new List<string>();
            if (!string.IsNullOrWhiteSpace(purchasedItem.Variant.OptionValue1?.Value))
                optParts.Add(purchasedItem.Variant.OptionValue1.Value.Trim());
            if (!string.IsNullOrWhiteSpace(purchasedItem.Variant.OptionValue2?.Value))
                optParts.Add(purchasedItem.Variant.OptionValue2.Value.Trim());

            if (optParts.Count > 0)
                purchasedVariantLabel = string.Join(" - ", optParts);
            else if (!string.IsNullOrWhiteSpace(purchasedItem.Variant.Sku))
                purchasedVariantLabel = purchasedItem.Variant.Sku;
        }

        // Save uploaded review images regardless of comment length (up to 5 photos)
        var uploadedUrls = new List<string>();
        if (reviewImages != null && reviewImages.Any(img => img.Length > 0))
        {
            try
            {
                var uploadsDir = Path.Combine(_env.WebRootPath, "uploads", "reviews");
                if (!Directory.Exists(uploadsDir))
                {
                    Directory.CreateDirectory(uploadsDir);
                }
                foreach (var img in reviewImages.Where(x => x.Length > 0).Take(5))
                {
                    var ext = Path.GetExtension(img.FileName);
                    if (string.IsNullOrWhiteSpace(ext)) ext = ".jpg";
                    var fileName = $"{Guid.NewGuid()}{ext}";
                    var filePath = Path.Combine(uploadsDir, fileName);
                    using (var stream = new FileStream(filePath, FileMode.Create))
                    {
                        await img.CopyToAsync(stream, cancellationToken);
                    }
                    uploadedUrls.Add($"/uploads/reviews/{fileName}");
                }
            }
            catch (Exception)
            {
                // Non-blocking
            }
        }

        string? mediaJson = uploadedUrls.Count > 0
            ? System.Text.Json.JsonSerializer.Serialize(uploadedUrls)
            : null;

        // Check if already reviewed
        var existing = await _db.ProductReviews
            .FirstOrDefaultAsync(r => r.AccountId == accountId && r.ProductId == productId, cancellationToken);

        if (existing != null)
        {
            existing.Rating = rating;
            existing.Comment = comment;
            if (!string.IsNullOrWhiteSpace(purchasedVariantLabel))
                existing.VariantName = purchasedVariantLabel;
            if (mediaJson != null)
                existing.MediaUrlsJson = mediaJson;
            existing.UpdatedAt = DateTime.UtcNow;
            _db.ProductReviews.Update(existing);
        }
        else
        {
            var review = new ProductReview
            {
                ProductId = productId,
                AccountId = accountId,
                Rating = rating,
                Comment = comment,
                VariantName = purchasedVariantLabel,
                MediaUrlsJson = mediaJson,
                IsHidden = false,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            await _db.ProductReviews.AddAsync(review, cancellationToken);
        }

        await _db.SaveChangesAsync(cancellationToken);

        // Recalculate average rating and review count for visible reviews only
        var reviews = await _db.ProductReviews
            .Where(r => r.ProductId == productId && !r.IsHidden)
            .Select(r => r.Rating)
            .ToListAsync(cancellationToken);

        if (reviews.Count > 0)
        {
            product.ReviewCount = reviews.Count;
            product.AverageRating = Math.Round((decimal)reviews.Average(), 2);
        }
        else
        {
            product.ReviewCount = 0;
            product.AverageRating = 0m;
        }

        _db.Products.Update(product);
        await _db.SaveChangesAsync(cancellationToken);

        // --- Coin Reward Logic: 1+ image & >= 50 characters ---
        bool hasImage = uploadedUrls.Count > 0;
        int commentLen = comment?.Trim().Length ?? 0;
        bool rewarded = false;

        if (hasImage && commentLen >= 50)
        {
            // Reward coins via WalletService
            var wallet = await _walletService.GetOrCreateWalletAsync(accountId, cancellationToken);
            string refId = $"REVIEW-BONUS-{productId}-{accountId}";
            bool alreadyRewarded = await _db.WalletTransactions.AnyAsync(
                t => t.WalletId == wallet.WalletId && t.ReferenceId == refId, cancellationToken);

            if (!alreadyRewarded)
            {
                await using var dbTx = await _db.Database.BeginTransactionAsync(cancellationToken);
                try
                {
                    wallet.Balance += 500m;
                    wallet.UpdatedAt = DateTime.UtcNow;

                    _db.WalletTransactions.Add(new WalletTransaction
                    {
                        WalletId        = wallet.WalletId,
                        Amount          = 500m,
                        TransactionType = WalletService.TypeCashback,
                        ReferenceId     = refId,
                        Description     = $"Thưởng đánh giá sản phẩm {product.Name} (+500 xu)",
                        BalanceAfter    = wallet.Balance,
                        CreatedAt       = DateTime.UtcNow
                    });

                    await _db.SaveChangesAsync(cancellationToken);
                    await dbTx.CommitAsync(cancellationToken);
                    rewarded = true;
                }
                catch (Exception)
                {
                    await dbTx.RollbackAsync(cancellationToken);
                }
            }
        }

        string successMsg = "Cảm ơn bạn đã gửi đánh giá thành công!";
        if (rewarded)
        {
            successMsg += " Bạn đã được nhận thưởng 500 xu vào ví số dư!";
        }

        return Json(new { success = true, message = successMsg });
    }

    private sealed record NormalizedAddress(
        string FullName,
        string PhoneNumber,
        int ProvinceId,
        string ProvinceName,
        int DistrictId,
        string DistrictName,
        string WardCode,
        string WardName,
        string StreetAddress);

    // ─────────────────────────────────────────────────────────────────────────
    //  RETURN REQUEST
    // ─────────────────────────────────────────────────────────────────────────

    private static bool IsDeliveredStatus(string? rawStatus)
    {
        var s = (rawStatus ?? string.Empty).Trim().Replace(" ", string.Empty).ToLowerInvariant();
        return s is "delivered" or "deliverysuccess" or "dagiao" or "đãgiao" or "completed" or "success";
    }

    [HttpGet("/my-orders/return/{orderId:int}")]
    public async Task<IActionResult> ReturnRequest(int orderId, CancellationToken cancellationToken)
    {
        if (orderId <= 0) return NotFound();

        if (!(User?.Identity?.IsAuthenticated ?? false))
            return Redirect("/auth/login");

        var idRaw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(idRaw, out var accountId) || accountId <= 0)
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Redirect("/auth/login");
        }

        var order = await _db.Orders
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == orderId && o.CustomerId == accountId, cancellationToken);

        if (order is null) return NotFound();

        var normStatus = (order.OrderStatus ?? string.Empty).Trim().Replace(" ", string.Empty).ToLowerInvariant();
        var isReturnState = normStatus is "returnrequest" or "returning" or "return" or "returned" or "refund" or "trahang" or "trảhàng";

        if (!IsDeliveredStatus(order.OrderStatus) && !isReturnState)
        {
            TempData["AccountError"] = "Chỉ có thể trả hàng sau khi đơn đã được giao thành công.";
            return RedirectToAction("OrderDetails", "Account", new { id = orderId });
        }

        var hasNonReturnableItem = await _db.OrderItems
            .AsNoTracking()
            .AnyAsync(i => i.OrderId == orderId && i.Variant != null && i.Variant.Product != null && !i.Variant.Product.IsReturnable, cancellationToken);
        if (hasNonReturnableItem && !isReturnState)
        {
            TempData["AccountError"] = "Đơn hàng có sản phẩm thuộc ngành hàng không hỗ trợ đổi trả.";
            return RedirectToAction("OrderDetails", "Account", new { id = orderId });
        }

        // Find the delivered tracking log for timestamp
        var deliveredLog = await _db.OrderTrackingLogs
            .AsNoTracking()
            .Where(l => l.OrderId == orderId)
            .Where(l => l.Status != null && (
                EF.Functions.Like(l.Status, "%delivered%") ||
                EF.Functions.Like(l.Status, "%dagiao%") ||
                EF.Functions.Like(l.Status, "%completed%") ||
                EF.Functions.Like(l.Status, "%success%")))
            .OrderByDescending(l => l.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        var deliveredAt = deliveredLog?.CreatedAt ?? order.UpdatedAt;
        var nowUtc = DateTime.UtcNow;
        var daysSinceDelivery = (nowUtc - deliveredAt).TotalDays;
        var daysRemaining = (int)Math.Max(0, Math.Ceiling(5 - daysSinceDelivery));
        var canReturn = daysSinceDelivery <= 5;

        // Check existing return request
        var existingRr = await _db.ReturnRequests
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.OrderId == orderId && r.CustomerId == accountId, cancellationToken);

        var vm = new ReturnRequestVm
        {
            OrderId = order.Id,
            OrderCode = order.OrderCode,
            ShippingAddress = order.ShippingAddress,
            RecipientName = order.RecipientName,
            RecipientPhone = order.RecipientPhone,
            DeliveredAt = deliveredAt,
            DaysRemaining = daysRemaining,
            CanReturn = canReturn,
            ExistingRequest = existingRr is null ? null : new ReturnRequestStatusVm
            {
                Id = existingRr.Id,
                Status = existingRr.Status,
                Reason = existingRr.Reason,
                MediaUrls = ParseMediaUrls(existingRr.MediaUrlsJson),
                RejectionNote = existingRr.RejectionNote,
                ReturnTrackingCode = existingRr.ReturnTrackingCode,
                EstimatedPickupTime = existingRr.EstimatedPickupTime,
                CreatedAt = existingRr.CreatedAt,
                UpdatedAt = existingRr.UpdatedAt
            }
        };

        return View("ReturnRequest", vm);
    }

    private static List<string> ParseMediaUrls(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new List<string>();
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
        }
        catch
        {
            return new List<string>();
        }
    }

    [HttpPost("/my-orders/return/upload-media")]
    public async Task<IActionResult> UploadReturnMedia(IFormFile file, CancellationToken cancellationToken)
    {
        if (!(User?.Identity?.IsAuthenticated ?? false))
            return Unauthorized(new { message = "Unauthorized" });

        if (file is null || file.Length <= 0)
            return BadRequest(new { message = "Không có file được chọn." });

        const long maxBytes = 20 * 1024 * 1024; // 20MB (support videos)
        if (file.Length > maxBytes)
            return BadRequest(new { message = "File quá lớn. Tối đa 20MB." });

        var ext = Path.GetExtension(file.FileName)?.ToLowerInvariant() ?? string.Empty;
        var allowedExts = new[] { ".jpg", ".jpeg", ".png", ".webp", ".gif", ".mp4", ".mov", ".avi", ".webm" };
        if (!allowedExts.Contains(ext))
            return BadRequest(new { message = "Định dạng file không được hỗ trợ." });

        var webRoot = string.IsNullOrWhiteSpace(_env.WebRootPath)
            ? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot")
            : _env.WebRootPath;

        var returnMediaDir = Path.Combine(webRoot, "uploads", "returns");
        Directory.CreateDirectory(returnMediaDir);

        var fileName = $"{Guid.NewGuid():N}{ext}";
        var fullPath = Path.Combine(returnMediaDir, fileName);

        await using (var fs = System.IO.File.Create(fullPath))
        {
            await file.CopyToAsync(fs, cancellationToken);
        }

        var url = $"/uploads/returns/{fileName}";
        return Ok(new { url });
    }

    [HttpPost("/my-orders/return/submit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SubmitReturnRequest([FromForm] int orderId, [FromForm] string reason, [FromForm] string? mediaUrlsJson, CancellationToken cancellationToken)
    {
        var returnUrl = $"/my-orders/return/{orderId}";

        if (!(User?.Identity?.IsAuthenticated ?? false))
            return Redirect("/auth/login");

        var idRaw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(idRaw, out var accountId) || accountId <= 0)
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Redirect("/auth/login");
        }

        if (orderId <= 0)
        {
            TempData["AccountError"] = "Đơn hàng không hợp lệ.";
            return Redirect(returnUrl);
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            TempData["AccountError"] = "Vui lòng nhập lý do trả hàng.";
            return Redirect(returnUrl);
        }

        var order = await _db.Orders
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == orderId && o.CustomerId == accountId, cancellationToken);

        if (order is null)
        {
            TempData["AccountError"] = "Không tìm thấy đơn hàng.";
            return Redirect(returnUrl);
        }

        var normStatus = (order.OrderStatus ?? string.Empty).Trim().Replace(" ", string.Empty).ToLowerInvariant();
        var isReturnState = normStatus is "returnrequest" or "returning" or "return" or "returned" or "refund" or "trahang" or "trảhàng";

        if (!IsDeliveredStatus(order.OrderStatus) && !isReturnState)
        {
            TempData["AccountError"] = "Chỉ có thể trả hàng sau khi đơn đã được giao thành công.";
            return Redirect(returnUrl);
        }

        var hasNonReturnableItem = await _db.OrderItems
            .AsNoTracking()
            .AnyAsync(i => i.OrderId == orderId && i.Variant != null && i.Variant.Product != null && !i.Variant.Product.IsReturnable, cancellationToken);
        if (hasNonReturnableItem)
        {
            TempData["AccountError"] = "Đơn hàng có sản phẩm không hỗ trợ đổi trả.";
            return Redirect(returnUrl);
        }

        // Check if within 5 days of delivery
        var deliveredLog = await _db.OrderTrackingLogs
            .AsNoTracking()
            .Where(l => l.OrderId == orderId)
            .Where(l => l.Status != null && (
                EF.Functions.Like(l.Status, "%delivered%") ||
                EF.Functions.Like(l.Status, "%dagiao%") ||
                EF.Functions.Like(l.Status, "%completed%") ||
                EF.Functions.Like(l.Status, "%success%")))
            .OrderByDescending(l => l.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        var deliveredAt = deliveredLog?.CreatedAt ?? order.UpdatedAt;
        var daysSinceDelivery = (DateTime.UtcNow - deliveredAt).TotalDays;

        if (daysSinceDelivery > 5)
        {
            TempData["AccountError"] = "Đã quá 5 ngày kể từ khi nhận hàng. Không thể gửi yêu cầu trả hàng.";
            return Redirect(returnUrl);
        }

        // Check if already has a return request
        var existing = await _db.ReturnRequests
            .FirstOrDefaultAsync(r => r.OrderId == orderId && r.CustomerId == accountId, cancellationToken);

        if (existing is not null)
        {
            if (existing.Status == "Pending" || existing.Status == "Approved")
            {
                TempData["AccountError"] = "Bạn đã gửi yêu cầu trả hàng cho đơn này rồi.";
                return Redirect(returnUrl);
            }

            // If rejected, allow re-submit
            existing.Reason = reason.Trim();
            existing.MediaUrlsJson = mediaUrlsJson;
            existing.Status = "Pending";
            existing.RejectionNote = null;
            existing.ReturnTrackingCode = null;
            existing.EstimatedPickupTime = null;
            existing.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            _db.ReturnRequests.Add(new ReturnRequest
            {
                OrderId = orderId,
                CustomerId = accountId,
                Reason = reason.Trim(),
                MediaUrlsJson = mediaUrlsJson,
                Status = "Pending",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });

            // Update order status to ReturnRequest
            var orderToUpdate = await _db.Orders.FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);
            if (orderToUpdate is not null)
            {
                orderToUpdate.OrderStatus = "ReturnRequest";
                orderToUpdate.UpdatedAt = DateTime.UtcNow;

                _db.OrderTrackingLogs.Add(new OrderTrackingLog
                {
                    OrderId = orderId,
                    Status = "ReturnRequest",
                    Note = "Khách hàng gửi yêu cầu trả hàng. Đang chờ shop duyệt.",
                    CreatedAt = DateTime.UtcNow
                });
            }
        }

        _db.SystemNotifications.Add(new SystemNotification
        {
            Title = $"Yêu cầu trả hàng/hoàn tiền mới - Đơn #{order.OrderCode}",
            Content = $"Khách hàng {order.RecipientName} vừa gửi yêu cầu trả hàng/hoàn tiền cho đơn #{order.OrderCode}. Lý do: {reason.Trim()}. Vui lòng vào Kênh Người Bán để kiểm tra và phản hồi.",
            TargetType = "Account",
            TargetAccountId = order.ShopId,
            SentByAdminId = accountId,
            CreatedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync(cancellationToken);

        TempData["AccountSuccess"] = "Yêu cầu trả hàng đã được gửi. Vui lòng chờ shop xem xét và duyệt.";
        return Redirect(returnUrl);
    }
}
