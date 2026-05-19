using DOAN_BANHANG.Models;
using Microsoft.EntityFrameworkCore;

namespace DOAN_BANHANG.Services;

public sealed class WarehouseNetworkSeeder
{
    private const string NodeSoc = "Soc";
    private const string NodeProvince = "ProvinceHub";
    private const string NodeDistrict = "DistrictHub";

    private const string RegionNorth = "North";
    private const string RegionCentral = "Central";
    private const string RegionSouth = "South";
    private const string RegionUnknown = "Unknown";

    public async Task SeedFromGhnAsync(
        DataContext db,
        GhnClient ghn,
        ILogger logger,
        bool includeDistricts,
        bool resetExisting,
        CancellationToken cancellationToken)
    {
        if (resetExisting)
        {
            logger.LogWarning("Resetting warehouse network (Warehouses/WarehouseRoutes)...");
            await db.WarehouseRoutes.ExecuteDeleteAsync(cancellationToken);
            await db.Warehouses.ExecuteDeleteAsync(cancellationToken);
        }

        if (await db.Warehouses.AsNoTracking().AnyAsync(cancellationToken))
        {
            logger.LogInformation("Warehouse network already exists; skipping seed.");
            return;
        }

        logger.LogInformation("Seeding warehouse network from GHN master-data... includeDistricts={IncludeDistricts}", includeDistricts);

        // ---- 1) SOC nodes ----
        var socNorth = new Warehouse { Code = "SOC-N", Name = "SOC Miền Bắc", NodeType = NodeSoc, Region = RegionNorth };
        var socCentral = new Warehouse { Code = "SOC-C", Name = "SOC Miền Trung", NodeType = NodeSoc, Region = RegionCentral };
        var socSouth = new Warehouse { Code = "SOC-S", Name = "SOC Miền Nam", NodeType = NodeSoc, Region = RegionSouth };

        db.Warehouses.AddRange(socNorth, socCentral, socSouth);
        await db.SaveChangesAsync(cancellationToken);

        // ---- 2) Province hubs ----
        IReadOnlyList<GhnProvince> provinces;
        try
        {
            provinces = await ghn.GetProvincesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch GHN provinces; warehouse network seed skipped.");
            return;
        }

        provinces = FilterAndDeduplicateProvinces(provinces, logger);

        var socByRegion = new Dictionary<string, Warehouse>(StringComparer.OrdinalIgnoreCase)
        {
            [RegionNorth] = socNorth,
            [RegionCentral] = socCentral,
            [RegionSouth] = socSouth,
            [RegionUnknown] = socCentral
        };

        var provinceWarehouseByProvinceId = new Dictionary<int, Warehouse>();

        foreach (var p in provinces)
        {
            var region = GuessRegionFromProvinceName(p.ProvinceName);
            var code = $"PROV-{p.ProvinceId}";
            var name = $"Hub tỉnh/thành - {p.ProvinceName}";

            var wh = new Warehouse
            {
                Code = code,
                Name = name,
                NodeType = NodeProvince,
                Region = region,
                GhnProvinceId = p.ProvinceId
            };

            db.Warehouses.Add(wh);
            provinceWarehouseByProvinceId[p.ProvinceId] = wh;
        }

        await db.SaveChangesAsync(cancellationToken);

        // ---- 3) Province <-> SOC routes ----
        var routes = new List<WarehouseRoute>();
        foreach (var p in provinces)
        {
            if (!provinceWarehouseByProvinceId.TryGetValue(p.ProvinceId, out var prov)) continue;

            var region = prov.Region ?? RegionUnknown;
            if (!socByRegion.TryGetValue(region, out var soc)) soc = socCentral;

            routes.Add(new WarehouseRoute
            {
                FromWarehouseId = prov.Id,
                ToWarehouseId = soc.Id,
                RouteKind = "Linehaul",
                DefaultHours = 10
            });
            routes.Add(new WarehouseRoute
            {
                FromWarehouseId = soc.Id,
                ToWarehouseId = prov.Id,
                RouteKind = "Linehaul",
                DefaultHours = 10
            });
        }

        db.WarehouseRoutes.AddRange(routes);
        await db.SaveChangesAsync(cancellationToken);

        // ---- 4) District hubs (dense) ----
        if (!includeDistricts)
        {
            logger.LogInformation("Seeded: SOC={SocCount}, Provinces={ProvinceCount}", 3, provinces.Count);
            return;
        }

        var totalDistricts = 0;
        foreach (var p in provinces)
        {
            if (!provinceWarehouseByProvinceId.TryGetValue(p.ProvinceId, out var prov)) continue;

            IReadOnlyList<GhnDistrict> districts;
            try
            {
                districts = await ghn.GetDistrictsAsync(p.ProvinceId, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to fetch districts for provinceId={ProvinceId} ({ProvinceName}). Skipping.", p.ProvinceId, p.ProvinceName);
                continue;
            }

            var newDistrictWarehouses = new List<Warehouse>();
            var newDistrictRoutes = new List<WarehouseRoute>();

            foreach (var d in districts)
            {
                var distCode = $"DIST-{d.DistrictId}";
                var distName = $"Hub quận/huyện - {d.DistrictName} ({p.ProvinceName})";

                newDistrictWarehouses.Add(new Warehouse
                {
                    Code = distCode,
                    Name = distName,
                    NodeType = NodeDistrict,
                    Region = prov.Region,
                    GhnProvinceId = p.ProvinceId,
                    GhnDistrictId = d.DistrictId,
                    ParentId = prov.Id
                });
            }

            db.Warehouses.AddRange(newDistrictWarehouses);
            await db.SaveChangesAsync(cancellationToken);

            foreach (var dist in newDistrictWarehouses)
            {
                newDistrictRoutes.Add(new WarehouseRoute
                {
                    FromWarehouseId = dist.Id,
                    ToWarehouseId = prov.Id,
                    RouteKind = "FirstMile",
                    DefaultHours = 4
                });
                newDistrictRoutes.Add(new WarehouseRoute
                {
                    FromWarehouseId = prov.Id,
                    ToWarehouseId = dist.Id,
                    RouteKind = "LastMile",
                    DefaultHours = 4
                });
            }

            db.WarehouseRoutes.AddRange(newDistrictRoutes);
            await db.SaveChangesAsync(cancellationToken);

            totalDistricts += newDistrictWarehouses.Count;
            logger.LogInformation("Seeded districts for {ProvinceName}: {Count}", p.ProvinceName, newDistrictWarehouses.Count);
        }

        logger.LogInformation(
            "Warehouse network seed complete. SOC={SocCount}, Provinces={ProvinceCount}, Districts={DistrictCount}",
            3,
            provinces.Count,
            totalDistricts);
    }

    private static IReadOnlyList<GhnProvince> FilterAndDeduplicateProvinces(IReadOnlyList<GhnProvince> provinces, ILogger logger)
    {
        if (provinces.Count == 0) return provinces;

        static bool IsTestProvince(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return true;
            return name.Contains("test", StringComparison.OrdinalIgnoreCase)
                || name.Contains("alert", StringComparison.OrdinalIgnoreCase);
        }

        static string NormalizeName(string name)
        {
            var n = (name ?? string.Empty).Trim();
            while (n.Contains("  ", StringComparison.Ordinal)) n = n.Replace("  ", " ");

            // GHN sometimes returns duplicate test-like entries such as "Hà Nội 02".
            // If a trailing token is a small number (e.g., 01/02), strip it for de-duping.
            var lastSpace = n.LastIndexOf(' ');
            if (lastSpace > 0)
            {
                var tail = n[(lastSpace + 1)..].Trim();
                if (tail.Length is 1 or 2 && tail.All(char.IsDigit))
                {
                    n = n[..lastSpace].Trim();
                }
            }

            return n;
        }

        static int Score(GhnProvince p)
        {
            // Higher score = preferred.
            var name = p.ProvinceName ?? string.Empty;
            var hasDigits = name.Any(char.IsDigit);
            return (hasDigits ? 0 : 100) + Math.Min(50, 50 - Math.Min(50, name.Length));
        }

        var kept = provinces
            .Where(p => !IsTestProvince(p.ProvinceName))
            .GroupBy(p => NormalizeName(p.ProvinceName ?? string.Empty), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(Score).First())
            .ToList();

        var removedCount = provinces.Count - kept.Count;
        if (removedCount > 0)
        {
            logger.LogInformation(
                "Filtered GHN provinces: input={InputCount}, kept={KeptCount} (removed={RemovedCount}).",
                provinces.Count,
                kept.Count,
                removedCount);
        }

        return kept;
    }

    private static string GuessRegionFromProvinceName(string? provinceName)
    {
        if (string.IsNullOrWhiteSpace(provinceName)) return RegionUnknown;
        var p = provinceName.Trim().ToLowerInvariant();

        // Simple heuristic (best-effort). It's OK if a few provinces go to Unknown.
        if (p.Contains("hà nội") || p.Contains("ha noi") || p.Contains("hải") || p.Contains("hai") || p.Contains("quảng ninh") || p.Contains("bac") || p.Contains("bắc") || p.Contains("lào") || p.Contains("lao") || p.Contains("thái") || p.Contains("thai") || p.Contains("ninh") || p.Contains("nam") || p.Contains("hưng") || p.Contains("hung") || p.Contains("phú") || p.Contains("phu") || p.Contains("tuyên") || p.Contains("tuyen"))
        {
            return RegionNorth;
        }

        if (p.Contains("đà nẵng") || p.Contains("da nang") || p.Contains("huế") || p.Contains("hue") || p.Contains("quảng") || p.Contains("quang") || p.Contains("bình") || p.Contains("binh") || p.Contains("khánh") || p.Contains("khanh") || p.Contains("kon") || p.Contains("gia lai") || p.Contains("đắk") || p.Contains("dak") || p.Contains("lâm") || p.Contains("lam dong"))
        {
            // Many central provinces include "Quảng"/"Bình"; this heuristic is intentionally broad.
            return RegionCentral;
        }

        if (p.Contains("hồ") || p.Contains("ho chi minh") || p.Contains("sài") || p.Contains("sai") || p.Contains("cần") || p.Contains("can tho") || p.Contains("đồng") || p.Contains("dong") || p.Contains("bình dương") || p.Contains("long an") || p.Contains("tiền") || p.Contains("tien") || p.Contains("an giang") || p.Contains("kiên") || p.Contains("kien") || p.Contains("cà") || p.Contains("ca mau"))
        {
            return RegionSouth;
        }

        return RegionUnknown;
    }
}
