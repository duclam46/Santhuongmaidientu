using System.Security.Claims;
using DOAN_BANHANG.Helpers;
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
    private const string SimNotePrefix = "SIM|";

    private sealed record SimShipper(string Name, string Phone);

    private static readonly IReadOnlyDictionary<string, SimShipper[]> SimShippersByRegion =
        new Dictionary<string, SimShipper[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["North"] =
            [
                new SimShipper("Nguyễn Văn Long", "0901 000 111"),
                new SimShipper("Trần Minh Quân", "0901 000 222"),
                new SimShipper("Phạm Đức Anh", "0901 000 333"),
                new SimShipper("Lê Hoàng Nam", "0901 000 444"),
            ],
            ["Central"] =
            [
                new SimShipper("Võ Thành Đạt", "0902 000 111"),
                new SimShipper("Đặng Quốc Huy", "0902 000 222"),
                new SimShipper("Ngô Hải Đăng", "0902 000 333"),
                new SimShipper("Bùi Gia Bảo", "0902 000 444"),
            ],
            ["South"] =
            [
                new SimShipper("Nguyễn Thảo Vy", "0903 000 111"),
                new SimShipper("Lý Minh Trí", "0903 000 222"),
                new SimShipper("Huỳnh Tuấn Kiệt", "0903 000 333"),
                new SimShipper("Trần Nhật Hào", "0903 000 444"),
            ],
            ["Unknown"] =
            [
                new SimShipper("Shipper", "0900 000 000"),
            ],
        };

    private readonly DataContext _db;
    private readonly GhnClient _ghn;
    private readonly GhnStationCatalog _stationCatalog;
    private readonly WalletService _walletService;

    public GhnSimulatorController(DataContext db, GhnClient ghn, GhnStationCatalog stationCatalog, WalletService walletService)
    {
        _db = db;
        _ghn = ghn;
        _stationCatalog = stationCatalog;
        _walletService = walletService;
    }

    private static bool IsAjaxRequest(HttpRequest request)
    {
        if (request is null) return false;
        return string.Equals(request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase)
            || string.Equals(request.Headers["X-Partial"], "true", StringComparison.OrdinalIgnoreCase);
    }

    private static void SetToastHeader(HttpResponse response, string? toast)
    {
        if (response is null) return;
        if (string.IsNullOrWhiteSpace(toast)) return;

        // Encode for safe transport in headers (supports UTF-8 via percent-encoding).
        response.Headers["X-Delivery-Toast"] = Uri.EscapeDataString(toast);
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

    private static int StableHash(string key)
    {
        if (string.IsNullOrEmpty(key)) return 0;
        unchecked
        {
            int hash = 17;
            foreach (var ch in key)
            {
                hash = (hash * 31) + ch;
            }
            return hash;
        }
    }

    private static bool TryExtractSimShipper(string? note, out string? shipperName, out string? shipperPhone)
    {
        shipperName = null;
        shipperPhone = null;

        if (string.IsNullOrWhiteSpace(note)) return false;

        var separators = new[] { " - ", " – ", " — " };
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

        // Head: SIM|i=...|e=...|shipper=...|phone=...
        var parts = head.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            var eq = part.IndexOf('=');
            if (eq <= 0 || eq >= part.Length - 1) continue;

            var key = part[..eq].Trim();
            var val = Uri.UnescapeDataString(part[(eq + 1)..].Trim());
            if (string.IsNullOrWhiteSpace(val)) continue;

            if (key.Equals("shipper", StringComparison.OrdinalIgnoreCase)) shipperName = val;
            else if (key.Equals("phone", StringComparison.OrdinalIgnoreCase)) shipperPhone = val;
        }

        return !string.IsNullOrWhiteSpace(shipperName) || !string.IsNullOrWhiteSpace(shipperPhone);
    }

    private async Task<SimShipper> ResolveOrAssignSimShipperAsync(Order order, CancellationToken cancellationToken)
    {
        // 1) Reuse existing assigned shipper from any prior SIM log.
        var existingNote = await _db.OrderTrackingLogs
            .AsNoTracking()
            .Where(l => l.OrderId == order.Id && l.Note != null && EF.Functions.Like(l.Note, "SIM|%|shipper=%"))
            .OrderByDescending(l => l.CreatedAt)
            .Select(l => l.Note)
            .FirstOrDefaultAsync(cancellationToken);

        if (TryExtractSimShipper(existingNote, out var existingName, out var existingPhone)
            && (!string.IsNullOrWhiteSpace(existingName) || !string.IsNullOrWhiteSpace(existingPhone)))
        {
            return new SimShipper(existingName ?? "Shipper", existingPhone ?? "0900 000 000");
        }

        // 2) Assign deterministically by destination + region (route-based feel).
        var region = "Unknown";
        if (order.ShippingProvinceId.HasValue && order.ShippingProvinceId.Value > 0)
        {
            var dbRegion = await _db.Warehouses
                .AsNoTracking()
                .Where(w => w.NodeType == "ProvinceHub" && w.GhnProvinceId == order.ShippingProvinceId.Value)
                .Select(w => w.Region)
                .FirstOrDefaultAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(dbRegion)) region = dbRegion!;
        }

        if (!SimShippersByRegion.TryGetValue(region, out var list) || list.Length == 0)
        {
            list = SimShippersByRegion["Central"];
        }

        var seed = (order.ShippingDistrictId?.ToString() ?? order.ShippingProvinceId?.ToString() ?? order.Id.ToString()) + "|" + region;
        var idx = Math.Abs(StableHash(seed)) % list.Length;
        return list[idx];
    }

    private static List<DeliveryGhnRouteStepVm> BuildOfflinePlannedRoute(UserAddress? origin, Order order, string? note = null)
    {
        static string JoinAddress(params string?[] parts)
            => string.Join(", ", parts.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!.Trim()));

        var originLabel = origin is null
            ? "Kho lấy hàng (Shop)"
            : $"Kho lấy hàng (Shop - {JoinAddress(origin.DistrictName, origin.ProvinceName)})";

        var originAddress = origin is null
            ? null
            : JoinAddress(origin.StreetAddress, origin.WardName, origin.DistrictName, origin.ProvinceName);

        var destinationLabel = "Kho phát / giao (Khách)";
        var destinationAddress = string.IsNullOrWhiteSpace(order.ShippingAddress) ? null : order.ShippingAddress.Trim();

        return new List<DeliveryGhnRouteStepVm>
        {
            new()
            {
                Index = 1,
                Location = originLabel,
                StatusName = "Dự kiến",
                Message = originAddress,
                TimeRaw = null
            },
            new()
            {
                Index = 2,
                Location = "Kho trung chuyển (giả lập)",
                StatusName = "Dự kiến",
                Message = string.IsNullOrWhiteSpace(note) ? "(Offline) Không đồng bộ được danh sách kho; đang dùng tuyến giả lập." : note,
                TimeRaw = null
            },
            new()
            {
                Index = 3,
                Location = destinationLabel,
                StatusName = "Dự kiến",
                Message = destinationAddress,
                TimeRaw = null
            }
        };
    }

    private static List<DeliveryGhnRouteStepVm> BuildOfflinePlannedRouteForReturn(UserAddress? shopAddress, Order order, string? note = null)
    {
        static string JoinAddress(params string?[] parts)
            => string.Join(", ", parts.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!.Trim()));

        var originLabel = "Nơi lấy hàng hoàn (Khách hàng)";
        var originAddress = string.IsNullOrWhiteSpace(order.ShippingAddress) ? null : order.ShippingAddress.Trim();

        var destinationLabel = shopAddress is null
            ? "Nơi trả hàng hoàn (Shop)"
            : $"Nơi trả hàng hoàn (Shop - {JoinAddress(shopAddress.DistrictName, shopAddress.ProvinceName)})";

        var destinationAddress = shopAddress is null
            ? null
            : JoinAddress(shopAddress.StreetAddress, shopAddress.WardName, shopAddress.DistrictName, shopAddress.ProvinceName);

        return new List<DeliveryGhnRouteStepVm>
        {
            new()
            {
                Index = 1,
                Location = originLabel,
                StatusName = "Dự kiến",
                Message = originAddress,
                TimeRaw = null
            },
            new()
            {
                Index = 2,
                Location = "Kho trung chuyển hoàn (giả lập)",
                StatusName = "Dự kiến",
                Message = string.IsNullOrWhiteSpace(note) ? "(Offline) Đơn hàng hoàn đang trên đường vận chuyển về kho Shop." : note,
                TimeRaw = null
            },
            new()
            {
                Index = 3,
                Location = destinationLabel,
                StatusName = "Dự kiến",
                Message = destinationAddress,
                TimeRaw = null
            }
        };
    }

    private enum MacroRegion
    {
        Unknown = 0,
        North = 1,
        Central = 2,
        South = 3,
    }

    private static MacroRegion GetMacroRegion(string? provinceName)
    {
        if (string.IsNullOrWhiteSpace(provinceName)) return MacroRegion.Unknown;
        var p = provinceName.Trim().ToLowerInvariant();

        // Best-effort mapping by province/city name.
        // This is NOT official GHN data; it only drives the simulator route.
        var north = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "hà nội","ha noi","hải phòng","hai phong","quảng ninh","quang ninh","bắc ninh","bac ninh","bắc giang","bac giang",
            "hưng yên","hung yen","hải dương","hai duong","vĩnh phúc","vinh phuc","phú thọ","phu tho","thái nguyên","thai nguyen",
            "bắc kạn","bac kan","lạng sơn","lang son","cao bằng","cao bang","hà giang","ha giang","tuyên quang","tuyen quang",
            "yên bái","yen bai","lào cai","lao cai","sơn la","son la","điện biên","dien bien","lai châu","lai chau",
            "hòa bình","hoa binh","ninh bình","ninh binh","nam định","nam dinh","thái bình","thai binh","hà nam","ha nam",
            "thanh hóa","thanh hoa","nghệ an","nghe an","hà tĩnh","ha tinh"
        };

        var central = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "quảng bình","quang binh","quảng trị","quang tri","thừa thiên huế","thua thien hue","huế","hue",
            "đà nẵng","da nang","quảng nam","quang nam","quảng ngãi","quang ngai","bình định","binh dinh",
            "phú yên","phu yen","khánh hòa","khanh hoa","ninh thuận","ninh thuan","bình thuận","binh thuan",
            "kon tum","gia lai","đắk lắk","dak lak","đắk nông","dak nong","lâm đồng","lam dong"
        };

        var south = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "tp. hồ chí minh","tp hcm","tphcm","hồ chí minh","ho chi minh","sài gòn","sai gon",
            "bình dương","binh duong","đồng nai","dong nai","bà rịa - vũng tàu","ba ria - vung tau","bà rịa vũng tàu","ba ria vung tau",
            "tây ninh","tay ninh","bình phước","binh phuoc","long an","tiền giang","tien giang","bến tre","ben tre","trà vinh","tra vinh",
            "vĩnh long","vinh long","đồng tháp","dong thap","an giang","kiên giang","kien giang","cần thơ","can tho","hậu giang","hau giang",
            "sóc trăng","soc trang","bạc liêu","bac lieu","cà mau","ca mau"
        };

        if (north.Contains(p)) return MacroRegion.North;
        if (central.Contains(p)) return MacroRegion.Central;
        if (south.Contains(p)) return MacroRegion.South;

        // Some inputs may contain suffix/prefix like "Tỉnh ..." or "Thành phố ...".
        p = p.Replace("tỉnh", "").Replace("thành phố", "").Replace("thanh pho", "").Trim();
        if (north.Contains(p)) return MacroRegion.North;
        if (central.Contains(p)) return MacroRegion.Central;
        if (south.Contains(p)) return MacroRegion.South;

        return MacroRegion.Unknown;
    }

    private static (string SocLabel, string RegionalHubLabel) GetRegionHubLabels(MacroRegion region)
    {
        return region switch
        {
            MacroRegion.North => (
                SocLabel: "SOC Miền Bắc (giả lập - Hà Nội)",
                RegionalHubLabel: "Trung tâm phân loại Miền Bắc (giả lập - Hà Nội)"),
            MacroRegion.Central => (
                SocLabel: "SOC Miền Trung (giả lập - Đà Nẵng)",
                RegionalHubLabel: "Trung tâm phân loại Miền Trung (giả lập - Đà Nẵng)"),
            MacroRegion.South => (
                SocLabel: "SOC Miền Nam (giả lập - TP.HCM)",
                RegionalHubLabel: "Trung tâm phân loại Miền Nam (giả lập - TP.HCM)"),
            _ => (
                SocLabel: "SOC Tổng (giả lập)",
                RegionalHubLabel: "Trung tâm phân loại (giả lập)")
        };
    }

    private static int MapGhnStatusToStage(string? status)
    {
        if (string.IsNullOrWhiteSpace(status)) return 0;
        var s = status.Trim().ToLowerInvariant();

        // NOTE: GHN statuses differ by environment/version.
        // This mapping is intentionally coarse; unknown statuses fall back to stage 0.
        if (s.Contains("cancel") || s.Contains("canceled") || s.Contains("return")) return -1;
        if (s.Contains("delivered") || s == "delivered") return 6;
        if (s.Contains("delivering") || s.Contains("out_for_delivery")) return 5;
        if (s.Contains("transport") || s.Contains("in_transit") || s.Contains("transit")) return 3;
        if (s.Contains("sorting") || s.Contains("sort")) return 4;
        if (s.Contains("stored") || s.Contains("storing") || s.Contains("warehouse")) return 2;
        if (s.Contains("picked") || s.Contains("picked_up") || s.Contains("pick")) return 1;
        if (s.Contains("ready_to_pick") || s.Contains("ready")) return 0;
        if (s.Contains("new") || s.Contains("create") || s.Contains("created")) return 0;

        return 0;
    }

    private static int StageToCurrentIndex(int stage, int stepCount)
    {
        if (stepCount <= 1) return 1;
        if (stage < 0) return 1;

        var clamped = Math.Clamp(stage, 0, 6);
        var ratio = clamped / 6.0;
        var idx = 1 + (int)Math.Floor(ratio * (stepCount - 1));
        return Math.Clamp(idx, 1, stepCount);
    }

    private static string FormatVnTime(DateTime utc)
    {
        var local = VietnamTimeHelper.UtcToVietNamLocal(utc) ?? utc;
        return local.ToString("yyyy-MM-dd HH:mm");
    }

    private async Task<List<DeliveryGhnRouteStepVm>> BuildSimulatedWarehouseNetworkRouteAsync(
        UserAddress? origin,
        Order order,
        string? ghnStatus,
        string? ghnStatusName,
        bool isReturn,
        CancellationToken cancellationToken)
    {
        static string JoinAddress(params string?[] parts)
            => string.Join(", ", parts.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!.Trim()));

        // Prefer DB-backed warehouse network (dense nodes) if available.
        if (await _db.Warehouses.AsNoTracking().AnyAsync(cancellationToken))
        {
            var originProvinceId = isReturn ? order.ShippingProvinceId : origin?.ProvinceId;
            var originDistrictId = isReturn ? order.ShippingDistrictId : origin?.DistrictId;
            var destProvinceId = isReturn ? origin?.ProvinceId : order.ShippingProvinceId;
            var destDistrictId = isReturn ? origin?.DistrictId : order.ShippingDistrictId;

            if (originProvinceId is int op && op > 0 && destProvinceId is int dp && dp > 0)
            {
                var originProvinceWh = await _db.Warehouses
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.NodeType == "ProvinceHub" && x.GhnProvinceId == op, cancellationToken);

                var destProvinceWh = await _db.Warehouses
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.NodeType == "ProvinceHub" && x.GhnProvinceId == dp, cancellationToken);

                Warehouse? originDistrictWh = null;
                if (originDistrictId is int od && od > 0)
                {
                    originDistrictWh = await _db.Warehouses
                        .AsNoTracking()
                        .FirstOrDefaultAsync(x => x.NodeType == "DistrictHub" && x.GhnDistrictId == od, cancellationToken);
                }

                Warehouse? destDistrictWh = null;
                if (destDistrictId is int dd && dd > 0)
                {
                    destDistrictWh = await _db.Warehouses
                        .AsNoTracking()
                        .FirstOrDefaultAsync(x => x.NodeType == "DistrictHub" && x.GhnDistrictId == dd, cancellationToken);
                }

                // If province hubs are missing, fall back to name-based simulation below.
                if (originProvinceWh is not null && destProvinceWh is not null)
                {
                    var dbOriginRegion = string.IsNullOrWhiteSpace(originProvinceWh.Region) ? null : originProvinceWh.Region.Trim();
                    var dbDestRegion = string.IsNullOrWhiteSpace(destProvinceWh.Region) ? null : destProvinceWh.Region.Trim();

                        Warehouse? originSocWh = null;
                        if (!string.IsNullOrWhiteSpace(dbOriginRegion))
                    {
                        originSocWh = await _db.Warehouses
                            .AsNoTracking()
                            .FirstOrDefaultAsync(x => x.NodeType == "Soc" && x.Region == dbOriginRegion, cancellationToken);
                    }

                        Warehouse? destSocWh = null;
                        if (!string.IsNullOrWhiteSpace(dbDestRegion))
                    {
                        destSocWh = await _db.Warehouses
                            .AsNoTracking()
                            .FirstOrDefaultAsync(x => x.NodeType == "Soc" && x.Region == dbDestRegion, cancellationToken);
                    }

                    var dbSameProvince = op == dp;
                    var dbSameRegion = !string.IsNullOrWhiteSpace(dbOriginRegion)
                                     && !string.IsNullOrWhiteSpace(dbDestRegion)
                                     && string.Equals(dbOriginRegion, dbDestRegion, StringComparison.OrdinalIgnoreCase);

                    var dbOriginLabel = isReturn
                        ? $"Nhận hàng hoàn (Khách - {destDistrictWh?.Name ?? destProvinceWh?.Name ?? "Khách"})"
                        : (origin is null
                            ? "Kho lấy hàng (Shop)"
                            : $"Kho lấy hàng (Shop - {JoinAddress(origin.DistrictName, origin.ProvinceName)})");

                    var dbOriginAddr = isReturn
                        ? (string.IsNullOrWhiteSpace(order.ShippingAddress) ? null : order.ShippingAddress.Trim())
                        : (origin is null
                            ? null
                            : JoinAddress(origin.StreetAddress, origin.WardName, origin.DistrictName, origin.ProvinceName));

                    var dbDestAddr = isReturn
                        ? (origin is null
                            ? null
                            : JoinAddress(origin.StreetAddress, origin.WardName, origin.DistrictName, origin.ProvinceName))
                        : (string.IsNullOrWhiteSpace(order.ShippingAddress) ? null : order.ShippingAddress.Trim());

                    var dbDestSuffix = isReturn
                        ? (origin is null ? "" : origin.DistrictName ?? origin.ProvinceName ?? "")
                        : (destDistrictWh is null
                            ? (destProvinceWh is null ? "" : destProvinceWh.Name)
                            : destDistrictWh.Name);

                    var dbDestLabel = isReturn
                        ? $"Trả hàng hoàn (Shop - {dbDestSuffix})"
                        : (string.IsNullOrWhiteSpace(dbDestSuffix)
                            ? "Giao hàng (Khách)"
                            : $"Giao hàng (Khách - {dbDestSuffix})");

                    var dbNodes = new List<Warehouse>();
                    if (originDistrictWh is not null) dbNodes.Add(originDistrictWh);
                    dbNodes.Add(originProvinceWh);

                    if (!dbSameProvince)
                    {
                        if (originSocWh is not null) dbNodes.Add(originSocWh);
                        if (!dbSameRegion && destSocWh is not null && (originSocWh?.Id != destSocWh.Id)) dbNodes.Add(destSocWh);
                    }

                    dbNodes.Add(destProvinceWh);
                    if (destDistrictWh is not null) dbNodes.Add(destDistrictWh);

                    var dbSteps = new List<DeliveryGhnRouteStepVm>
                    {
                        new()
                        {
                            Index = 1,
                            Location = dbOriginLabel,
                            StatusName = "",
                            Message = dbOriginAddr,
                            TimeRaw = null
                        }
                    };

                    foreach (var wh in dbNodes)
                    {
                        dbSteps.Add(new DeliveryGhnRouteStepVm
                        {
                            Index = dbSteps.Count + 1,
                            Location = wh.Name,
                            StatusName = "",
                            Message = wh.NodeType switch
                            {
                                "DistrictHub" => "Tập kết/khai thác cấp quận/huyện (mô phỏng)",
                                "ProvinceHub" => "Trung chuyển/khai thác cấp tỉnh (mô phỏng)",
                                "Soc" => "Trung tâm phân loại tổng (SOC - mô phỏng)",
                                _ => "Trung chuyển (mô phỏng)"
                            },
                            TimeRaw = null
                        });
                    }

                    dbSteps.Add(new DeliveryGhnRouteStepVm
                    {
                        Index = dbSteps.Count + 1,
                        Location = dbDestLabel,
                        StatusName = "",
                        Message = dbDestAddr,
                        TimeRaw = null
                    });

                    // Pull default hours from DB routes (if present) to build a more realistic ETA.
                    var involvedIds = dbNodes.Select(x => x.Id).ToHashSet();
                    var routeLookup = await _db.WarehouseRoutes
                        .AsNoTracking()
                        .Where(r => involvedIds.Contains(r.FromWarehouseId) && involvedIds.Contains(r.ToWarehouseId))
                        .Select(r => new { r.FromWarehouseId, r.ToWarehouseId, r.DefaultHours })
                        .ToListAsync(cancellationToken);

                    var hoursByLeg = routeLookup
                        .GroupBy(x => (x.FromWarehouseId, x.ToWarehouseId))
                        .ToDictionary(g => g.Key, g => g.First().DefaultHours);

                    var legHours = new List<int>();
                    for (var i = 0; i < dbNodes.Count - 1; i++)
                    {
                        var from = dbNodes[i];
                        var to = dbNodes[i + 1];
                        if (!hoursByLeg.TryGetValue((from.Id, to.Id), out var h))
                        {
                            h = dbSameProvince ? 2 : (dbSameRegion ? 6 : 10);
                        }

                        legHours.Add(Math.Max(1, h));
                    }

                    // Apply progress/status text based on GHN status.
                    var dbStage = MapGhnStatusToStage(ghnStatus);
                    var dbCurrentIndex = StageToCurrentIndex(dbStage, dbSteps.Count);

                    var dbBaseUtc = order.CreatedAt.Kind == DateTimeKind.Utc
                        ? order.CreatedAt
                        : DateTime.SpecifyKind(order.CreatedAt, DateTimeKind.Utc);

                    // Step 1 is shop; step 2 corresponds to nodes[0] if any.
                    var cumulativeHours = 0;
                    for (var i = 0; i < dbSteps.Count; i++)
                    {
                        var idx = i + 1;
                        if (dbStage < 0)
                        {
                            dbSteps[i].StatusName = idx == 1
                                ? (string.IsNullOrWhiteSpace(ghnStatusName) ? "Đơn đã hủy/hoàn" : ghnStatusName)
                                : "Không thực hiện";
                            dbSteps[i].TimeRaw = idx == 1 ? FormatVnTime(DateTime.UtcNow) : null;
                            continue;
                        }

                        dbSteps[i].StatusName = idx < dbCurrentIndex ? "Đã qua" : (idx == dbCurrentIndex ? "Đang xử lý" : "Dự kiến");

                        if (i == 0)
                        {
                            dbSteps[i].TimeRaw = FormatVnTime(dbBaseUtc);
                            continue;
                        }

                        // Map step i -> leg hours from previous node transitions.
                        // steps: [Shop] + nodes + [Deliver]
                        var nodeIndex = i - 1; // 0..dbNodes.Count
                        if (nodeIndex - 1 >= 0 && nodeIndex - 1 < legHours.Count)
                        {
                            cumulativeHours += legHours[nodeIndex - 1];
                        }
                        else if (nodeIndex == dbNodes.Count)
                        {
                            // last step (deliver)
                            cumulativeHours += dbSameProvince ? 2 : 4;
                        }
                        else
                        {
                            cumulativeHours += 2;
                        }

                        dbSteps[i].TimeRaw = FormatVnTime(dbBaseUtc.AddHours(cumulativeHours));
                    }

                    if (!string.IsNullOrWhiteSpace(ghnStatusName))
                    {
                        var cur = dbSteps[Math.Clamp(dbCurrentIndex - 1, 0, dbSteps.Count - 1)];
                        cur.Message = string.IsNullOrWhiteSpace(cur.Message)
                            ? $"Trạng thái: {ghnStatusName}"
                            : $"{cur.Message} | Trạng thái: {ghnStatusName}";
                    }

                    return dbSteps;
                }
            }
        }

        string? destProvinceName = null;
        string? destDistrictName = null;

        try
        {
            if (order.ShippingProvinceId is int provinceId && provinceId > 0)
            {
                var provinces = await _ghn.GetProvincesAsync(cancellationToken);
                destProvinceName = provinces.FirstOrDefault(x => x.ProvinceId == provinceId)?.ProvinceName;

                if (order.ShippingDistrictId is int districtId && districtId > 0)
                {
                    var districts = await _ghn.GetDistrictsAsync(provinceId, cancellationToken);
                    destDistrictName = districts.FirstOrDefault(x => x.DistrictId == districtId)?.DistrictName;
                }
            }
        }
        catch
        {
            // Best-effort only.
        }

        var originProvince = isReturn ? (string.IsNullOrWhiteSpace(destProvinceName) ? null : destProvinceName!.Trim()) : (string.IsNullOrWhiteSpace(origin?.ProvinceName) ? null : origin!.ProvinceName!.Trim());
        var originDistrict = isReturn ? (string.IsNullOrWhiteSpace(destDistrictName) ? null : destDistrictName!.Trim()) : (string.IsNullOrWhiteSpace(origin?.DistrictName) ? null : origin!.DistrictName!.Trim());
        var destProvince = isReturn ? (string.IsNullOrWhiteSpace(origin?.ProvinceName) ? null : origin!.ProvinceName!.Trim()) : (string.IsNullOrWhiteSpace(destProvinceName) ? null : destProvinceName!.Trim());
        var destDistrict = isReturn ? (string.IsNullOrWhiteSpace(origin?.DistrictName) ? null : origin!.DistrictName!.Trim()) : (string.IsNullOrWhiteSpace(destDistrictName) ? null : destDistrictName!.Trim());

        var originRegion = GetMacroRegion(originProvince);
        var destRegion = GetMacroRegion(destProvince);

        var (originSoc, originRegionalHub) = GetRegionHubLabels(originRegion);
        var (destSoc, destRegionalHub) = GetRegionHubLabels(destRegion);

        var sameProvince = !string.IsNullOrWhiteSpace(originProvince)
                           && !string.IsNullOrWhiteSpace(destProvince)
                           && string.Equals(originProvince, destProvince, StringComparison.OrdinalIgnoreCase);
        var sameRegion = originRegion != MacroRegion.Unknown && originRegion == destRegion;

        var originLabel = isReturn
            ? $"Nhận hàng hoàn (Khách - {JoinAddress(originDistrict, originProvince)})"
            : (origin is null
                ? "Kho lấy hàng (Shop)"
                : $"Kho lấy hàng (Shop - {JoinAddress(originDistrict, originProvince)})");
        var originAddr = isReturn
            ? (string.IsNullOrWhiteSpace(order.ShippingAddress) ? null : order.ShippingAddress.Trim())
            : (origin is null ? null : JoinAddress(origin.StreetAddress, origin.WardName, originDistrict, originProvince));

        var destSuffix = JoinAddress(destDistrict, destProvince);
        var destLabel = isReturn
            ? $"Trả hàng hoàn (Shop - {destSuffix})"
            : (string.IsNullOrWhiteSpace(destSuffix)
                ? "Giao hàng (Khách)"
                : $"Giao hàng (Khách - {destSuffix})");
        var destAddr = isReturn
            ? (origin is null ? null : JoinAddress(origin.StreetAddress, origin.WardName, destDistrict, destProvince))
            : (string.IsNullOrWhiteSpace(order.ShippingAddress) ? null : order.ShippingAddress.Trim());

        var steps = new List<DeliveryGhnRouteStepVm>
        {
            new()
            {
                Index = 1,
                Location = originLabel,
                StatusName = "",
                Message = originAddr,
                TimeRaw = null
            }
        };

        var originProvinceHub = string.IsNullOrWhiteSpace(originProvince)
            ? "Kho khai thác (giả lập)"
            : $"Kho khai thác (giả lập - {originProvince})";

        var destProvinceHub = string.IsNullOrWhiteSpace(destProvince)
            ? "Kho khai thác (giả lập)"
            : $"Kho khai thác (giả lập - {destProvince})";

        if (sameProvince)
        {
            steps.Add(new DeliveryGhnRouteStepVm
            {
                Index = steps.Count + 1,
                Location = originProvinceHub,
                StatusName = "",
                Message = "Phân loại & điều phối nội tỉnh (mô phỏng)",
                TimeRaw = null
            });
        }
        else if (sameRegion)
        {
            steps.Add(new DeliveryGhnRouteStepVm
            {
                Index = steps.Count + 1,
                Location = originProvinceHub,
                StatusName = "",
                Message = "Tập kết/khai thác tại tỉnh gửi (mô phỏng)",
                TimeRaw = null
            });
            steps.Add(new DeliveryGhnRouteStepVm
            {
                Index = steps.Count + 1,
                Location = originRegionalHub,
                StatusName = "",
                Message = "Trung chuyển trong cùng vùng (mô phỏng)",
                TimeRaw = null
            });
        }
        else
        {
            steps.Add(new DeliveryGhnRouteStepVm
            {
                Index = steps.Count + 1,
                Location = originProvinceHub,
                StatusName = "",
                Message = "Tập kết/khai thác tại tỉnh gửi (mô phỏng)",
                TimeRaw = null
            });
            steps.Add(new DeliveryGhnRouteStepVm
            {
                Index = steps.Count + 1,
                Location = originSoc,
                StatusName = "",
                Message = "Trung tâm phân loại tổng vùng gửi (mô phỏng)",
                TimeRaw = null
            });
            steps.Add(new DeliveryGhnRouteStepVm
            {
                Index = steps.Count + 1,
                Location = destSoc,
                StatusName = "",
                Message = "Trung tâm phân loại tổng vùng nhận (mô phỏng)",
                TimeRaw = null
            });
        }

        steps.Add(new DeliveryGhnRouteStepVm
        {
            Index = steps.Count + 1,
            Location = destProvinceHub,
            StatusName = "",
            Message = "Khai thác tại tỉnh nhận (mô phỏng)",
            TimeRaw = null
        });

        var destDistrictHub = string.IsNullOrWhiteSpace(destDistrict)
            ? "Kho phát (giả lập)"
            : $"Kho phát (giả lập - {destDistrict})";

        steps.Add(new DeliveryGhnRouteStepVm
        {
            Index = steps.Count + 1,
            Location = destDistrictHub,
            StatusName = "",
            Message = "Sẵn sàng giao / phát tuyến (mô phỏng)",
            TimeRaw = null
        });

        steps.Add(new DeliveryGhnRouteStepVm
        {
            Index = steps.Count + 1,
            Location = destLabel,
            StatusName = "",
            Message = destAddr,
            TimeRaw = null
        });

        // Apply progress/status text based on GHN status.
        var stage = MapGhnStatusToStage(ghnStatus);
        var currentIndex = StageToCurrentIndex(stage, steps.Count);

        var totalHours = sameProvince ? 12 : (sameRegion ? 30 : 60);
        var baseUtc = order.CreatedAt.Kind == DateTimeKind.Utc
            ? order.CreatedAt
            : DateTime.SpecifyKind(order.CreatedAt, DateTimeKind.Utc);

        var stepHours = steps.Count <= 1 ? 0 : (double)totalHours / (steps.Count - 1);
        for (var i = 0; i < steps.Count; i++)
        {
            var idx = i + 1;
            if (stage < 0)
            {
                steps[i].StatusName = idx == 1
                    ? (string.IsNullOrWhiteSpace(ghnStatusName) ? "Đơn đã hủy/hoàn" : ghnStatusName)
                    : "Không thực hiện";
                steps[i].TimeRaw = idx == 1 ? FormatVnTime(DateTime.UtcNow) : null;
                continue;
            }

            steps[i].StatusName = idx < currentIndex ? "Đã qua" : (idx == currentIndex ? "Đang xử lý" : "Dự kiến");

            var etaUtc = baseUtc.AddHours(stepHours * i);
            steps[i].TimeRaw = FormatVnTime(etaUtc);
        }

        // Add a hint on the current step.
        if (!string.IsNullOrWhiteSpace(ghnStatusName))
        {
            var cur = steps[Math.Clamp(currentIndex - 1, 0, steps.Count - 1)];
            cur.Message = string.IsNullOrWhiteSpace(cur.Message)
                ? $"Trạng thái: {ghnStatusName}"
                : $"{cur.Message} | Trạng thái: {ghnStatusName}";
        }

        return steps;
    }

    private async Task<List<DeliveryGhnRouteStepVm>> BuildOfflinePlannedRouteFromMasterDataAsync(UserAddress? origin, Order order, CancellationToken cancellationToken)
    {
        static string JoinAddress(params string?[] parts)
            => string.Join(", ", parts.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!.Trim()));

        string? provinceName = null;
        string? districtName = null;

        try
        {
            if (order.ShippingProvinceId is int provinceId && provinceId > 0)
            {
                var provinces = await _ghn.GetProvincesAsync(cancellationToken);
                provinceName = provinces.FirstOrDefault(x => x.ProvinceId == provinceId)?.ProvinceName;

                if (order.ShippingDistrictId is int districtId && districtId > 0)
                {
                    var districts = await _ghn.GetDistrictsAsync(provinceId, cancellationToken);
                    districtName = districts.FirstOrDefault(x => x.DistrictId == districtId)?.DistrictName;
                }
            }
        }
        catch
        {
            // Best-effort only; if GHN master-data fails, we still render a generic offline route.
        }

        var originLabel = origin is null
            ? "Kho lấy hàng (Shop)"
            : $"Kho lấy hàng (Shop - {JoinAddress(origin.DistrictName, origin.ProvinceName)})";

        var originAddress = origin is null
            ? null
            : JoinAddress(origin.StreetAddress, origin.WardName, origin.DistrictName, origin.ProvinceName);

        var destinationSuffix = JoinAddress(districtName, provinceName);
        var destinationLabel = string.IsNullOrWhiteSpace(destinationSuffix)
            ? "Kho phát / giao (Khách)"
            : $"Kho phát / giao (Khách - {destinationSuffix})";

        var destinationAddress = string.IsNullOrWhiteSpace(order.ShippingAddress) ? null : order.ShippingAddress.Trim();

        var hubProvince = !string.IsNullOrWhiteSpace(provinceName)
            ? provinceName!.Trim()
            : (string.IsNullOrWhiteSpace(origin?.ProvinceName) ? "" : origin!.ProvinceName!.Trim());

        var hubLabel = string.IsNullOrWhiteSpace(hubProvince)
            ? "Kho trung chuyển lớn (giả lập)"
            : $"Kho trung chuyển lớn (giả lập - {hubProvince})";

        return new List<DeliveryGhnRouteStepVm>
        {
            new()
            {
                Index = 1,
                Location = originLabel,
                StatusName = "Dự kiến",
                Message = originAddress,
                TimeRaw = null
            },
            new()
            {
                Index = 2,
                Location = hubLabel,
                StatusName = "Dự kiến",
                Message = "(Offline) Không đồng bộ được danh sách kho; đang dùng tuyến giả lập.",
                TimeRaw = null
            },
            new()
            {
                Index = 3,
                Location = destinationLabel,
                StatusName = "Dự kiến",
                Message = destinationAddress,
                TimeRaw = null
            }
        };
    }

    private static List<DeliveryGhnRouteStepVm> BuildPlannedRouteFromCatalog(
        UserAddress? originAddress,
        Order order,
        IReadOnlyList<GhnStationRecord> originStations,
        IReadOnlyList<GhnStationRecord> destStations,
        IReadOnlyList<GhnStationRecord> allStations)
    {
        static string FormatStation(GhnStationRecord s)
        {
            var name = string.IsNullOrWhiteSpace(s.LocationName) ? $"Bưu cục #{s.LocationId}" : s.LocationName.Trim();
            var suffix = string.Join(", ", new[] { s.DistrictName, s.ProvinceName }
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x!.Trim()));
            return string.IsNullOrWhiteSpace(suffix) ? name : $"{name} ({suffix})";
        }

        static GhnStationRecord? PickBestEndpoint(IReadOnlyList<GhnStationRecord> stations)
        {
            return stations
                .OrderByDescending(x => x.Latitude.HasValue && x.Longitude.HasValue)
                .ThenBy(x => x.IsHub) // prefer non-hub for endpoints
                .ThenBy(x => x.LocationName)
                .FirstOrDefault();
        }

        var originPick = PickBestEndpoint(originStations);
        var destPick = PickBestEndpoint(destStations);

        // If we can't even identify endpoints, fall back to the simplest offline route.
        if (originPick is null && destPick is null)
        {
            return BuildOfflinePlannedRoute(originAddress, order, note: "(Offline) Không có dữ liệu bưu cục/kho để dựng tuyến (chưa sync/cache được station catalog)."
            );
        }

        GhnStationRecord? hubPick = null;
        if (originPick is not null && destPick is not null)
        {
            var hubs = allStations
                .Where(x => x.IsHub)
                .Where(x => x.Latitude.HasValue && x.Longitude.HasValue)
                .Where(x => x.LocationId != originPick.LocationId && x.LocationId != destPick.LocationId)
                .ToList();

            double? BestScore = null;
            foreach (var h in hubs)
            {
                var d1 = GhnStationCatalog.DistanceKm(originPick.Latitude, originPick.Longitude, h.Latitude, h.Longitude);
                var d2 = GhnStationCatalog.DistanceKm(h.Latitude, h.Longitude, destPick.Latitude, destPick.Longitude);
                if (!d1.HasValue || !d2.HasValue) continue;

                var score = d1.Value + d2.Value;
                if (!BestScore.HasValue || score < BestScore.Value)
                {
                    BestScore = score;
                    hubPick = h;
                }
            }
        }

        var steps = new List<DeliveryGhnRouteStepVm>();

        if (originPick is not null)
        {
            steps.Add(new DeliveryGhnRouteStepVm
            {
                Index = steps.Count + 1,
                Location = FormatStation(originPick),
                StatusName = "Kho lấy hàng (dự kiến)",
                Message = originPick.Address,
                TimeRaw = null
            });
        }

        if (hubPick is not null)
        {
            steps.Add(new DeliveryGhnRouteStepVm
            {
                Index = steps.Count + 1,
                Location = FormatStation(hubPick),
                StatusName = "Kho trung chuyển lớn (hub - giả lập)",
                Message = hubPick.Address,
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
                Message = string.IsNullOrWhiteSpace(destPick.Address) ? (string.IsNullOrWhiteSpace(order.ShippingAddress) ? null : order.ShippingAddress.Trim()) : destPick.Address,
                TimeRaw = null
            });
        }

        return steps;
    }

    private static DateTimeOffset? TryParseTime(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        return DateTimeOffset.TryParse(raw, out var parsed) ? parsed : null;
    }

    private static List<DeliveryGhnRouteStepVm> BuildRouteFromOrderDetail(GhnShippingOrderDetail detail)
    {
        var logs = detail.Logs
            .Select((log, i) => new
            {
                Log = log,
                Index = i,
                ParsedTime = TryParseTime(log.TimeRaw)
            })
            .OrderBy(x => x.ParsedTime ?? DateTimeOffset.MaxValue)
            .ThenBy(x => x.Index)
            .Select(x => x.Log)
            .ToList();

        var steps = logs
            .Select((x, i) => new DeliveryGhnRouteStepVm
            {
                Index = i + 1,
                Location = string.IsNullOrWhiteSpace(x.Location) ? "Hành trình" : x.Location,
                StatusName = string.IsNullOrWhiteSpace(x.StatusName) ? "Đang xử lý" : x.StatusName,
                Message = x.Message,
                TimeRaw = x.TimeRaw
            })
            .ToList();

        if (steps.Count == 0 && (!string.IsNullOrWhiteSpace(detail.StatusName) || !string.IsNullOrWhiteSpace(detail.Status)))
        {
            steps.Add(new DeliveryGhnRouteStepVm
            {
                Index = 1,
                Location = "Đơn hàng",
                StatusName = string.IsNullOrWhiteSpace(detail.StatusName) ? detail.Status : detail.StatusName,
                Message = null,
                TimeRaw = null
            });
        }

        return steps;
    }

    private sealed record SimProgress(HashSet<int> Arrived, HashSet<int> Departed, HashSet<int> Refused, int CurrentStepIndex, bool IsComplete);

    private static bool TryParseSimNote(string? note, bool isReturn, out int stepIndex, out string ev)
    {
        stepIndex = 0;
        ev = string.Empty;
        if (string.IsNullOrWhiteSpace(note)) return false;

        var idx = note.IndexOf(" - ", StringComparison.Ordinal);
        var head = (idx >= 0 ? note[..idx] : note).Trim();

        var prefix = isReturn ? "SIM_RET|" : SimNotePrefix;
        if (!head.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;

        // Format: SIM|i=3|e=arrive or SIM_RET|i=3|e=arrive
        var parts = head.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 3) return false;

        foreach (var part in parts)
        {
            if (part.Equals("SIM", StringComparison.OrdinalIgnoreCase) || part.Equals("SIM_RET", StringComparison.OrdinalIgnoreCase)) continue;
            var eq = part.IndexOf('=');
            if (eq <= 0) continue;

            var key = part[..eq].Trim();
            var val = part[(eq + 1)..].Trim();
            if (key.Equals("i", StringComparison.OrdinalIgnoreCase))
            {
                _ = int.TryParse(val, out stepIndex);
            }
            else if (key.Equals("e", StringComparison.OrdinalIgnoreCase))
            {
                ev = val;
            }
        }

        if (stepIndex <= 0) return false;
        if (string.IsNullOrWhiteSpace(ev)) return false;
        return true;
    }

    private async Task<SimProgress> GetSimProgressAsync(int orderId, int routeStepCount, bool isReturn, CancellationToken cancellationToken)
    {
        var arrived = new HashSet<int>();
        var departed = new HashSet<int>();
        var refused = new HashSet<int>();

        if (orderId <= 0 || routeStepCount <= 0)
        {
            return new SimProgress(arrived, departed, refused, CurrentStepIndex: 1, IsComplete: false);
        }

        var prefixPattern = isReturn ? "SIM_RET|%" : "SIM|%";
        var logs = await _db.OrderTrackingLogs
            .AsNoTracking()
            .Where(x => x.OrderId == orderId
                && x.Note != null
                && EF.Functions.Like(x.Note, prefixPattern))
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(cancellationToken);

        foreach (var log in logs)
        {
            if (isReturn && log.Note.StartsWith("SIM|", StringComparison.OrdinalIgnoreCase) && !log.Note.StartsWith("SIM_RET|", StringComparison.OrdinalIgnoreCase)) continue;
            if (!isReturn && log.Note.StartsWith("SIM_RET|", StringComparison.OrdinalIgnoreCase)) continue;

            if (!TryParseSimNote(log.Note, isReturn, out var stepIndex, out var ev)) continue;
            if (stepIndex <= 0 || stepIndex > routeStepCount) continue;

            var e = ev.Trim().ToLowerInvariant();
            if (e is "arrive" or "a")
            {
                arrived.Add(stepIndex);
            }
            else if (e is "depart" or "leave" or "d")
            {
                departed.Add(stepIndex);
            }
            else if (e is "refuse" or "reject" or "r")
            {
                // Terminal failure for the step.
                departed.Add(stepIndex);
                refused.Add(stepIndex);
            }
        }

        // Current step: first step that hasn't been departed yet.
        var current = 1;
        for (var i = 1; i <= routeStepCount; i++)
        {
            if (!departed.Contains(i))
            {
                current = i;
                break;
            }

            if (i == routeStepCount)
            {
                current = routeStepCount;
            }
        }

        var isComplete = departed.Count >= routeStepCount;
        return new SimProgress(arrived, departed, refused, current, isComplete);
    }

    private static void ApplySimProgressToSteps(List<DeliveryGhnRouteStepVm> steps, SimProgress progress)
    {
        if (steps.Count == 0) return;

        foreach (var step in steps)
        {
            step.SimArrived = progress.Arrived.Contains(step.Index);
            step.SimDeparted = progress.Departed.Contains(step.Index);
            step.SimRefused = progress.Refused.Contains(step.Index);

            step.SimIsActive = !progress.IsComplete && step.Index == progress.CurrentStepIndex;
            step.SimCanArrive = step.SimIsActive && !step.SimArrived;
            step.SimCanDepart = step.SimIsActive && step.SimArrived && !step.SimDeparted;
        }
    }


    private async Task<(DeliveryGhnSimulatorVm Vm, string? Toast)> BuildSimulatorVmAsync(
        DeliveryGhnSimulatorQueryVm query,
        CancellationToken cancellationToken)
    {
        var toast = (string?)null;
        var vm = new DeliveryGhnSimulatorVm
        {
            Query = new DeliveryGhnSimulatorQueryVm
            {
                TrackingCode = query.TrackingCode,
                SyncStationCatalog = query.SyncStationCatalog
            }
        };

        if (!TryGetSignedInAccount(out var accountId, out var role))
        {
            toast = "Vui lòng đăng nhập để sử dụng trình giả lập.";
            return (vm, toast);
        }

        if (!IsInRole(role, "Admin", "Shop", "Delivery"))
        {
            toast = "Bạn không có quyền sử dụng trình giả lập.";
            return (vm, toast);
        }

        Order? order = null;
        bool isReturnReq = false;
        ReturnRequest? returnReq = null;

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
                // Secondary check: look up in ReturnRequests!
                returnReq = await _db.ReturnRequests
                    .Include(r => r.Order)
                    .Include(r => r.Customer)
                    .FirstOrDefaultAsync(r => r.ReturnTrackingCode == code || r.ReturnTrackingCode == compact, cancellationToken);

                if (returnReq is not null && returnReq.Order is not null)
                {
                    bool hasPermission = IsInRole(role, "Admin", "Delivery") || (IsInRole(role, "Shop") && returnReq.Order.ShopId == accountId);
                    if (hasPermission)
                    {
                        order = returnReq.Order;
                        isReturnReq = true;
                    }
                }
            }

            if (order is null)
            {
                toast = $"Không tìm thấy đơn hàng với mã vận đơn '{code}'.";
            }
        }

        if (order is null)
        {
            return (vm, toast);
        }

        vm.ResolvedOrderId = order.Id;
        vm.ResolvedOrderCode = order.OrderCode;
        vm.ResolvedTrackingCode = isReturnReq ? returnReq!.ReturnTrackingCode : order.TrackingCode;
        vm.IsReturnRequestSim = isReturnReq;

        UserAddress? origin = null;
        Exception? detailEx = null;
        List<DeliveryGhnRouteStepVm>? detailStatusOnlySteps = null;
        try
        {
            origin = await _db.UserAddresses
                .AsNoTracking()
                .Where(x => x.AccountId == order.ShopId)
                .OrderByDescending(x => x.IsDefault)
                .ThenByDescending(x => x.UpdatedAt)
                .FirstOrDefaultAsync(cancellationToken);

            if (isReturnReq)
            {
                vm.GhnStatusName = "Đang hoàn hàng";
                try
                {
                    vm.RouteSteps = await BuildSimulatedWarehouseNetworkRouteAsync(origin, order, "returning", "Đang hoàn hàng", true, cancellationToken);
                }
                catch
                {
                    vm.RouteSteps = BuildOfflinePlannedRouteForReturn(origin, order);
                }

                var simProgress = await GetSimProgressAsync(order.Id, vm.RouteSteps.Count, true, cancellationToken);
                ApplySimProgressToSteps(vm.RouteSteps, simProgress);

                return (vm, toast);
            }

            if (vm.Query.SyncStationCatalog && IsInRole(role, "Admin", "Delivery"))
            {
                try
                {
                    var sync = await _stationCatalog.SyncAllStationsAsync(cancellationToken);
                    toast = $"Đã đồng bộ station catalog: {sync.StationCount} kho/bưu cục (mất {sync.Duration.TotalSeconds:0}s).";
                }
                catch (Exception syncEx)
                {
                    toast = $"Không đồng bộ được station catalog: {syncEx.Message}";
                }
            }

            if (!string.IsNullOrWhiteSpace(order.TrackingCode))
            {
                try
                {
                    var detail = await _ghn.GetShippingOrderDetailAsync(order.TrackingCode.Trim(), cancellationToken);
                    vm.GhnStatusName = string.IsNullOrWhiteSpace(detail.StatusName) ? detail.Status : detail.StatusName;

                    vm.RouteSteps = await BuildSimulatedWarehouseNetworkRouteAsync(origin, order, detail.Status, detail.StatusName, false, cancellationToken);
                    detailStatusOnlySteps = null;

                    var simProgress = await GetSimProgressAsync(order.Id, vm.RouteSteps.Count, false, cancellationToken);
                    ApplySimProgressToSteps(vm.RouteSteps, simProgress);

                    return (vm, toast);
                }
                catch (Exception ex)
                {
                    detailEx = ex;
                }
            }

            var destinationDistrictId = order.ShippingDistrictId;
            var destinationWardCode = order.ShippingWardCode;

            if (destinationDistrictId is null || destinationDistrictId.Value <= 0)
            {
                toast = "Đơn hàng chưa có ShippingDistrictId nên không dựng được lộ trình dự kiến. (Cần lưu district/ward khi checkout)";
                if (detailStatusOnlySteps is { Count: > 0 })
                {
                    vm.RouteSteps = detailStatusOnlySteps;
                }

                if (vm.RouteSteps.Count > 0)
                {
                    var simProgress = await GetSimProgressAsync(order.Id, vm.RouteSteps.Count, false, cancellationToken);
                    ApplySimProgressToSteps(vm.RouteSteps, simProgress);
                }

                return (vm, toast);
            }

            var originLookup = origin is not null && origin.DistrictId > 0
                ? await _stationCatalog.GetStationsForDistrictAsync(origin.DistrictId, origin.WardCode, cancellationToken)
                : new GhnStationLookupResult(Array.Empty<GhnStationRecord>(), FromCache: false, UsedCacheBecauseGhnRejectedToken: false, CacheAvailable: false);

            var destLookup = await _stationCatalog.GetStationsForDistrictAsync(destinationDistrictId.Value, destinationWardCode, cancellationToken);

            var (snapshot, isFresh) = await _stationCatalog.TryGetSnapshotAsync(cancellationToken);
            var allStations = snapshot?.Stations ?? new List<GhnStationRecord>();

            var skippedStationApi = originLookup.SkippedStationApi || destLookup.SkippedStationApi;
            if (skippedStationApi && toast is null)
            {
                toast = "Không lấy được danh sách điểm xử lý; đang dùng tuyến giả lập.";
            }

            var usedCacheBecauseGhnRejected = originLookup.UsedCacheBecauseGhnRejectedToken || destLookup.UsedCacheBecauseGhnRejectedToken;
            if (usedCacheBecauseGhnRejected && !skippedStationApi)
            {
                toast = "Không lấy được danh sách điểm xử lý; đang dùng tuyến giả lập.";
            }

            if (originLookup.Stations.Count > 0 || destLookup.Stations.Count > 0)
            {
                vm.RouteSteps = BuildPlannedRouteFromCatalog(origin, order, originLookup.Stations.ToList(), destLookup.Stations.ToList(), allStations);
            }
            else
            {
                vm.RouteSteps = await BuildOfflinePlannedRouteFromMasterDataAsync(origin, order, cancellationToken);
            }

            if (vm.RouteSteps.Count > 0)
            {
                var simProgress = await GetSimProgressAsync(order.Id, vm.RouteSteps.Count, false, cancellationToken);
                ApplySimProgressToSteps(vm.RouteSteps, simProgress);
            }

            return (vm, toast);
        }
        catch (Exception ex)
        {
            var msg = ex.Message ?? string.Empty;
            if (msg.Contains("SECURITY_CHECK_FAILED", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("invalid number of segments", StringComparison.OrdinalIgnoreCase))
            {
                toast = "Không đồng bộ được danh sách kho; đang hiển thị lộ trình giả lập để bạn vẫn có thể check-in.";

                origin ??= await _db.UserAddresses
                    .AsNoTracking()
                    .Where(x => x.AccountId == order.ShopId)
                    .OrderByDescending(x => x.IsDefault)
                    .ThenByDescending(x => x.UpdatedAt)
                    .FirstOrDefaultAsync(cancellationToken);

                vm.RouteSteps = BuildOfflinePlannedRoute(origin, order);

                if (vm.RouteSteps.Count > 0)
                {
                    var simProgress = await GetSimProgressAsync(order.Id, vm.RouteSteps.Count, false, cancellationToken);
                    ApplySimProgressToSteps(vm.RouteSteps, simProgress);
                }
            }
            else if (msg.Contains("GHN token is missing", StringComparison.OrdinalIgnoreCase))
            {
                toast = "Thiếu cấu hình token dịch vụ vận chuyển; đang hiển thị lộ trình giả lập.";

                origin ??= await _db.UserAddresses
                    .AsNoTracking()
                    .Where(x => x.AccountId == order.ShopId)
                    .OrderByDescending(x => x.IsDefault)
                    .ThenByDescending(x => x.UpdatedAt)
                    .FirstOrDefaultAsync(cancellationToken);

                vm.RouteSteps = BuildOfflinePlannedRoute(origin, order, note: "(Offline) Thiếu cấu hình token nên chỉ dựng được tuyến giả lập cơ bản.");

                if (vm.RouteSteps.Count > 0)
                {
                    var simProgress = await GetSimProgressAsync(order.Id, vm.RouteSteps.Count, false, cancellationToken);
                    ApplySimProgressToSteps(vm.RouteSteps, simProgress);
                }
            }
            else
            {
                toast = $"Không dựng được lộ trình: {ex.Message}";

                origin ??= await _db.UserAddresses
                    .AsNoTracking()
                    .Where(x => x.AccountId == order.ShopId)
                    .OrderByDescending(x => x.IsDefault)
                    .ThenByDescending(x => x.UpdatedAt)
                    .FirstOrDefaultAsync(cancellationToken);

                vm.RouteSteps = BuildOfflinePlannedRoute(origin, order, note: "(Offline) Không dựng được lộ trình do lỗi bất ngờ.");

                if (vm.RouteSteps.Count > 0)
                {
                    var simProgress = await GetSimProgressAsync(order.Id, vm.RouteSteps.Count, false, cancellationToken);
                    ApplySimProgressToSteps(vm.RouteSteps, simProgress);
                }
            }

            return (vm, toast);
        }
    }

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] DeliveryGhnSimulatorQueryVm query, CancellationToken cancellationToken)
    {
        var (vm, toast) = await BuildSimulatorVmAsync(query, cancellationToken);
        if (!string.IsNullOrWhiteSpace(toast))
        {
            TempData["DeliveryToast"] = toast;
        }
        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> RoutePanel([FromQuery] DeliveryGhnSimulatorQueryVm query, CancellationToken cancellationToken)
    {
        var (vm, toast) = await BuildSimulatorVmAsync(query, cancellationToken);
        SetToastHeader(Response, toast);
        return PartialView("_RoutePanel", vm);
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

        // Check if this is a Return Request tracking code!
        var returnReq = await _db.ReturnRequests
            .FirstOrDefaultAsync(r => r.OrderId == model.OrderId && r.ReturnTrackingCode == model.TrackingCode, cancellationToken);
        bool isReturnReq = returnReq is not null;

        // Enforce sequential simulation: must arrive before depart; must finish this step before moving to next.
        var (routeVm, _) = await BuildSimulatorVmAsync(new DeliveryGhnSimulatorQueryVm { TrackingCode = model.TrackingCode }, cancellationToken);
        var lastNonInternalStepIndex = routeVm.RouteSteps
            .Where(x => !string.Equals(x.Location, "Log nội bộ", StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Index)
            .DefaultIfEmpty(1)
            .Max();
        var routeStepCount = Math.Max(1, lastNonInternalStepIndex);
        var progress = await GetSimProgressAsync(order.Id, routeStepCount, isReturnReq, cancellationToken);
        var expectedStep = progress.CurrentStepIndex;
        if (model.StepIndex != expectedStep)
        {
            var toastSeq = $"Chưa tới lượt kho này. Hãy check-in theo thứ tự (bước hiện tại: {expectedStep}).";
            if (IsAjaxRequest(Request))
            {
                SetToastHeader(Response, toastSeq);
                return PartialView("_RoutePanel", routeVm);
            }

            TempData["DeliveryToast"] = toastSeq;
            return RedirectToAction(nameof(Index), new { TrackingCode = model.TrackingCode });
        }

        var locationName = string.IsNullOrWhiteSpace(model.RouteLocation)
            ? "(không rõ kho)"
            : model.RouteLocation.Trim();

        var isInternalLogLocation = string.Equals(locationName, "Log nội bộ", StringComparison.OrdinalIgnoreCase);

        var ev = (model.Event ?? string.Empty).Trim().ToLowerInvariant();

                var alreadyArrived = progress.Arrived.Contains(model.StepIndex);
                var alreadyDeparted = progress.Departed.Contains(model.StepIndex);
                if (ev is "arrive" or "a")
                {
                    if (alreadyArrived)
                    {
                        var toastArr = "Bước này đã được check-in 'Đã đến kho' rồi.";
                        if (IsAjaxRequest(Request))
                        {
                            var (vmArr, vmToast) = await BuildSimulatorVmAsync(new DeliveryGhnSimulatorQueryVm { TrackingCode = model.TrackingCode }, cancellationToken);
                            SetToastHeader(Response, vmToast ?? toastArr);
                            return PartialView("_RoutePanel", vmArr);
                        }

                        TempData["DeliveryToast"] = toastArr;
                        return RedirectToAction(nameof(Index), new { TrackingCode = model.TrackingCode });
                    }
                }
                else if (ev is "depart" or "leave" or "d")
                {
                    if (!alreadyArrived)
                    {
                        var toastDep = "Bạn cần bấm 'Đã đến kho' trước khi bấm 'Đã rời kho'.";
                        if (IsAjaxRequest(Request))
                        {
                            var (vmDep, vmToast) = await BuildSimulatorVmAsync(new DeliveryGhnSimulatorQueryVm { TrackingCode = model.TrackingCode }, cancellationToken);
                            SetToastHeader(Response, vmToast ?? toastDep);
                            return PartialView("_RoutePanel", vmDep);
                        }

                        TempData["DeliveryToast"] = toastDep;
                        return RedirectToAction(nameof(Index), new { TrackingCode = model.TrackingCode });
                    }

                    if (alreadyDeparted)
                    {
                        var toastDep2 = "Bước này đã được check-in 'Đã rời kho' rồi.";
                        if (IsAjaxRequest(Request))
                        {
                            var (vmDep2, vmToast) = await BuildSimulatorVmAsync(new DeliveryGhnSimulatorQueryVm { TrackingCode = model.TrackingCode }, cancellationToken);
                            SetToastHeader(Response, vmToast ?? toastDep2);
                            return PartialView("_RoutePanel", vmDep2);
                        }

                        TempData["DeliveryToast"] = toastDep2;
                        return RedirectToAction(nameof(Index), new { TrackingCode = model.TrackingCode });
                    }
                }
                else if (ev is "refuse" or "reject" or "r")
                {
                    if (!alreadyArrived)
                    {
                        var toastRef = "Bạn cần bấm 'Nhận đơn giao' trước khi bấm 'Khách hàng không nhận đơn'.";
                        if (IsAjaxRequest(Request))
                        {
                            var (vmRef, vmToast) = await BuildSimulatorVmAsync(new DeliveryGhnSimulatorQueryVm { TrackingCode = model.TrackingCode }, cancellationToken);
                            SetToastHeader(Response, vmToast ?? toastRef);
                            return PartialView("_RoutePanel", vmRef);
                        }

                        TempData["DeliveryToast"] = toastRef;
                        return RedirectToAction(nameof(Index), new { TrackingCode = model.TrackingCode });
                    }

                    if (alreadyDeparted)
                    {
                        var toastRef2 = "Bước này đã kết thúc rồi.";
                        if (IsAjaxRequest(Request))
                        {
                            var (vmRef2, vmToast) = await BuildSimulatorVmAsync(new DeliveryGhnSimulatorQueryVm { TrackingCode = model.TrackingCode }, cancellationToken);
                            SetToastHeader(Response, vmToast ?? toastRef2);
                            return PartialView("_RoutePanel", vmRef2);
                        }

                        TempData["DeliveryToast"] = toastRef2;
                        return RedirectToAction(nameof(Index), new { TrackingCode = model.TrackingCode });
                    }
                }
                else
                {
                    ev = "arrive";
                }

            var isLastStep = !isInternalLogLocation && model.StepIndex == routeStepCount;

            string? shipperName = null;
            string? shipperPhone = null;
            if (isLastStep)
            {
                var shipper = await ResolveOrAssignSimShipperAsync(order, cancellationToken);
                shipperName = shipper.Name;
                shipperPhone = shipper.Phone;
            }

            var noteDefault = (isLastStep, ev) switch
            {
                (true, "depart" or "leave" or "d") => isReturnReq ? "Đã trả hàng hoàn thành công về kho Shop" : "Đã giao thành công",
                (true, "refuse" or "reject" or "r") => isReturnReq ? "Shop từ chối nhận hàng hoàn" : "Khách hàng không nhận đơn",
                (true, _) => isReturnReq
                    ? (shipperName is not null && shipperPhone is not null
                        ? $"Shipper {shipperName} ({shipperPhone}) đang giao trả về kho Shop"
                        : (shipperName is not null ? $"Shipper {shipperName} đang giao trả về kho Shop" : "Shipper đang giao trả về kho Shop"))
                    : (shipperName is not null && shipperPhone is not null
                        ? $"Shipper {shipperName} ({shipperPhone}) đang giao cho khách"
                        : (shipperName is not null ? $"Shipper {shipperName} đang giao cho khách" : "Shipper đang giao cho khách")),
                (false, "depart" or "leave") => isInternalLogLocation ? "Đã cập nhật trạng thái vận chuyển (rời điểm xử lý)." : $"Đã rời {locationName}",
                (false, _) => isInternalLogLocation ? "Đã cập nhật trạng thái vận chuyển (đến điểm xử lý)." : $"Đã đến {locationName}",
            };

            var noteBody = string.IsNullOrWhiteSpace(model.CustomNote) ? noteDefault : model.CustomNote.Trim();

            var meta = string.Empty;
            if (isLastStep)
            {
                // Extra metadata helps customer UI show shipper info without leaking SIM prefix.
                if (!string.IsNullOrWhiteSpace(shipperName))
                {
                    meta += $"|shipper={Uri.EscapeDataString(shipperName)}";
                }
                if (!string.IsNullOrWhiteSpace(shipperPhone))
                {
                    meta += $"|phone={Uri.EscapeDataString(shipperPhone)}";
                }
            }

            // Persist simulation state in a parseable prefix (no DB schema changes needed).
            // Example: SIM|i=3|e=arrive|shipper=...|phone=... - <human note> or SIM_RET|i=3|e=arrive|shipper=...|phone=... - <human note>
            var notePrefix = isReturnReq ? "SIM_RET|" : SimNotePrefix;
            var note = $"{notePrefix}i={model.StepIndex}|e={ev}{meta} - {noteBody}";

            if (note.Length > 255)
            {
                note = note[..255];
            }

        var logStatus = "Shipping";

        // Terminal last-mile outcomes
        if (isLastStep && ev is "depart" or "leave" or "d")
        {
            logStatus = isReturnReq ? "Returned" : "Delivered";
        }
        else if (isLastStep && ev is "refuse" or "reject" or "r")
        {
            logStatus = "Returned";
        }

        _db.OrderTrackingLogs.Add(new OrderTrackingLog
        {
            OrderId = order.Id,
            Status = logStatus,
            Note = note,
            CreatedAt = DateTime.UtcNow
        });

        // Last-mile terminal state.
        if (isLastStep)
        {
            if (ev is "depart" or "leave" or "d")
            {
                order.OrderStatus = isReturnReq ? "Returned" : "Delivered";

                if (isReturnReq)
                {
                    // Refund the FinalAmount to the customer's wallet upon successful return delivery
                    order.PaymentStatus = "Refunded";

                    var refundResult = await _walletService.RefundAsync(
                        order.CustomerId,
                        order.FinalAmount,
                        order.OrderCode,
                        $"Hoàn tiền trả hàng thành công đơn hàng {order.OrderCode}",
                        cancellationToken);

                    _db.OrderTrackingLogs.Add(new OrderTrackingLog
                    {
                        OrderId = order.Id,
                        Status = "Returned",
                        Note = refundResult.Success
                            ? $"Hệ thống đã tự động hoàn tiền {order.FinalAmount:N0}đ về ví cho bạn."
                            : $"Lỗi tự động hoàn tiền về ví: {refundResult.Message}",
                        CreatedAt = DateTime.UtcNow
                    });
                }
            }
            else if (ev is "refuse" or "reject" or "r")
            {
                order.OrderStatus = "Returned";
            }
        }

        // When the parcel leaves the pickup warehouse, the order is now in transit.
        var isPickupStep = model.StepIndex == 1
            || locationName.Contains("kho lấy hàng", StringComparison.OrdinalIgnoreCase)
            || locationName.Contains("pickup", StringComparison.OrdinalIgnoreCase)
            || locationName.Contains("nơi lấy hàng", StringComparison.OrdinalIgnoreCase);
        if (isPickupStep && ev is "depart" or "leave" or "d")
        {
            var rawStatus = (order.OrderStatus ?? string.Empty).Trim();
            var norm = rawStatus.Replace(" ", string.Empty).ToLowerInvariant();

            // Do not override terminal states.
            var isTerminal = norm is (
                "cancelled" or "canceled" or "cancel" or "đãhủy" or "dahuy" or
                "returned" or "return" or "refund" or "completed" or "success" or "hoàntất" or "hoantat" or
                "delivered" or "deliverysuccess" or "dagiao" or "đãgiao");

            if (!isTerminal)
            {
                // Once departed pickup, the order should be in transit.
                order.OrderStatus = isReturnReq ? "Returning" : "Shipping";
            }
        }

        order.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);

        var toast = "Đã ghi nhận check-in kho (giả lập).";
        if (IsAjaxRequest(Request))
        {
            var (vm, vmToast) = await BuildSimulatorVmAsync(new DeliveryGhnSimulatorQueryVm { TrackingCode = model.TrackingCode }, cancellationToken);
            SetToastHeader(Response, vmToast ?? toast);
            return PartialView("_RoutePanel", vm);
        }

        TempData["DeliveryToast"] = toast;
        return RedirectToAction(nameof(Index), new { TrackingCode = model.TrackingCode });
    }
}
