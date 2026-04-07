using System.Security.Claims;
using DOAN_BANHANG.Models;
using DOAN_BANHANG.Services;
using DOAN_BANHANG.ViewModels;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DOAN_BANHANG.Areas.Admin.Controllers;

[Area("Admin")]
[Route("Admin/[controller]/[action]")]
public class OrdersController : Controller
{
    private readonly DataContext _db;
    private readonly GhnClient _ghn;
    private readonly GhnOptions _ghnOptions;

    public OrdersController(DataContext db, GhnClient ghn, IOptions<GhnOptions> ghnOptions)
    {
        _db = db;
        _ghn = ghn;
        _ghnOptions = ghnOptions.Value;
    }

    private static UserAddress? PickBestSenderAddress(IEnumerable<UserAddress> addrs)
        => addrs
            .OrderByDescending(x => x.AddressType == AddressType.Shop)
            .ThenByDescending(x => x.IsDefault)
            .ThenByDescending(x => x.UpdatedAt)
            .FirstOrDefault();

    private static string ResolveGhnPrintHost(string baseUrl)
        => baseUrl.Contains("dev-online-gateway.ghn.vn", StringComparison.OrdinalIgnoreCase)
            ? "https://dev-online-gateway.ghn.vn"
            : "https://online-gateway.ghn.vn";

    private bool TryGetSignedInAccount(out int accountId, out string? role)
    {
        role = null;
        accountId = 0;

        if (!(User?.Identity?.IsAuthenticated ?? false)) return false;

        var idRaw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(idRaw, out accountId) || accountId <= 0) return false;

