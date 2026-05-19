using System.Globalization;
using System.Text;
using System.Text.Json;
using DOAN_BANHANG.Services;
using Microsoft.AspNetCore.Hosting;

namespace DOAN_BANHANG.Services;

public sealed class GhnStationCatalog
{
    private readonly IWebHostEnvironment _env;
    private readonly GhnClient _ghn;
    private readonly ILogger<GhnStationCatalog> _logger;

    private static readonly JsonSerializerOptions CacheJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private static readonly TimeSpan DefaultTtl = TimeSpan.FromDays(14);

    private readonly SemaphoreSlim _gate = new(1, 1);

    public GhnStationCatalog(IWebHostEnvironment env, GhnClient ghn, ILogger<GhnStationCatalog> logger)
    {
        _env = env;
        _ghn = ghn;
        _logger = logger;
    }

    private string CacheFilePath
        => Path.Combine(_env.ContentRootPath, "App_Data", "ghn", "station-catalog.json");

    private static bool IsSecurityCheckFailedException(Exception ex)
    {
        var msg = ex.Message ?? string.Empty;
        return msg.Contains("SECURITY_CHECK_FAILED", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("invalid number of segments", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<GhnStationCatalogSnapshot?> TryLoadSnapshotAsync(CancellationToken cancellationToken)
    {
        var path = CacheFilePath;
        if (!System.IO.File.Exists(path)) return null;

        try
        {
            await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var snapshot = await JsonSerializer.DeserializeAsync<GhnStationCatalogSnapshot>(fs, CacheJsonOptions, cancellationToken);
            if (snapshot is null) return null;

            // Basic sanity.
            if (snapshot.Stations is null) snapshot.Stations = new List<GhnStationRecord>();

            // Refresh computed fields.
            foreach (var s in snapshot.Stations)
            {
                s.IsHub = GuessIsHub(s.LocationName);
            }

            return snapshot;
        }
        catch
        {
            return null;
        }
    }

    private async Task SaveSnapshotAsync(GhnStationCatalogSnapshot snapshot, CancellationToken cancellationToken)
    {
        var path = CacheFilePath;
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);

        await using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        await JsonSerializer.SerializeAsync(fs, snapshot, CacheJsonOptions, cancellationToken);
    }

    private async Task MergeAndSaveAsync(IEnumerable<GhnStationRecord> records, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var snapshot = await TryLoadSnapshotAsync(cancellationToken) ?? new GhnStationCatalogSnapshot
            {
                CreatedAtUtc = DateTimeOffset.UtcNow,
                BaseUrl = _ghn.GetAuthDebugInfo().BaseUrl,
                ShopId = _ghn.GetAuthDebugInfo().ShopId,
                Stations = new List<GhnStationRecord>()
            };

            var byKey = snapshot.Stations
                .ToDictionary(x => (x.DistrictId, x.LocationId), x => x);

            foreach (var r in records)
            {
                byKey[(r.DistrictId, r.LocationId)] = r;
            }

            snapshot.CreatedAtUtc = DateTimeOffset.UtcNow;
            snapshot.Stations = byKey.Values.ToList();

            await SaveSnapshotAsync(snapshot, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<GhnStationLookupResult> GetStationsForDistrictAsync(int districtId, string? wardCode, CancellationToken cancellationToken)
    {
        if (districtId <= 0)
        {
            return new GhnStationLookupResult(Array.Empty<GhnStationRecord>(), FromCache: false, UsedCacheBecauseGhnRejectedToken: false, CacheAvailable: false, SkippedStationApi: false);
        }

        // If station token format is clearly unsupported by GHN security (e.g. non-JWT),
        // skip station/get to avoid repeated hard failures and fall back to cache/offline route.
        try
        {
            var auth = _ghn.GetAuthDebugInfo(forStationGet: true);
            if (!auth.LooksLikeJwt)
            {
                var snapshot = await TryLoadSnapshotAsync(cancellationToken);
                var cached = snapshot?.Stations
                    .Where(x => x.DistrictId == districtId)
                    .ToList() ?? new List<GhnStationRecord>();

                return new GhnStationLookupResult(cached, FromCache: snapshot is not null, UsedCacheBecauseGhnRejectedToken: false, CacheAvailable: snapshot is not null, SkippedStationApi: true);
            }
        }
        catch (InvalidOperationException ex) when ((ex.Message ?? string.Empty).Contains("GHN token is missing", StringComparison.OrdinalIgnoreCase))
        {
            var snapshot = await TryLoadSnapshotAsync(cancellationToken);
            var cached = snapshot?.Stations
                .Where(x => x.DistrictId == districtId)
                .ToList() ?? new List<GhnStationRecord>();

            return new GhnStationLookupResult(cached, FromCache: snapshot is not null, UsedCacheBecauseGhnRejectedToken: false, CacheAvailable: snapshot is not null, SkippedStationApi: true);
        }

        try
        {
            var live = await _ghn.GetStationsAsync(new GhnStationGetRequest
            {
                DistrictId = districtId,
                WardCode = string.IsNullOrWhiteSpace(wardCode) ? null : wardCode.Trim(),
                Offset = 0,
                Limit = 1000
            }, cancellationToken);

            var records = live
                .Select(s => GhnStationRecord.FromGhn(districtId, wardCode, s))
                .ToList();

            await MergeAndSaveAsync(records, cancellationToken);

            return new GhnStationLookupResult(records, FromCache: false, UsedCacheBecauseGhnRejectedToken: false, CacheAvailable: true, SkippedStationApi: false);
        }
        catch (Exception ex) when (IsSecurityCheckFailedException(ex))
        {
            var snapshot = await TryLoadSnapshotAsync(cancellationToken);
            var cached = snapshot?.Stations
                .Where(x => x.DistrictId == districtId)
                .ToList() ?? new List<GhnStationRecord>();

            return new GhnStationLookupResult(cached, FromCache: true, UsedCacheBecauseGhnRejectedToken: true, CacheAvailable: snapshot is not null, SkippedStationApi: false);
        }
        catch (InvalidOperationException ex) when ((ex.Message ?? string.Empty).Contains("GHN token is missing", StringComparison.OrdinalIgnoreCase))
        {
            var snapshot = await TryLoadSnapshotAsync(cancellationToken);
            var cached = snapshot?.Stations
                .Where(x => x.DistrictId == districtId)
                .ToList() ?? new List<GhnStationRecord>();

            // Token missing usually means wrong environment/profile loaded. Still allow using cached catalog if present.
            return new GhnStationLookupResult(cached, FromCache: snapshot is not null, UsedCacheBecauseGhnRejectedToken: false, CacheAvailable: snapshot is not null, SkippedStationApi: true);
        }
    }

    public async Task<GhnStationSyncResult> SyncAllStationsAsync(CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;

        var provinces = await _ghn.GetProvincesAsync(cancellationToken);
        var all = new Dictionary<(int DistrictId, int LocationId), GhnStationRecord>();

        foreach (var p in provinces)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var districts = await _ghn.GetDistrictsAsync(p.ProvinceId, cancellationToken);
            foreach (var d in districts)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var offset = 0;
                const int limit = 1000;

                while (true)
                {
                    var stations = await _ghn.GetStationsAsync(new GhnStationGetRequest
                    {
                        DistrictId = d.DistrictId,
                        WardCode = null,
                        Offset = offset,
                        Limit = limit
                    }, cancellationToken);

                    foreach (var s in stations)
                    {
                        var r = GhnStationRecord.FromGhn(d.DistrictId, null, s);
                        all[(r.DistrictId, r.LocationId)] = r;
                    }

                    if (stations.Count < limit) break;
                    offset += stations.Count;
                }
            }
        }

        var snapshot = new GhnStationCatalogSnapshot
        {
            CreatedAtUtc = DateTimeOffset.UtcNow,
            BaseUrl = _ghn.GetAuthDebugInfo().BaseUrl,
            ShopId = _ghn.GetAuthDebugInfo().ShopId,
            Stations = all.Values.ToList()
        };

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await SaveSnapshotAsync(snapshot, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }

        var duration = DateTimeOffset.UtcNow - started;
        return new GhnStationSyncResult(snapshot.Stations.Count, duration);
    }

    public static bool GuessIsHub(string? locationName)
    {
        var n = NormalizeForSearch(locationName);
        if (string.IsNullOrWhiteSpace(n)) return false;

        // Heuristics: GHN naming differs by region, so keep this broad.
        return n.Contains("HUB", StringComparison.Ordinal)
            || n.Contains("TRUNG TAM", StringComparison.Ordinal)
            || n.Contains("TRUNG CHUYEN", StringComparison.Ordinal)
            || n.Contains("SORTING", StringComparison.Ordinal)
            || n.Contains("KHO TONG", StringComparison.Ordinal)
            || n.Contains("KHO TRUNG CHUYEN", StringComparison.Ordinal);
    }

    private static string NormalizeForSearch(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        var s = input.Trim().ToUpperInvariant();

        var normalized = s.Normalize(NormalizationForm.FormD);
        var chars = normalized
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            .ToArray();

        var noMarks = new string(chars).Normalize(NormalizationForm.FormC);
        return noMarks.Replace('Đ', 'D').Replace('đ', 'D');
    }

    public static double? DistanceKm(double? lat1, double? lon1, double? lat2, double? lon2)
    {
        if (!lat1.HasValue || !lon1.HasValue || !lat2.HasValue || !lon2.HasValue) return null;

        static double ToRad(double deg) => Math.PI * deg / 180.0;

        var dLat = ToRad(lat2.Value - lat1.Value);
        var dLon = ToRad(lon2.Value - lon1.Value);

        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
            + Math.Cos(ToRad(lat1.Value)) * Math.Cos(ToRad(lat2.Value))
            * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        const double earthRadiusKm = 6371.0;
        return earthRadiusKm * c;
    }

    public async Task<(GhnStationCatalogSnapshot? Snapshot, bool IsFresh)> TryGetSnapshotAsync(CancellationToken cancellationToken)
    {
        var snapshot = await TryLoadSnapshotAsync(cancellationToken);
        if (snapshot is null) return (null, false);

        var age = DateTimeOffset.UtcNow - snapshot.CreatedAtUtc;
        return (snapshot, age <= DefaultTtl);
    }
}

public sealed class GhnStationCatalogSnapshot
{
    public DateTimeOffset CreatedAtUtc { get; set; }

    public string? BaseUrl { get; set; }

    public int ShopId { get; set; }

    public List<GhnStationRecord> Stations { get; set; } = new();
}

public sealed class GhnStationRecord
{
    public int DistrictId { get; set; }

    public string? WardCode { get; set; }

    public int LocationCode { get; set; }

    public int LocationId { get; set; }

    public string? LocationName { get; set; }

    public string? Address { get; set; }

    public double? Latitude { get; set; }

    public double? Longitude { get; set; }

    public string? WardName { get; set; }

    public string? DistrictName { get; set; }

    public string? ProvinceName { get; set; }

    public bool IsHub { get; set; }

    public static GhnStationRecord FromGhn(int districtId, string? wardCode, GhnStation s)
    {
        return new GhnStationRecord
        {
            DistrictId = districtId,
            WardCode = string.IsNullOrWhiteSpace(wardCode) ? null : wardCode.Trim(),
            LocationCode = s.LocationCode,
            LocationId = s.LocationId,
            LocationName = s.LocationName,
            Address = s.Address,
            Latitude = s.Latitude,
            Longitude = s.Longitude,
            WardName = s.WardName,
            DistrictName = s.DistrictName,
            ProvinceName = s.ProvinceName,
            IsHub = GhnStationCatalog.GuessIsHub(s.LocationName)
        };
    }
}

public sealed record GhnStationLookupResult(
    IReadOnlyList<GhnStationRecord> Stations,
    bool FromCache,
    bool UsedCacheBecauseGhnRejectedToken,
    bool CacheAvailable,
    bool SkippedStationApi = false);

public sealed record GhnStationSyncResult(int StationCount, TimeSpan Duration);
