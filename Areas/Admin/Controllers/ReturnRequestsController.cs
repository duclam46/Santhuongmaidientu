using System.Security.Claims;
using DOAN_BANHANG.Models;
using DOAN_BANHANG.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Authorization;

namespace DOAN_BANHANG.Areas.Admin.Controllers;

[Area("Admin")]
[Route("Admin/[controller]/[action]")]
[Authorize(Roles = "Shop,Admin")]
public class ReturnRequestsController : Controller
{
    private readonly DataContext _db;
    private readonly GhnClient _ghn;
    private readonly GhnOptions _ghnOptions;

    public ReturnRequestsController(DataContext db, GhnClient ghn, IOptions<GhnOptions> ghnOptions)
    {
        _db = db;
        _ghn = ghn;
        _ghnOptions = ghnOptions.Value;
    }

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

    private static UserAddress? PickBestSenderAddress(IEnumerable<UserAddress> addrs)
        => addrs
            .OrderByDescending(x => x.AddressType == AddressType.Shop)
            .ThenByDescending(x => x.IsDefault)
            .ThenByDescending(x => x.UpdatedAt)
            .FirstOrDefault();

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        if (!TryGetSignedInAccount(out var accountId, out var role))
            return Redirect("/auth/login");

        IQueryable<ReturnRequest> query = _db.ReturnRequests
            .AsNoTracking()
            .Include(r => r.Order)
            .Include(r => r.Customer)
            .ThenInclude(c => c!.Profile);

