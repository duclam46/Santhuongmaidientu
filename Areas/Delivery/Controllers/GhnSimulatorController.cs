using System.Security.Claims;
using DOAN_BANHANG.Models;
using DOAN_BANHANG.Services;
using DOAN_BANHANG.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DOAN_BANHANG.Areas.Delivery.Controllers;

[Area("Delivery")]
[Route("Delivery/[controller]/[action]")]
public class GhnSimulatorController : Controller
{
    private readonly DataContext _db;
    private readonly GhnClient _ghn;

    public GhnSimulatorController(DataContext db, GhnClient ghn)
    {
        _db = db;
        _ghn = ghn;
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

    private static bool IsInRole(string? role, params string[] allowed)
    {
        if (string.IsNullOrWhiteSpace(role)) return false;
        return allowed.Any(r => string.Equals(r, role, StringComparison.OrdinalIgnoreCase));
    }

    private IQueryable<Order> ScopedOrders(int? accountId, string? role)
    {
        var q = _db.Orders.AsQueryable();

        if (!accountId.HasValue || accountId.Value <= 0)
        {
            return q.Where(_ => false);
        }

        if (IsInRole(role, "Admin", "Delivery"))
        {
            return q;
        }

        if (IsInRole(role, "Shop"))
        {
            return q.Where(o => o.ShopId == accountId.Value);
        }

        return q.Where(_ => false);
    }

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] DeliveryGhnSimulatorQueryVm query, CancellationToken cancellationToken)
    {
        var vm = new DeliveryGhnSimulatorVm
        {
            Query = new DeliveryGhnSimulatorQueryVm
            {
                TrackingCode = query.TrackingCode
            }
        };

        if (!TryGetSignedInAccount(out var accountId, out var role))
        {
            TempData["DeliveryToast"] = "Vui lòng đăng nhập để sử dụng GHN Simulator.";
            return View(vm);
        }

        if (!IsInRole(role, "Admin", "Shop", "Delivery"))
        {
            TempData["DeliveryToast"] = "Bạn không có quyền sử dụng GHN Simulator.";
            return View(vm);
        }

        Order? order = null;
        if (!string.IsNullOrWhiteSpace(vm.Query.TrackingCode))
        {
            var code = vm.Query.TrackingCode.Trim();
            var compact = new string(code.Where(c => !char.IsWhiteSpace(c)).ToArray());

            var scoped = ScopedOrders(accountId, role)
                .AsNoTracking()
                .OrderByDescending(o => o.CreatedAt);

            order = await scoped
                .FirstOrDefaultAsync(o => o.TrackingCode == code, cancellationToken);

            if (order is null && !string.IsNullOrWhiteSpace(compact) && !string.Equals(compact, code, StringComparison.Ordinal))
            {
                order = await scoped
                    .FirstOrDefaultAsync(o => o.TrackingCode == compact, cancellationToken);
            }

            if (order is null)
            {
                TempData["DeliveryToast"] = $"Không tìm thấy đơn hàng với mã vận đơn '{code}'.";
            }
        }

        if (order is not null)
        {
            vm.ResolvedOrderId = order.Id;
            vm.ResolvedOrderCode = order.OrderCode;
            vm.ResolvedTrackingCode = order.TrackingCode;

            // Planned route (simulation) built from GHN station list (docs id=96), not from GHN tracking logs.
            // We use: origin = shop default address (if any), destination = order shipping district/ward.
            try
            {
                UserAddress? origin = await _db.UserAddresses
                    .AsNoTracking()
                    .Where(x => x.AccountId == order.ShopId)
                    .OrderByDescending(x => x.IsDefault)
                    .ThenByDescending(x => x.UpdatedAt)
                    .FirstOrDefaultAsync(cancellationToken);

                var destinationDistrictId = order.ShippingDistrictId;
                var destinationWardCode = order.ShippingWardCode;

                if (destinationDistrictId is null || destinationDistrictId.Value <= 0)
                {
                    TempData["DeliveryToast"] = "Đơn hàng chưa có ShippingDistrictId nên không dựng được lộ trình dự kiến. (Cần lưu district/ward khi checkout)";
                }
                else
                {
                    IReadOnlyList<GhnStation> originStations = Array.Empty<GhnStation>();
                    if (origin is not null && origin.DistrictId > 0)
                    {
                        originStations = await _ghn.GetStationsAsync(new GhnStationGetRequest
                        {
                            DistrictId = origin.DistrictId,
                            WardCode = string.IsNullOrWhiteSpace(origin.WardCode) ? null : origin.WardCode.Trim(),
                            Offset = 0,
                            Limit = 50
                        }, cancellationToken);
                    }

                    var destStations = await _ghn.GetStationsAsync(new GhnStationGetRequest
                    {
                        DistrictId = destinationDistrictId.Value,
                        WardCode = string.IsNullOrWhiteSpace(destinationWardCode) ? null : destinationWardCode.Trim(),
                        Offset = 0,
                        Limit = 50
                    }, cancellationToken);

                    static string FormatStation(GhnStation s)
                    {
                        var name = string.IsNullOrWhiteSpace(s.LocationName) ? $"Bưu cục #{s.LocationId}" : s.LocationName.Trim();
                        var suffix = string.Join(", ", new[] { s.DistrictName, s.ProvinceName }
                            .Where(x => !string.IsNullOrWhiteSpace(x))
                            .Select(x => x!.Trim()));
                        return string.IsNullOrWhiteSpace(suffix) ? name : $"{name} ({suffix})";
                    }

                    // Pick a few representative stations to form a simple route.
                    var originPick1 = originStations.OrderBy(x => x.LocationName).FirstOrDefault();
                    var originPick2 = originStations.OrderBy(x => x.LocationName).Skip(1).FirstOrDefault();
                    var destPick = destStations.OrderBy(x => x.LocationName).FirstOrDefault();

                    var steps = new List<DeliveryGhnRouteStepVm>();

                    if (originPick1 is not null)
                    {
                        steps.Add(new DeliveryGhnRouteStepVm
                        {
                            Index = steps.Count + 1,
                            Location = FormatStation(originPick1),
                            StatusName = "Kho lấy hàng (dự kiến)",
                            Message = originPick1.Address,
                            TimeRaw = null
                        });
                    }

                    if (originPick2 is not null)
                    {
                        steps.Add(new DeliveryGhnRouteStepVm
                        {
                            Index = steps.Count + 1,
                            Location = FormatStation(originPick2),
                            StatusName = "Kho trung chuyển (dự kiến)",
                            Message = originPick2.Address,
                            TimeRaw = null
                        });
                    }

                    if (destPick is not null)
                    {
                        steps.Add(new DeliveryGhnRouteStepVm
                        {
                            Index = steps.Count + 1,
                            Location = FormatStation(destPick),
                            StatusName = "Kho phát / giao (dự kiến)",
                            Message = destPick.Address,
                            TimeRaw = null
                        });
                    }

                    // Fallback: if we couldn't pick any representative station, show a few destination stations as a clickable list.
                    if (steps.Count == 0)
                    {
                        steps = destStations
                            .OrderBy(x => x.LocationName)
                            .Take(5)
                            .Select((s, idx) => new DeliveryGhnRouteStepVm
                            {
                                Index = idx + 1,
                                Location = FormatStation(s),
                                StatusName = "Bưu cục/kho (dự kiến)",
                                Message = s.Address,
                                TimeRaw = null
                            })
                            .ToList();
                    }

                    vm.RouteSteps = steps;
                }
            }
            catch (Exception ex)
            {
                var msg = ex.Message ?? string.Empty;
                if (msg.Contains("SECURITY_CHECK_FAILED", StringComparison.OrdinalIgnoreCase)
                    || msg.Contains("invalid number of segments", StringComparison.OrdinalIgnoreCase))
                {
                    TempData["DeliveryToast"] =
                        "GHN từ chối Token (SECURITY_CHECK_FAILED). Hãy kiểm tra lại Ghn:Token trong appsettings.Development.json (đúng môi trường dev/prod, không dán kèm 'Bearer ', không có xuống dòng) và Ghn:BaseUrl tương ứng (dev-online-gateway vs online-gateway).";
                }
                else
                {
                    TempData["DeliveryToast"] = $"Không dựng được lộ trình dự kiến từ danh sách bưu cục GHN (id=96): {ex.Message}";
                }
            }
        }

        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CheckIn(DeliveryGhnSimulatorCheckInVm model, CancellationToken cancellationToken)
    {
        if (!TryGetSignedInAccount(out var accountId, out var role))
        {
            TempData["DeliveryToast"] = "Vui lòng đăng nhập để check-in.";
            return RedirectToAction(nameof(Index), new
            {
                TrackingCode = model.TrackingCode
            });
        }

        if (!IsInRole(role, "Admin", "Shop", "Delivery"))
        {
            TempData["DeliveryToast"] = "Bạn không có quyền check-in.";
            return RedirectToAction(nameof(Index), new
            {
                TrackingCode = model.TrackingCode
            });
        }

        if (!ModelState.IsValid)
        {
            TempData["DeliveryToast"] = "Dữ liệu check-in không hợp lệ.";
            return RedirectToAction(nameof(Index), new
            {
                TrackingCode = model.TrackingCode
            });
        }

        var order = await ScopedOrders(accountId, role)
            .FirstOrDefaultAsync(o => o.Id == model.OrderId, cancellationToken);

        if (order is null) return NotFound();

        var locationName = string.IsNullOrWhiteSpace(model.RouteLocation)
            ? "(không rõ kho)"
            : model.RouteLocation.Trim();

        var ev = (model.Event ?? string.Empty).Trim().ToLowerInvariant();
        var noteDefault = ev switch
        {
            "depart" or "leave" => $"Đã rời {locationName}",
            _ => $"Đã đến {locationName}",
        };

        var note = string.IsNullOrWhiteSpace(model.CustomNote) ? noteDefault : model.CustomNote.Trim();

        if (note.Length > 255)
        {
            note = note[..255];
        }

        _db.OrderTrackingLogs.Add(new OrderTrackingLog
        {
            OrderId = order.Id,
            Status = "Shipping",
            Note = note,
            CreatedAt = DateTime.UtcNow
        });

        order.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);
        TempData["DeliveryToast"] = "Đã ghi nhận check-in kho (giả lập).";

        return RedirectToAction(nameof(Index), new
        {
            TrackingCode = order.TrackingCode
        });
    }
}