        role = User.FindFirstValue(ClaimTypes.Role);
        return true;
    }

    private IQueryable<Order> ScopedOrders(int? accountId, string? role)
    {
        var q = _db.Orders.AsQueryable();

        // Admin area should not leak data to anonymous users.
        if (!accountId.HasValue || accountId.Value <= 0)
        {
            return q.Where(_ => false);
        }

        if (string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase))
        {
            return q;
        }

        if (string.Equals(role, "Shop", StringComparison.OrdinalIgnoreCase))
        {
            return q.Where(o => o.ShopId == accountId.Value);
        }

        // Other roles (e.g., Customer) do not have access.
        return q.Where(_ => false);
    }

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] AdminOrderListQueryVm query, CancellationToken cancellationToken)
    {
        int? accountId = null;
        string? role = null;
        if (TryGetSignedInAccount(out var id, out var r))
        {
            accountId = id;
            role = r;
        }

        var q = ScopedOrders(accountId, role)
            .AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            q = q.Where(o => o.OrderCode.Contains(term));
        }

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            var status = query.Status.Trim();
            q = q.Where(o => o.OrderStatus == status);
        }

        var totalItems = await q.CountAsync(cancellationToken);

        var page = query.Page <= 0 ? 1 : query.Page;
        var pageSize = query.PageSize <= 0 ? 20 : Math.Min(query.PageSize, 50);
        var skip = (page - 1) * pageSize;

        var orders = await q
            .OrderByDescending(o => o.CreatedAt)
            .Skip(skip)
            .Take(pageSize)
            .Select(o => new AdminOrderListItemVm
            {
                Id = o.Id,
                OrderCode = o.OrderCode,
                OrderStatus = o.OrderStatus,
                PaymentStatus = o.PaymentStatus,
                FinalAmount = o.FinalAmount,
                ItemCount = o.Items.Count,
                CreatedAt = o.CreatedAt,
                CustomerEmail = o.Customer != null ? o.Customer.Email : null,
                RecipientName = o.RecipientName,
                RecipientPhone = o.RecipientPhone
            })
            .ToListAsync(cancellationToken);

        var vm = new AdminOrderListVm
        {
            Query = new AdminOrderListQueryVm
            {
                Search = query.Search,
                Status = query.Status,
                Page = page,
                PageSize = pageSize
            },
            Orders = orders,
            TotalItems = totalItems
        };

        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken)
    {
        if (id <= 0) return NotFound();

        int? accountId = null;
        string? role = null;
        if (TryGetSignedInAccount(out var signedInAccountId, out var r))
        {
            accountId = signedInAccountId;
            role = r;
        }

        var order = await ScopedOrders(accountId, role)
            .AsNoTracking()
            .Include(o => o.Customer)
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

        if (order is null) return NotFound();

        string? cancellationNote = null;
        if (string.Equals(order.OrderStatus, "Cancelled", StringComparison.OrdinalIgnoreCase)
            || string.Equals(order.OrderStatus, "Canceled", StringComparison.OrdinalIgnoreCase))
        {
            cancellationNote = await _db.OrderTrackingLogs
                .AsNoTracking()
                .Where(x => x.OrderId == order.Id)
                .Where(x => x.Status == "Cancelled" || x.Status == "Canceled")
                .OrderByDescending(x => x.CreatedAt)
                .Select(x => x.Note)
                .FirstOrDefaultAsync(cancellationToken);

            if (string.IsNullOrWhiteSpace(cancellationNote)) cancellationNote = null;
        }

        string? pickupShiftTitle = null;
        var pickupShiftNote = await _db.OrderTrackingLogs
            .AsNoTracking()
            .Where(x => x.OrderId == order.Id)
            .Where(x => x.Note != null && EF.Functions.Like(x.Note, "%Ca lấy:%"))
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => x.Note)
            .FirstOrDefaultAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(pickupShiftNote))
        {
            const string marker = "Ca lấy:";
            var idx = pickupShiftNote.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                pickupShiftTitle = pickupShiftNote[(idx + marker.Length)..].Trim();
                if (string.IsNullOrWhiteSpace(pickupShiftTitle)) pickupShiftTitle = null;
            }
        }

        var vm = new AdminOrderDetailsVm
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
            PickupShiftTitle = pickupShiftTitle,
            CreatedAt = order.CreatedAt,
            UpdatedAt = order.UpdatedAt,
            CustomerEmail = order.Customer?.Email,
            CancellationNote = cancellationNote,
            Items = order.Items
                .OrderBy(i => i.Id)
                .Select(i => new AdminOrderItemVm
                {
                    Id = i.Id,
                    ProductName = i.ProductName,
                    ColorName = i.ColorName,
                    SizeName = i.SizeName,
                    UnitPrice = i.UnitPrice,
                    Quantity = i.Quantity,
                    LineTotal = i.LineTotal
                })
                .ToList()
        };

        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Confirm(AdminOrderDecisionVm model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return RedirectToAction(nameof(Details), new { id = model.Id });

        if (!TryGetSignedInAccount(out var accountId, out var role))
        {
            TempData["AdminToast"] = "Vui lòng đăng nhập để xác nhận đơn hàng.";
            return RedirectToAction(nameof(Details), new { id = model.Id });
        }

        if (!string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(role, "Shop", StringComparison.OrdinalIgnoreCase))
        {
            TempData["AdminToast"] = "Bạn không có quyền xác nhận đơn hàng.";
            return RedirectToAction(nameof(Details), new { id = model.Id });
        }

        var order = await ScopedOrders(accountId, role)
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == model.Id, cancellationToken);

        if (order is null) return NotFound();

        if (!string.Equals(order.OrderStatus, "Pending", StringComparison.OrdinalIgnoreCase))
        {
            TempData["AdminToast"] = "Đơn hàng không còn ở trạng thái chờ xác nhận.";
            return RedirectToAction(nameof(Details), new { id = order.Id });
        }

        // Create GHN shipping order on first confirmation.
        if (string.IsNullOrWhiteSpace(order.TrackingCode))
        {
            if (order.ShippingDistrictId is null || order.ShippingDistrictId <= 0
                || string.IsNullOrWhiteSpace(order.ShippingWardCode))
            {
                TempData["AdminToast"] = "Thiếu District/Ward của địa chỉ giao hàng (GHN). Vui lòng đặt lại đơn hoặc cập nhật logic lưu địa chỉ.";
                return RedirectToAction(nameof(Details), new { id = order.Id });
            }

            if (string.IsNullOrWhiteSpace(_ghnOptions.Token) || _ghnOptions.ShopId <= 0)
            {
                TempData["AdminToast"] = "Chưa cấu hình GHN Token/ShopId nên không thể tạo vận đơn.";
                return RedirectToAction(nameof(Details), new { id = order.Id });
            }

            var senderAddresses = await _db.UserAddresses
                .AsNoTracking()
                .Where(a => a.AccountId == order.ShopId)
                .ToListAsync(cancellationToken);

            var sender = PickBestSenderAddress(senderAddresses);
            if (sender is null)
            {
                TempData["AdminToast"] = "Shop chưa có địa chỉ gửi hàng nên không thể tạo vận đơn GHN.";
                return RedirectToAction(nameof(Details), new { id = order.Id });
            }

            var totalQty = order.Items.Sum(i => i.Quantity);
            if (totalQty <= 0) totalQty = 1;

            var baseWeight = _ghnOptions.DefaultWeightGram <= 0 ? 500 : _ghnOptions.DefaultWeightGram;
            var weight = Math.Min(50000, Math.Max(1, baseWeight * totalQty));

            var length = _ghnOptions.DefaultLengthCm <= 0 ? 20 : _ghnOptions.DefaultLengthCm;
            var width = _ghnOptions.DefaultWidthCm <= 0 ? 15 : _ghnOptions.DefaultWidthCm;
            var height = _ghnOptions.DefaultHeightCm <= 0 ? 10 : _ghnOptions.DefaultHeightCm;

            var codAmount = string.Equals(order.PaymentMethod, "COD", StringComparison.OrdinalIgnoreCase)
                ? (int)Math.Max(0, Math.Round(order.FinalAmount, MidpointRounding.AwayFromZero))
                : 0;

            var req = new GhnCreateOrderRequest
            {
                ServiceTypeId = _ghnOptions.ServiceTypeId <= 0 ? 2 : _ghnOptions.ServiceTypeId,
                PaymentTypeId = 2,
                Note = string.IsNullOrWhiteSpace(model.Note) ? null : model.Note.Trim(),
                RequiredNote = "KHONGCHOXEMHANG",
                FromName = sender.FullName,
                FromPhone = sender.PhoneNumber,
                FromAddress = sender.StreetAddress,
                FromDistrictId = sender.DistrictId,
                FromWardCode = sender.WardCode,
                ToName = order.RecipientName,
                ToPhone = order.RecipientPhone,
                ToAddress = order.ShippingStreetAddress ?? order.ShippingAddress,
                ToDistrictId = order.ShippingDistrictId.Value,
                ToWardCode = order.ShippingWardCode!,
                CodAmount = codAmount,
                Content = $"Don {order.OrderCode}",
                Weight = weight,
                Length = length,
                Width = width,
                Height = height,
                InsuranceValue = (int)Math.Max(0, Math.Round(order.SubTotal, MidpointRounding.AwayFromZero)),
                Items = order.Items
                    .Select(i => new GhnCreateOrderItem
                    {
                        Name = i.ProductName,
                        Quantity = Math.Max(1, i.Quantity),
                        Price = (int)Math.Max(0, Math.Round(i.UnitPrice, MidpointRounding.AwayFromZero))
                    })
                    .ToList()
            };

            try
            {
                var ghnOrder = await _ghn.CreateShippingOrderAsync(req, cancellationToken);
                if (string.IsNullOrWhiteSpace(ghnOrder.OrderCode))
                {
                    TempData["AdminToast"] = "GHN không trả về mã vận đơn.";
                    return RedirectToAction(nameof(Details), new { id = order.Id });
                }

                order.TrackingCode = ghnOrder.OrderCode.Trim();
            }
            catch (Exception ex)
            {
                TempData["AdminToast"] = $"Tạo vận đơn GHN thất bại: {ex.Message}";
                return RedirectToAction(nameof(Details), new { id = order.Id });
            }
        }

        string? pickupShiftTitle = null;
        try
        {
            var shifts = await _ghn.GetPickupShiftsAsync(cancellationToken);
            pickupShiftTitle = shifts
                .OrderBy(s => s.FromTime)
                .Select(s => s.Title)
                .FirstOrDefault(t => !string.IsNullOrWhiteSpace(t));
        }
        catch
        {
            // Best-effort only: do not block confirmation if GHN shift API fails.
        }

        order.OrderStatus = "Confirmed";
        order.UpdatedAt = DateTime.UtcNow;

        var note = string.IsNullOrWhiteSpace(model.Note)
            ? $"Shop xác nhận đơn hàng. GHN={order.TrackingCode}" + (string.IsNullOrWhiteSpace(pickupShiftTitle) ? string.Empty : $". Ca lấy: {pickupShiftTitle}")
            : model.Note.Trim();

        _db.OrderTrackingLogs.Add(new OrderTrackingLog
        {
            OrderId = order.Id,
            Status = order.OrderStatus,
            Note = note,
            CreatedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync(cancellationToken);
        TempData["AdminToast"] = string.IsNullOrWhiteSpace(order.TrackingCode)
            ? "Đã xác nhận đơn hàng."
            : $"Đã xác nhận đơn hàng. Mã vận đơn GHN: {order.TrackingCode}" + (string.IsNullOrWhiteSpace(pickupShiftTitle) ? string.Empty : $" | Ca lấy: {pickupShiftTitle}");
        return RedirectToAction(nameof(Details), new { id = order.Id });
    }

    [HttpGet]
    public async Task<IActionResult> PrintLabel(int id, [FromQuery] string? format, CancellationToken cancellationToken)
    {
        if (id <= 0) return NotFound();

        int? accountId = null;
        string? role = null;
        if (TryGetSignedInAccount(out var signedInAccountId, out var r))
        {
            accountId = signedInAccountId;
            role = r;
        }

        var order = await ScopedOrders(accountId, role)
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

        if (order is null) return NotFound();

        if (string.IsNullOrWhiteSpace(order.TrackingCode))
        {
            TempData["AdminToast"] = "Đơn hàng chưa có mã vận đơn GHN.";
            return RedirectToAction(nameof(Details), new { id = order.Id });
        }

        try
        {
            var token = await _ghn.GenerateA5TokenAsync(new[] { order.TrackingCode }, cancellationToken);
            var host = ResolveGhnPrintHost(_ghnOptions.BaseUrl ?? string.Empty);

            var fmt = (format ?? "A5").Trim().ToUpperInvariant();
            var url = fmt switch
            {
                "80" or "80X80" => $"{host}/a5/public-api/print80x80?token={Uri.EscapeDataString(token)}",
                "52" or "52X70" or "50X72" => $"{host}/a5/public-api/print52x70?token={Uri.EscapeDataString(token)}",
                _ => $"{host}/a5/public-api/printA5?token={Uri.EscapeDataString(token)}",
            };

            return Redirect(url);
        }
        catch (Exception ex)
        {
            TempData["AdminToast"] = $"Không in được phiếu GHN: {ex.Message}";
            return RedirectToAction(nameof(Details), new { id = order.Id });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reject(AdminOrderDecisionVm model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return RedirectToAction(nameof(Details), new { id = model.Id });

        if (!TryGetSignedInAccount(out var accountId, out var role))
        {
            TempData["AdminToast"] = "Vui lòng đăng nhập để từ chối đơn hàng.";
            return RedirectToAction(nameof(Details), new { id = model.Id });
        }

        if (!string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(role, "Shop", StringComparison.OrdinalIgnoreCase))
        {
            TempData["AdminToast"] = "Bạn không có quyền từ chối đơn hàng.";
            return RedirectToAction(nameof(Details), new { id = model.Id });
        }

        var order = await ScopedOrders(accountId, role)
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == model.Id, cancellationToken);

        if (order is null) return NotFound();

        if (!string.Equals(order.OrderStatus, "Pending", StringComparison.OrdinalIgnoreCase))
        {
            TempData["AdminToast"] = "Đơn hàng không còn ở trạng thái chờ xác nhận.";
            return RedirectToAction(nameof(Details), new { id = order.Id });
        }

        order.OrderStatus = "Rejected";
        order.UpdatedAt = DateTime.UtcNow;

        _db.OrderTrackingLogs.Add(new OrderTrackingLog
        {
            OrderId = order.Id,
            Status = order.OrderStatus,
            Note = string.IsNullOrWhiteSpace(model.Note) ? "Shop từ chối đơn hàng" : model.Note.Trim(),
            CreatedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync(cancellationToken);
        TempData["AdminToast"] = "Đã từ chối đơn hàng.";
        return RedirectToAction(nameof(Details), new { id = order.Id });
    }
}
