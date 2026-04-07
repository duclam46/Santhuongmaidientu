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

namespace DOAN_BANHANG.Controllers;

public class AccountController : Controller
{
    private readonly DataContext _db;
    private readonly IPasswordHasher<Account> _passwordHasher;
    private readonly IWebHostEnvironment _env;
    private readonly GhnClient _ghn;
    private readonly GhnOptions _ghnOptions;

    [ActivatorUtilitiesConstructor]
    public AccountController(DataContext db, IPasswordHasher<Account> passwordHasher, IWebHostEnvironment env, GhnClient ghn, IOptions<GhnOptions> ghnOptions)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _env = env;
        _ghn = ghn;
        _ghnOptions = ghnOptions.Value;
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

        // Remove seller-internal pickup shift info from customer UI.
        const string marker = "Ca lấy:";
        var idx = note.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return note;

        // Also trim a preceding punctuation/space if present (e.g. ". Ca lấy: ...").
        var left = note[..idx].TrimEnd();
        if (left.EndsWith(".", StringComparison.Ordinal)) left = left[..^1].TrimEnd();
        return string.IsNullOrWhiteSpace(left) ? null : left;
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
                OrderStatus = o.OrderStatus,
                PaymentStatus = o.PaymentStatus,
                FinalAmount = o.FinalAmount,
                ShippingFee = o.ShippingFee,
                ItemCount = o.Items.Count,
                CreatedAt = o.CreatedAt
            })
            .ToListAsync(cancellationToken);

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
                OrderStatus = o.OrderStatus,
                PaymentStatus = o.PaymentStatus,
                FinalAmount = o.FinalAmount,
                ShippingFee = o.ShippingFee,
                ItemCount = o.Items.Count,
                CreatedAt = o.CreatedAt
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
        order.UpdatedAt = DateTime.UtcNow;

        _db.OrderTrackingLogs.Add(new OrderTrackingLog
        {
            OrderId = order.Id,
            Status = order.OrderStatus,
            Note = "Người mua hủy đơn hàng",
            CreatedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync(cancellationToken);
        TempData["AccountSuccess"] = "Đã hủy đơn hàng.";
        return Redirect(returnUrl);
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

        var order = await _db.Orders
            .AsNoTracking()
            .Include(o => o.Items)
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

        var vm = new CustomerOrderDetailsVm
        {
            Id = order.Id,
            OrderCode = order.OrderCode,
            OrderStatus = order.OrderStatus,
            PaymentMethod = order.PaymentMethod,
            PaymentStatus = order.PaymentStatus,
            SubTotal = order.SubTotal,
            ShippingFee = order.ShippingFee,
            DiscountAmount = order.DiscountAmount,
            FinalAmount = order.FinalAmount,
            RecipientName = order.RecipientName,
            RecipientPhone = order.RecipientPhone,
            ShippingAddress = order.ShippingAddress,
            TrackingCode = order.TrackingCode,
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
                    UnitPrice = i.UnitPrice,
                    Quantity = i.Quantity,
                    LineTotal = i.LineTotal
                })
                .ToList(),
            TrackingLogs = logs
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
}