        // Shop only sees their own order returns
        if (string.Equals(role, "Shop", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(r => r.Order != null && r.Order.ShopId == accountId);
        }

        var items = await query
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(cancellationToken);

        return View(items);
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken)
    {
        if (id <= 0) return NotFound();

        if (!TryGetSignedInAccount(out var accountId, out var role))
            return Redirect("/auth/login");

        var rr = await _db.ReturnRequests
            .AsNoTracking()
            .Include(r => r.Order)
            .ThenInclude(o => o!.Items)
            .Include(r => r.Customer)
            .ThenInclude(c => c!.Profile)
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

        if (rr is null) return NotFound();

        // Shop scope check
        if (string.Equals(role, "Shop", StringComparison.OrdinalIgnoreCase)
            && rr.Order?.ShopId != accountId)
        {
            return Forbid();
        }

        return View(rr);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(int id, CancellationToken cancellationToken)
    {
        if (!TryGetSignedInAccount(out var accountId, out var role))
        {
            TempData["AdminToast"] = "Vui lòng đăng nhập.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var rr = await _db.ReturnRequests
            .Include(r => r.Order)
            .ThenInclude(o => o!.Items)
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

        if (rr is null) return NotFound();

        // Shop scope check
        if (string.Equals(role, "Shop", StringComparison.OrdinalIgnoreCase)
            && rr.Order?.ShopId != accountId)
        {
            TempData["AdminToast"] = "Bạn không có quyền duyệt yêu cầu này.";
            return RedirectToAction(nameof(Details), new { id });
        }

        if (rr.Status != "Pending")
        {
            TempData["AdminToast"] = "Yêu cầu này đã được xử lý rồi.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var order = rr.Order;
        if (order is null) return NotFound();

        // Create GHN RETURN shipping order
        // For return: from = customer address, to = shop address
        string? returnTrackingCode = null;
        string? estimatedPickupTime = null;

        if (!string.IsNullOrWhiteSpace(_ghnOptions.Token) && _ghnOptions.ShopId > 0)
        {
            try
            {
                // Shop address (destination for return)
                var shopAddresses = await _db.UserAddresses
                    .AsNoTracking()
                    .Where(a => a.AccountId == order.ShopId)
                    .ToListAsync(cancellationToken);
                var shopAddr = PickBestSenderAddress(shopAddresses);

                if (shopAddr is not null
                    && order.ShippingDistrictId.HasValue && order.ShippingDistrictId > 0
                    && !string.IsNullOrWhiteSpace(order.ShippingWardCode))
                {
                    var totalQty = order.Items.Sum(i => i.Quantity);
                    if (totalQty <= 0) totalQty = 1;

                    var baseWeight = _ghnOptions.DefaultWeightGram <= 0 ? 500 : _ghnOptions.DefaultWeightGram;
                    var weight = Math.Min(50000, Math.Max(1, baseWeight * totalQty));
                    var length = _ghnOptions.DefaultLengthCm <= 0 ? 20 : _ghnOptions.DefaultLengthCm;
                    var width = _ghnOptions.DefaultWidthCm <= 0 ? 15 : _ghnOptions.DefaultWidthCm;
                    var height = _ghnOptions.DefaultHeightCm <= 0 ? 10 : _ghnOptions.DefaultHeightCm;

                    // Return order: customer sends to shop
                    var returnReq = new GhnCreateOrderRequest
                    {
                        ServiceTypeId = _ghnOptions.ServiceTypeId <= 0 ? 2 : _ghnOptions.ServiceTypeId,
                        PaymentTypeId = 2, // shop pays
                        Note = $"Hoàn hàng đơn {order.OrderCode}",
                        RequiredNote = "KHONGCHOXEMHANG",
                        // FROM = customer
                        FromName = order.RecipientName,
                        FromPhone = order.RecipientPhone,
                        FromAddress = order.ShippingStreetAddress ?? order.ShippingAddress,
                        FromDistrictId = order.ShippingDistrictId.Value,
                        FromWardCode = order.ShippingWardCode!,
                        // TO = shop
                        ToName = shopAddr.FullName,
                        ToPhone = shopAddr.PhoneNumber,
                        ToAddress = shopAddr.StreetAddress,
                        ToDistrictId = shopAddr.DistrictId,
                        ToWardCode = shopAddr.WardCode,
                        CodAmount = 0,
                        Content = $"Hoàn hàng {order.OrderCode}",
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

                    var ghnResult = await _ghn.CreateShippingOrderAsync(returnReq, cancellationToken);
                    if (!string.IsNullOrWhiteSpace(ghnResult.OrderCode))
                    {
                        returnTrackingCode = ghnResult.OrderCode.Trim();
                    }

                    // Get pickup time estimate
                    try
                    {
                        var shifts = await _ghn.GetPickupShiftsAsync(cancellationToken);
                        var firstShift = shifts.OrderBy(s => s.FromTime).FirstOrDefault();
                        if (firstShift is not null)
                        {
                            var fromDt = DateTimeOffset.FromUnixTimeSeconds(firstShift.FromTime).ToLocalTime();
                            var toDt = DateTimeOffset.FromUnixTimeSeconds(firstShift.ToTime).ToLocalTime();
                            estimatedPickupTime = $"{firstShift.Title}: {fromDt:HH:mm} - {toDt:HH:mm}, {fromDt:dd/MM/yyyy}";
                        }
                    }
                    catch
                    {
                        // Best-effort
                    }
                }
            }
            catch (Exception ex)
            {
                TempData["AdminToast"] = $"Lỗi tạo vận đơn GHN hoàn hàng: {ex.Message}. Đã duyệt nhưng chưa có mã hoàn.";
            }
        }

        rr.Status = "Approved";
        rr.ReturnTrackingCode = returnTrackingCode;
        rr.EstimatedPickupTime = estimatedPickupTime;
        rr.UpdatedAt = DateTime.UtcNow;

        // Update order status
        order.OrderStatus = "Returning";
        order.UpdatedAt = DateTime.UtcNow;

        _db.OrderTrackingLogs.Add(new OrderTrackingLog
        {
            OrderId = order.Id,
            Status = "Returning",
            Note = string.IsNullOrWhiteSpace(returnTrackingCode)
                ? "Shop đã duyệt yêu cầu trả hàng. GHN sẽ đến lấy hàng."
                : $"Shop đã duyệt yêu cầu trả hàng. Mã đơn hoàn GHN: {returnTrackingCode}" +
                  (string.IsNullOrWhiteSpace(estimatedPickupTime) ? "" : $". Thời gian lấy: {estimatedPickupTime}"),
            CreatedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync(cancellationToken);

        TempData["AdminToast"] = string.IsNullOrWhiteSpace(returnTrackingCode)
            ? "Đã duyệt yêu cầu trả hàng."
            : $"Đã duyệt. Mã đơn hoàn GHN: {returnTrackingCode}" +
              (string.IsNullOrWhiteSpace(estimatedPickupTime) ? "" : $" | Ca lấy: {estimatedPickupTime}");

        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reject(int id, [FromForm] string? note, CancellationToken cancellationToken)
    {
        if (!TryGetSignedInAccount(out var accountId, out var role))
        {
            TempData["AdminToast"] = "Vui lòng đăng nhập.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var rr = await _db.ReturnRequests
            .Include(r => r.Order)
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

        if (rr is null) return NotFound();

        // Shop scope check
        if (string.Equals(role, "Shop", StringComparison.OrdinalIgnoreCase)
            && rr.Order?.ShopId != accountId)
        {
            TempData["AdminToast"] = "Bạn không có quyền từ chối yêu cầu này.";
            return RedirectToAction(nameof(Details), new { id });
        }

        if (rr.Status != "Pending")
        {
            TempData["AdminToast"] = "Yêu cầu này đã được xử lý rồi.";
            return RedirectToAction(nameof(Details), new { id });
        }

        rr.Status = "Rejected";
        rr.RejectionNote = string.IsNullOrWhiteSpace(note) ? "Shop từ chối yêu cầu trả hàng." : note.Trim();
        rr.UpdatedAt = DateTime.UtcNow;

        // Revert order status back to delivered
        var order = rr.Order;
        if (order is not null)
        {
            order.OrderStatus = "Delivered";
            order.UpdatedAt = DateTime.UtcNow;

            _db.OrderTrackingLogs.Add(new OrderTrackingLog
            {
                OrderId = order.Id,
                Status = "Delivered",
                Note = $"Shop từ chối yêu cầu trả hàng. Lý do: {rr.RejectionNote}",
                CreatedAt = DateTime.UtcNow
            });
        }

        await _db.SaveChangesAsync(cancellationToken);

        TempData["AdminToast"] = "Đã từ chối yêu cầu trả hàng.";
        return RedirectToAction(nameof(Details), new { id });
    }
}
