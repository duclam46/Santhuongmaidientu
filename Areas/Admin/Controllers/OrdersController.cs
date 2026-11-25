using System.Security.Claims;
using DOAN_BANHANG.Models;
using DOAN_BANHANG.Services;
using DOAN_BANHANG.ViewModels;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Authorization;

namespace DOAN_BANHANG.Areas.Admin.Controllers;

[Area("Admin")]
[Route("Admin/[controller]/[action]")]
[Microsoft.AspNetCore.Authorization.Authorize(Roles = "Shop,Admin")]
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
        var q = _db.Orders.Where(o => !(o.PaymentMethod == "BankTransfer" && o.PaymentStatus == "AwaitingPayment"));

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
    [HttpGet("/Admin/Orders")]
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
            IsDigitalOrder = order.IsDigitalOrder,
            RequiresVatInvoice = order.RequiresVatInvoice,
            CompanyName = order.CompanyName,
            TaxCode = order.TaxCode,
            CompanyAddress = order.CompanyAddress,
            Items = order.Items
                .OrderBy(i => i.Id)
                .Select(i => new AdminOrderItemVm
                {
                    Id = i.Id,
                    ProductName = i.ProductName,
                    ColorName = i.ColorName,
                    SizeName = i.SizeName,
                    VariantDescription = i.VariantDescription,
                    CommissionAmount = i.CommissionAmount,
                    UnitCostPrice = i.UnitCostPrice,
                    UnitPrice = i.UnitPrice,
                    Quantity = i.Quantity,
                    LineTotal = i.LineTotal
                })
                .ToList()
        };

        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> PrintVatInvoice(int id, CancellationToken cancellationToken)
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

        var shopAccount = await _db.Accounts
            .AsNoTracking()
            .Include(a => a.Profile)
            .FirstOrDefaultAsync(a => a.Id == order.ShopId, cancellationToken);

        ViewBag.SellerShopName = shopAccount?.Profile?.ShopName ?? shopAccount?.Profile?.FullName ?? "GIAN HÀNG SELLZY MARKETPLACE";
        ViewBag.SellerEmail = shopAccount?.Email ?? "support@sellzy.vn";

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
            CreatedAt = order.CreatedAt,
            CustomerEmail = order.Customer?.Email,
            IsDigitalOrder = order.IsDigitalOrder,
            RequiresVatInvoice = order.RequiresVatInvoice,
            CompanyName = order.CompanyName,
            TaxCode = order.TaxCode,
            CompanyAddress = order.CompanyAddress,
            Items = order.Items.OrderBy(i => i.Id).Select(i => new AdminOrderItemVm
            {
                Id = i.Id,
                ProductName = i.ProductName,
                ColorName = i.ColorName,
                SizeName = i.SizeName,
                VariantDescription = i.VariantDescription,
                UnitPrice = i.UnitPrice,
                Quantity = i.Quantity,
                LineTotal = i.LineTotal
            }).ToList()
        };

        return View("PrintVatInvoice", vm);
    }

    [HttpGet]
    public async Task<IActionResult> PrintPackingSlip(int id, CancellationToken cancellationToken)
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

        var shopAccount = await _db.Accounts
            .AsNoTracking()
            .Include(a => a.Profile)
            .FirstOrDefaultAsync(a => a.Id == order.ShopId, cancellationToken);

        var senderAddrs = await _db.UserAddresses
            .AsNoTracking()
            .Where(a => a.AccountId == order.ShopId)
            .ToListAsync(cancellationToken);
        var sender = PickBestSenderAddress(senderAddrs);

        ViewBag.SellerShopName = shopAccount?.Profile?.ShopName ?? shopAccount?.Profile?.FullName ?? "GIAN HÀNG SELLZY";
        ViewBag.SellerPhone = sender?.PhoneNumber ?? shopAccount?.Profile?.PhoneNumber ?? "1900 6868";
        ViewBag.SellerAddress = sender?.StreetAddress ?? "Kho trung chuyển Đối tác Sellzy Việt Nam";

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
            TrackingCode = string.IsNullOrWhiteSpace(order.TrackingCode) ? $"SZ-GHN-{order.Id:D6}" : order.TrackingCode,
            CreatedAt = order.CreatedAt,
            CustomerEmail = order.Customer?.Email,
            IsDigitalOrder = order.IsDigitalOrder,
            Items = order.Items.OrderBy(i => i.Id).Select(i => new AdminOrderItemVm
            {
                Id = i.Id,
                ProductName = i.ProductName,
                ColorName = i.ColorName,
                SizeName = i.SizeName,
                VariantDescription = i.VariantDescription,
                UnitPrice = i.UnitPrice,
                Quantity = i.Quantity,
                LineTotal = i.LineTotal
            }).ToList()
        };

        return View("PrintPackingSlip", vm);
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
            .ThenInclude(i => i.Variant)
            .ThenInclude(v => v!.Product)
            .FirstOrDefaultAsync(o => o.Id == model.Id, cancellationToken);

        if (order is null) return NotFound();

        if (!string.Equals(order.OrderStatus, "Pending", StringComparison.OrdinalIgnoreCase))
        {
            TempData["AdminToast"] = "Đơn hàng không còn ở trạng thái chờ xác nhận.";
            return RedirectToAction(nameof(Details), new { id = order.Id });
        }

        if (order.IsDigitalOrder)
        {
            order.OrderStatus = "Completed";
            order.UpdatedAt = DateTime.UtcNow;

            var digitalNotes = order.Items
                .Select(i => new { i.ProductName, Code = i.Variant?.Product?.DigitalContent })
                .Select(x => $"[{x.ProductName}]: {(!string.IsNullOrWhiteSpace(x.Code) ? x.Code : $"EVOUCHER-{order.OrderCode}")}")
                .ToList();
            var combinedCodes = string.Join(" | ", digitalNotes);

            _db.OrderTrackingLogs.Add(new OrderTrackingLog
            {
                OrderId = order.Id,
                Status = "Completed",
                Note = $"Shop xác nhận và hoàn tất đơn hàng sản phẩm số. Mã kích hoạt / E-Voucher: {combinedCodes}",
                CreatedAt = DateTime.UtcNow
            });

            _db.SystemNotifications.Add(new SystemNotification
            {
                Title = $"Mã E-Voucher / Sản phẩm số đơn #{order.OrderCode}",
                Content = $"Đơn hàng sản phẩm số #{order.OrderCode} đã hoàn tất. Nội dung bàn giao: {combinedCodes}",
                TargetType = "Account",
                TargetAccountId = order.CustomerId,
                SentByAdminId = order.ShopId,
                CreatedAt = DateTime.UtcNow
            });

            await _db.SaveChangesAsync(cancellationToken);
            TempData["AdminToast"] = "Đã hoàn tất đơn hàng Sản phẩm số / E-Voucher và gửi mã cho khách hàng!";
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

            var baseWeight = _ghnOptions.DefaultWeightGram <= 0 ? 300 : _ghnOptions.DefaultWeightGram;
            var totalWeight = order.Items.Sum(i =>
                (i.Variant != null && i.Variant.WeightGram > 0 ? i.Variant.WeightGram : baseWeight) * Math.Max(1, i.Quantity));
            var weight = Math.Min(50000, Math.Max(1, totalWeight));

            var defaultLen = _ghnOptions.DefaultLengthCm <= 0 ? 15 : _ghnOptions.DefaultLengthCm;
            var defaultWid = _ghnOptions.DefaultWidthCm <= 0 ? 15 : _ghnOptions.DefaultWidthCm;
            var defaultHei = _ghnOptions.DefaultHeightCm <= 0 ? 10 : _ghnOptions.DefaultHeightCm;

            var length = order.Items.Any(i => i.Variant != null && i.Variant.LengthCm > 0)
                ? Math.Min(150, order.Items.Max(i => i.Variant?.LengthCm ?? defaultLen))
                : defaultLen;
            var width = order.Items.Any(i => i.Variant != null && i.Variant.WidthCm > 0)
                ? Math.Min(150, order.Items.Max(i => i.Variant?.WidthCm ?? defaultWid))
                : defaultWid;
            var height = order.Items.Any(i => i.Variant != null && i.Variant.HeightCm > 0)
                ? Math.Min(150, order.Items.Sum(i => (i.Variant?.HeightCm ?? defaultHei) * Math.Max(1, i.Quantity)))
                : defaultHei;

            var serviceTypeId = weight >= 20000 ? 5 : (_ghnOptions.ServiceTypeId <= 0 ? 2 : _ghnOptions.ServiceTypeId);

            var codAmount = string.Equals(order.PaymentMethod, "COD", StringComparison.OrdinalIgnoreCase)
                ? (int)Math.Max(0, Math.Round(order.FinalAmount, MidpointRounding.AwayFromZero))
                : 0;

            bool hasFragileItem = order.Items.Any(i =>
                string.Equals(i.Variant?.Product?.ShippingCondition, "Hàng dễ vỡ", StringComparison.OrdinalIgnoreCase));
            bool hasColdFoodItem = order.Items.Any(i =>
                i.Variant?.Product?.ShippingCondition != null &&
                i.Variant.Product.ShippingCondition.Contains("Thực phẩm", StringComparison.OrdinalIgnoreCase));

            var conditionPrefix = hasFragileItem
                ? "[HÀNG DỄ VỠ - XIN NHẸ TAY] "
                : (hasColdFoodItem ? "[THỰC PHẨM - BẢO QUẢN MÁT] " : string.Empty);

            var rawNote = string.IsNullOrWhiteSpace(model.Note) ? string.Empty : model.Note.Trim();
            var finalGhnNote = string.IsNullOrWhiteSpace(conditionPrefix + rawNote) ? null : (conditionPrefix + rawNote).Trim();

            var req = new GhnCreateOrderRequest
            {
                ServiceTypeId = serviceTypeId,
                PaymentTypeId = 2,
                Note = finalGhnNote,
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
                Content = $"{conditionPrefix}Don {order.OrderCode}".Trim(),
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
                        Price = (int)Math.Max(0, Math.Round(i.UnitPrice, MidpointRounding.AwayFromZero)),
                        Weight = i.Variant != null && i.Variant.WeightGram > 0 ? i.Variant.WeightGram : baseWeight,
                        Length = i.Variant != null && i.Variant.LengthCm > 0 ? i.Variant.LengthCm : defaultLen,
                        Width = i.Variant != null && i.Variant.WidthCm > 0 ? i.Variant.WidthCm : defaultWid,
                        Height = i.Variant != null && i.Variant.HeightCm > 0 ? i.Variant.HeightCm : defaultHei
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

        _db.SystemNotifications.Add(new SystemNotification
        {
            Title = $"Đơn hàng #{order.OrderCode} đã được xác nhận",
            Content = string.IsNullOrWhiteSpace(order.TrackingCode)
                ? $"Gian hàng đã xác nhận đơn hàng #{order.OrderCode} của bạn và đang chuẩn bị bàn giao cho đơn vị vận chuyển."
                : $"Gian hàng đã xác nhận đơn hàng #{order.OrderCode} và khởi tạo vận đơn GHN: {order.TrackingCode}. Bạn có thể theo dõi hành trình đơn hàng ngay!",
            TargetType = "Account",
            TargetAccountId = order.CustomerId,
            SentByAdminId = accountId,
            CreatedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync(cancellationToken);
        TempData["AdminToast"] = string.IsNullOrWhiteSpace(order.TrackingCode)
            ? "Đã xác nhận đơn hàng."
            : $"Đã xác nhận đơn hàng. Mã vận đơn GHN: {order.TrackingCode}" + (string.IsNullOrWhiteSpace(pickupShiftTitle) ? string.Empty : $" | Ca lấy: {pickupShiftTitle}");
        return RedirectToAction(nameof(Details), new { id = order.Id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BulkConfirm([FromForm] List<int>? orderIds, CancellationToken cancellationToken)
    {
        if (!TryGetSignedInAccount(out var accountId, out var role)) return Challenge();

        if (orderIds == null || orderIds.Count == 0)
        {
            TempData["AdminToast"] = "Vui lòng tích chọn ít nhất 1 đơn hàng để xác nhận.";
            return RedirectToAction(nameof(Index));
        }

        var distinctIds = orderIds.Where(x => x > 0).Distinct().ToList();
        var orders = await ScopedOrders(accountId, role)
            .Include(o => o.Items)
                .ThenInclude(i => i.Variant)
                    .ThenInclude(v => v!.Product)
            .Where(o => distinctIds.Contains(o.Id) && o.OrderStatus == "Pending")
            .ToListAsync(cancellationToken);

        if (orders.Count == 0)
        {
            TempData["AdminToast"] = "Không có đơn hàng nào ở trạng thái 'Chờ xác nhận' trong danh sách bạn chọn.";
            return RedirectToAction(nameof(Index));
        }

        var shopIds = orders.Select(o => o.ShopId).Distinct().ToList();
        var shopAddrs = await _db.UserAddresses
            .AsNoTracking()
            .Where(a => shopIds.Contains(a.AccountId))
            .ToListAsync(cancellationToken);

        int confirmedCount = 0;
        int ghnCreatedCount = 0;

        foreach (var order in orders)
        {
            if (!order.IsDigitalOrder && string.IsNullOrWhiteSpace(order.TrackingCode))
            {
                var sender = PickBestSenderAddress(shopAddrs.Where(a => a.AccountId == order.ShopId));
                if (sender != null && sender.DistrictId > 0 && !string.IsNullOrWhiteSpace(sender.WardCode)
                    && order.ShippingDistrictId.HasValue && order.ShippingDistrictId.Value > 0
                    && !string.IsNullOrWhiteSpace(order.ShippingWardCode))
                {
                    try
                    {
                        var baseWeight = _ghnOptions.DefaultWeightGram <= 0 ? 300 : _ghnOptions.DefaultWeightGram;
                        var weight = Math.Clamp(order.Items.Sum(i => (i.Variant?.WeightGram > 0 ? i.Variant.WeightGram : baseWeight) * Math.Max(1, i.Quantity)), 50, 50000);
                        var codAmount = string.Equals(order.PaymentMethod, "COD", StringComparison.OrdinalIgnoreCase)
                            ? (int)Math.Max(0, Math.Round(order.FinalAmount, MidpointRounding.AwayFromZero))
                            : 0;

                        var req = new GhnCreateOrderRequest
                        {
                            ServiceTypeId = weight >= 20000 ? 5 : (_ghnOptions.ServiceTypeId <= 0 ? 2 : _ghnOptions.ServiceTypeId),
                            PaymentTypeId = 2,
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
                            Length = 15,
                            Width = 15,
                            Height = 10,
                            InsuranceValue = (int)Math.Max(0, Math.Round(order.SubTotal, MidpointRounding.AwayFromZero)),
                            Items = order.Items.Select(i => new GhnCreateOrderItem
                            {
                                Name = i.ProductName,
                                Quantity = Math.Max(1, i.Quantity),
                                Price = (int)Math.Max(0, Math.Round(i.UnitPrice, MidpointRounding.AwayFromZero)),
                                Weight = baseWeight,
                                Length = 15,
                                Width = 15,
                                Height = 10
                            }).ToList()
                        };

                        var ghnRes = await _ghn.CreateShippingOrderAsync(req, cancellationToken);
                        if (!string.IsNullOrWhiteSpace(ghnRes.OrderCode))
                        {
                            order.TrackingCode = ghnRes.OrderCode.Trim();
                            ghnCreatedCount++;
                        }
                    }
                    catch
                    {
                        // Best-effort GHN creation during bulk confirmation so one external timeout doesn't abort the batch
                    }
                }
            }

            order.OrderStatus = "Confirmed";
            order.UpdatedAt = DateTime.UtcNow;
            confirmedCount++;

            _db.OrderTrackingLogs.Add(new OrderTrackingLog
            {
                OrderId = order.Id,
                Status = "Confirmed",
                Note = string.IsNullOrWhiteSpace(order.TrackingCode)
                    ? "Shop xác nhận hàng loạt (Đang chuẩn bị hàng)."
                    : $"Shop xác nhận hàng loạt. GHN={order.TrackingCode}",
                CreatedAt = DateTime.UtcNow
            });

            _db.SystemNotifications.Add(new SystemNotification
            {
                Title = $"Đơn hàng #{order.OrderCode} đã được xác nhận",
                Content = string.IsNullOrWhiteSpace(order.TrackingCode)
                    ? $"Gian hàng đã xác nhận đơn hàng #{order.OrderCode} của bạn và đang đóng gói kiện hàng."
                    : $"Gian hàng đã xác nhận đơn hàng #{order.OrderCode} và khởi tạo mã vận đơn GHN: {order.TrackingCode}.",
                TargetType = "Account",
                TargetAccountId = order.CustomerId,
                SentByAdminId = accountId,
                CreatedAt = DateTime.UtcNow
            });
        }

        await _db.SaveChangesAsync(cancellationToken);
        TempData["AdminToast"] = ghnCreatedCount > 0
            ? $"Đã xác nhận thành công {confirmedCount} đơn hàng (Đã tạo {ghnCreatedCount} mã vận đơn GHN)!"
            : $"Đã xác nhận hàng loạt thành công {confirmedCount} đơn hàng!";

        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> BulkPrintPackingSlips([FromQuery] string? ids, CancellationToken cancellationToken)
    {
        if (!TryGetSignedInAccount(out var accountId, out var role)) return Challenge();

        var parsedIds = (ids ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.TryParse(s, out var v) ? v : 0)
            .Where(v => v > 0)
            .Distinct()
            .ToList();

        if (parsedIds.Count == 0)
        {
            TempData["AdminToast"] = "Vui lòng chọn ít nhất 1 đơn hàng để in phiếu đóng gói.";
            return RedirectToAction(nameof(Index));
        }

        var orders = await ScopedOrders(accountId, role)
            .AsNoTracking()
            .Include(o => o.Items)
            .Include(o => o.Shop)
                .ThenInclude(s => s!.Profile)
            .Where(o => parsedIds.Contains(o.Id))
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync(cancellationToken);

        if (orders.Count == 0) return NotFound();

        return View(orders);
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
            .ThenInclude(i => i.Variant)
            .ThenInclude(v => v!.Product)
            .FirstOrDefaultAsync(o => o.Id == model.Id, cancellationToken);

        if (order is null) return NotFound();

        if (!string.Equals(order.OrderStatus, "Pending", StringComparison.OrdinalIgnoreCase))
        {
            TempData["AdminToast"] = "Đơn hàng không còn ở trạng thái chờ xác nhận.";
            return RedirectToAction(nameof(Details), new { id = order.Id });
        }

        order.OrderStatus = "Rejected";
        order.UpdatedAt = DateTime.UtcNow;

        var rejectReason = string.IsNullOrWhiteSpace(model.Note) ? "Shop từ chối đơn hàng" : model.Note.Trim();
        _db.OrderTrackingLogs.Add(new OrderTrackingLog
        {
            OrderId = order.Id,
            Status = order.OrderStatus,
            Note = rejectReason,
            CreatedAt = DateTime.UtcNow
        });

        _db.SystemNotifications.Add(new SystemNotification
        {
            Title = $"Đơn hàng #{order.OrderCode} đã bị từ chối",
            Content = $"Rất tiếc, gian hàng không thể tiếp nhận đơn hàng #{order.OrderCode}. Lý do: {rejectReason}.",
            TargetType = "Account",
            TargetAccountId = order.CustomerId,
            SentByAdminId = accountId,
            CreatedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync(cancellationToken);
        TempData["AdminToast"] = "Đã từ chối đơn hàng.";
        return RedirectToAction(nameof(Details), new { id = order.Id });
    }
}
