using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace DOAN_BANHANG.Services;

public sealed class GhnClient
{
    private readonly HttpClient _http;
    private readonly GhnOptions _options;

    public GhnClient(HttpClient http, IOptions<GhnOptions> options)
    {
        _http = http;
        _options = options.Value;
    }

    private string GetNormalizedTokenOrThrow()
    {
        var token = (_options.Token ?? string.Empty).Trim();
        if (token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            token = token[7..].Trim();
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("GHN token is missing. Configure Ghn:Token in appsettings or user-secrets.");
        }

        return token;
    }

    public async Task<IReadOnlyList<GhnProvince>> GetProvincesAsync(CancellationToken cancellationToken)
    {
        var res = await SendAsync<GhnListResponse<GhnProvince>>("master-data/province", cancellationToken);
        return res.Data ?? new List<GhnProvince>();
    }

    public async Task<IReadOnlyList<GhnDistrict>> GetDistrictsAsync(int provinceId, CancellationToken cancellationToken)
    {
        var res = await SendAsync<GhnListResponse<GhnDistrict>>($"master-data/district?province_id={provinceId}", cancellationToken);
        return res.Data ?? new List<GhnDistrict>();
    }

    public async Task<IReadOnlyList<GhnWard>> GetWardsAsync(int districtId, CancellationToken cancellationToken)
    {
        var res = await SendAsync<GhnListResponse<GhnWard>>($"master-data/ward?district_id={districtId}", cancellationToken);
        return res.Data ?? new List<GhnWard>();
    }

    public async Task<GhnFeeData> CalculateFeeAsync(GhnCalculateFeeRequest reqBody, CancellationToken cancellationToken)
        => await CalculateFeeAsync(reqBody, shopId: null, cancellationToken);

    public async Task<GhnFeeData> CalculateFeeAsync(GhnCalculateFeeRequest reqBody, int? shopId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.Token))
        {
            throw new InvalidOperationException("GHN token is missing. Configure Ghn:Token in appsettings or user-secrets.");
        }

        var resolvedShopId = shopId.GetValueOrDefault(_options.ShopId);
        if (resolvedShopId <= 0)
        {
            throw new InvalidOperationException("GHN ShopId is missing. Configure Ghn:ShopId in appsettings or user-secrets.");
        }

        var json = JsonSerializer.Serialize(reqBody, JsonOptions);
        using var req = new HttpRequestMessage(HttpMethod.Post, "v2/shipping-order/fee");
        req.Headers.TryAddWithoutValidation("Token", _options.Token);
        req.Headers.TryAddWithoutValidation("ShopId", resolvedShopId.ToString());
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        req.Content = new StringContent(json, Encoding.UTF8, "application/json");

        using var resp = await _http.SendAsync(req, cancellationToken);
        var body = await resp.Content.ReadAsStringAsync(cancellationToken);

        if (!resp.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"GHN fee request failed ({(int)resp.StatusCode}): {body}");
        }

        var parsed = JsonSerializer.Deserialize<GhnFeeResponse>(body, JsonOptions);
        if (parsed is null)
        {
            throw new InvalidOperationException("Unable to parse GHN fee response.");
        }

        if (parsed.Code != 200 || parsed.Data is null)
        {
            throw new HttpRequestException($"GHN fee response error (code={parsed.Code}): {parsed.Message}");
        }

        return parsed.Data;
    }

    public async Task<GhnCreateOrderData> CreateShippingOrderAsync(GhnCreateOrderRequest reqBody, CancellationToken cancellationToken)
        => await CreateShippingOrderAsync(reqBody, shopId: null, cancellationToken);

    public async Task<GhnCreateOrderData> CreateShippingOrderAsync(GhnCreateOrderRequest reqBody, int? shopId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.Token))
        {
            throw new InvalidOperationException("GHN token is missing. Configure Ghn:Token in appsettings or user-secrets.");
        }

        var resolvedShopId = shopId.GetValueOrDefault(_options.ShopId);
        if (resolvedShopId <= 0)
        {
            throw new InvalidOperationException("GHN ShopId is missing. Configure Ghn:ShopId in appsettings or user-secrets.");
        }

        var json = JsonSerializer.Serialize(reqBody, JsonOptions);
        using var req = new HttpRequestMessage(HttpMethod.Post, "v2/shipping-order/create");
        req.Headers.TryAddWithoutValidation("Token", _options.Token);
        req.Headers.TryAddWithoutValidation("ShopId", resolvedShopId.ToString());
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        req.Content = new StringContent(json, Encoding.UTF8, "application/json");

        using var resp = await _http.SendAsync(req, cancellationToken);
        var body = await resp.Content.ReadAsStringAsync(cancellationToken);

        if (!resp.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"GHN create-order request failed ({(int)resp.StatusCode}): {body}");
        }

        var parsed = JsonSerializer.Deserialize<GhnCreateOrderResponse>(body, JsonOptions);
        if (parsed is null)
        {
            throw new InvalidOperationException("Unable to parse GHN create-order response.");
        }

        if (parsed.Code != 200 || parsed.Data is null)
        {
            throw new HttpRequestException($"GHN create-order response error (code={parsed.Code}): {parsed.Message}");
        }

        return parsed.Data;
    }

    public async Task<string> GenerateA5TokenAsync(IEnumerable<string> orderCodes, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.Token))
        {
            throw new InvalidOperationException("GHN token is missing. Configure Ghn:Token in appsettings or user-secrets.");
        }

        var codes = orderCodes
            .Select(x => (x ?? string.Empty).Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (codes.Length == 0)
        {
            throw new InvalidOperationException("No GHN order_codes provided.");
        }

        var json = JsonSerializer.Serialize(new GhnA5GenTokenRequest { OrderCodes = codes }, JsonOptions);
        using var req = new HttpRequestMessage(HttpMethod.Post, "v2/a5/gen-token");
        req.Headers.TryAddWithoutValidation("Token", _options.Token);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        req.Content = new StringContent(json, Encoding.UTF8, "application/json");

        using var resp = await _http.SendAsync(req, cancellationToken);
        var body = await resp.Content.ReadAsStringAsync(cancellationToken);

        if (!resp.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"GHN a5 gen-token request failed ({(int)resp.StatusCode}): {body}");
        }

        var parsed = JsonSerializer.Deserialize<GhnA5GenTokenResponse>(body, JsonOptions);
        if (parsed is null)
        {
            throw new InvalidOperationException("Unable to parse GHN a5 gen-token response.");
        }

        if (parsed.Code != 200 || parsed.Data is null || string.IsNullOrWhiteSpace(parsed.Data.Token))
        {
            throw new HttpRequestException($"GHN a5 gen-token response error (code={parsed.Code}): {parsed.Message}");
        }

        return parsed.Data.Token;
    }

    public async Task<IReadOnlyList<GhnPickupShift>> GetPickupShiftsAsync(CancellationToken cancellationToken)
    {
        var token = GetNormalizedTokenOrThrow();

        using var req = new HttpRequestMessage(HttpMethod.Get, "v2/shift/date");
        req.Headers.TryAddWithoutValidation("Token", token);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var resp = await _http.SendAsync(req, cancellationToken);
        var body = await resp.Content.ReadAsStringAsync(cancellationToken);

        if (!resp.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"GHN shift/date request failed ({(int)resp.StatusCode}): {body}");
        }

        var parsed = JsonSerializer.Deserialize<GhnListResponse<GhnPickupShift>>(body, JsonOptions);
        if (parsed is null)
        {
            throw new InvalidOperationException("Unable to parse GHN shift/date response.");
        }

        if (parsed.Code != 200 || parsed.Data is null)
        {
            throw new HttpRequestException($"GHN shift/date response error (code={parsed.Code}): {parsed.Message}");
        }

        return parsed.Data;
    }

    /// <summary>
    /// Get list of GHN stations (bưu cục) via docs id=96.
    /// Endpoint: POST v2/station/get
    /// </summary>
    public async Task<IReadOnlyList<GhnStation>> GetStationsAsync(GhnStationGetRequest reqBody, CancellationToken cancellationToken)
    {
        var token = GetNormalizedTokenOrThrow();

        if (reqBody is null) throw new ArgumentNullException(nameof(reqBody));
        if (reqBody.DistrictId <= 0) throw new InvalidOperationException("district_id is required.");

        if (reqBody.Limit <= 0) reqBody.Limit = 1000;
        if (reqBody.Limit > 1000) reqBody.Limit = 1000;
        if (reqBody.Offset < 0) reqBody.Offset = 0;

        var json = JsonSerializer.Serialize(reqBody, JsonOptions);
        using var req = new HttpRequestMessage(HttpMethod.Post, "v2/station/get");
        req.Headers.TryAddWithoutValidation("Token", token);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        req.Content = new StringContent(json, Encoding.UTF8, "application/json");

        using var resp = await _http.SendAsync(req, cancellationToken);
        var body = await resp.Content.ReadAsStringAsync(cancellationToken);

        if (!resp.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"GHN station/get request failed ({(int)resp.StatusCode}): {body}");
        }

        var parsed = JsonSerializer.Deserialize<GhnListResponse<GhnStation>>(body, JsonOptions);
        if (parsed is null)
        {
            throw new InvalidOperationException("Unable to parse GHN station/get response.");
        }

        if (parsed.Code != 200)
        {
            throw new HttpRequestException($"GHN station/get response error (code={parsed.Code}): {parsed.Message}");
        }

        return parsed.Data ?? new List<GhnStation>();
    }

    public async Task<IReadOnlyList<GhnAvailableService>> GetAvailableServicesAsync(int fromDistrictId, int toDistrictId, CancellationToken cancellationToken)
        => await GetAvailableServicesAsync(fromDistrictId, toDistrictId, shopId: null, cancellationToken);

    public async Task<IReadOnlyList<GhnAvailableService>> GetAvailableServicesAsync(int fromDistrictId, int toDistrictId, int? shopId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.Token))
        {
            throw new InvalidOperationException("GHN token is missing. Configure Ghn:Token in appsettings or user-secrets.");
        }

        var resolvedShopId = shopId.GetValueOrDefault(_options.ShopId);
        if (resolvedShopId <= 0)
        {
            throw new InvalidOperationException("GHN ShopId is missing. Configure Ghn:ShopId in appsettings or user-secrets.");
        }

        var json = JsonSerializer.Serialize(new GhnAvailableServicesRequest
        {
            ShopId = resolvedShopId,
            FromDistrict = fromDistrictId,
            ToDistrict = toDistrictId
        }, JsonOptions);

        using var req = new HttpRequestMessage(HttpMethod.Post, "v2/shipping-order/available-services");
        req.Headers.TryAddWithoutValidation("Token", _options.Token);
        req.Headers.TryAddWithoutValidation("ShopId", resolvedShopId.ToString());
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        req.Content = new StringContent(json, Encoding.UTF8, "application/json");

        using var resp = await _http.SendAsync(req, cancellationToken);
        var body = await resp.Content.ReadAsStringAsync(cancellationToken);

        if (!resp.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"GHN available-services request failed ({(int)resp.StatusCode}): {body}");
        }

        var parsed = JsonSerializer.Deserialize<GhnListResponse<GhnAvailableService>>(body, JsonOptions);
        if (parsed is null)
        {
            throw new InvalidOperationException("Unable to parse GHN available-services response.");
        }

        if (parsed.Code != 200 || parsed.Data is null)
        {
            throw new HttpRequestException($"GHN available-services response error (code={parsed.Code}): {parsed.Message}");
        }

        return parsed.Data;
    }

    public async Task<GhnLeadTimeData> GetLeadTimeAsync(GhnLeadTimeRequest reqBody, CancellationToken cancellationToken)
        => await GetLeadTimeAsync(reqBody, shopId: null, cancellationToken);

    public async Task<GhnLeadTimeData> GetLeadTimeAsync(GhnLeadTimeRequest reqBody, int? shopId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.Token))
        {
            throw new InvalidOperationException("GHN token is missing. Configure Ghn:Token in appsettings or user-secrets.");
        }

        var resolvedShopId = shopId.GetValueOrDefault(_options.ShopId);
        if (resolvedShopId <= 0)
        {
            throw new InvalidOperationException("GHN ShopId is missing. Configure Ghn:ShopId in appsettings or user-secrets.");
        }

        var json = JsonSerializer.Serialize(reqBody, JsonOptions);
        using var req = new HttpRequestMessage(HttpMethod.Post, "v2/shipping-order/leadtime");
        req.Headers.TryAddWithoutValidation("Token", _options.Token);
        req.Headers.TryAddWithoutValidation("ShopId", resolvedShopId.ToString());
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        req.Content = new StringContent(json, Encoding.UTF8, "application/json");

        using var resp = await _http.SendAsync(req, cancellationToken);
        var body = await resp.Content.ReadAsStringAsync(cancellationToken);

        if (!resp.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"GHN leadtime request failed ({(int)resp.StatusCode}): {body}");
        }

        var parsed = JsonSerializer.Deserialize<GhnLeadTimeResponse>(body, JsonOptions);
        if (parsed is null)
        {
            throw new InvalidOperationException("Unable to parse GHN leadtime response.");
        }

        if (parsed.Code != 200 || parsed.Data is null)
        {
            throw new HttpRequestException($"GHN leadtime response error (code={parsed.Code}): {parsed.Message}");
        }

        return parsed.Data;
    }

    /// <summary>
    /// Get GHN shipping order detail by GHN order_code (aka tracking code in this app).
    /// Endpoint (commonly): POST v2/shipping-order/detail
    /// </summary>
    public async Task<GhnShippingOrderDetail> GetShippingOrderDetailAsync(string orderCode, CancellationToken cancellationToken)
        => await GetShippingOrderDetailAsync(orderCode, shopId: null, cancellationToken);

    public async Task<GhnShippingOrderDetail> GetShippingOrderDetailAsync(string orderCode, int? shopId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.Token))
        {
            throw new InvalidOperationException("GHN token is missing. Configure Ghn:Token in appsettings or user-secrets.");
        }

        var code = (orderCode ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new InvalidOperationException("order_code is required.");
        }

        // Some GHN endpoints require ShopId; we provide it when available.
        var resolvedShopId = shopId.GetValueOrDefault(_options.ShopId);

        var json = JsonSerializer.Serialize(new GhnShippingOrderDetailRequest { OrderCode = code }, JsonOptions);
        using var req = new HttpRequestMessage(HttpMethod.Post, "v2/shipping-order/detail");
        req.Headers.TryAddWithoutValidation("Token", _options.Token);
        if (resolvedShopId > 0)
        {
            req.Headers.TryAddWithoutValidation("ShopId", resolvedShopId.ToString());
        }
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        req.Content = new StringContent(json, Encoding.UTF8, "application/json");

        using var resp = await _http.SendAsync(req, cancellationToken);
        var body = await resp.Content.ReadAsStringAsync(cancellationToken);

        if (!resp.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"GHN shipping-order/detail request failed ({(int)resp.StatusCode}): {body}");
        }

        var parsed = JsonSerializer.Deserialize<GhnShippingOrderDetailResponse>(body, JsonOptions);
        if (parsed is null)
        {
            throw new InvalidOperationException("Unable to parse GHN shipping-order/detail response.");
        }

        if (parsed.Code != 200)
        {
            throw new HttpRequestException($"GHN shipping-order/detail response error (code={parsed.Code}): {parsed.Message}");
        }

        if (parsed.Data.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return new GhnShippingOrderDetail { OrderCode = code };
        }

        return GhnShippingOrderDetail.FromJson(parsed.Data, fallbackOrderCode: code);
    }

    public async Task<IReadOnlyList<GhnCancelOrderResult>> CancelShippingOrdersAsync(IEnumerable<string> orderCodes, CancellationToken cancellationToken)
        => await CancelShippingOrdersAsync(orderCodes, shopId: null, cancellationToken);

    /// <summary>
    /// Cancel GHN shipping orders via docs id=102.
    /// Endpoint: POST v2/switch-status/cancel
    /// </summary>
    public async Task<IReadOnlyList<GhnCancelOrderResult>> CancelShippingOrdersAsync(IEnumerable<string> orderCodes, int? shopId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.Token))
        {
            throw new InvalidOperationException("GHN token is missing. Configure Ghn:Token in appsettings or user-secrets.");
        }

        var resolvedShopId = shopId.GetValueOrDefault(_options.ShopId);
        if (resolvedShopId <= 0)
        {
            throw new InvalidOperationException("GHN ShopId is missing. Configure Ghn:ShopId in appsettings or user-secrets.");
        }

        var codes = orderCodes
            .Select(x => (x ?? string.Empty).Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (codes.Length == 0)
        {
            throw new InvalidOperationException("No GHN order_codes provided.");
        }

        var json = JsonSerializer.Serialize(new GhnCancelOrderRequest { OrderCodes = codes }, JsonOptions);
        using var req = new HttpRequestMessage(HttpMethod.Post, "v2/switch-status/cancel");
        req.Headers.TryAddWithoutValidation("Token", _options.Token);
        req.Headers.TryAddWithoutValidation("ShopId", resolvedShopId.ToString());
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        req.Content = new StringContent(json, Encoding.UTF8, "application/json");

        using var resp = await _http.SendAsync(req, cancellationToken);
        var body = await resp.Content.ReadAsStringAsync(cancellationToken);

        if (!resp.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"GHN cancel request failed ({(int)resp.StatusCode}): {body}");
        }

        var parsed = JsonSerializer.Deserialize<GhnCancelOrderResponse>(body, JsonOptions);
        if (parsed is null)
        {
            throw new InvalidOperationException("Unable to parse GHN cancel response.");
        }

        if (parsed.Code != 200)
        {
            throw new HttpRequestException($"GHN cancel response error (code={parsed.Code}): {parsed.Message}");
        }

        return parsed.Data ?? new List<GhnCancelOrderResult>();
    }

    private async Task<T> SendAsync<T>(string relativeUrl, CancellationToken cancellationToken)
    {
        var token = GetNormalizedTokenOrThrow();

        using var req = new HttpRequestMessage(HttpMethod.Get, relativeUrl);
        req.Headers.TryAddWithoutValidation("Token", token);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var resp = await _http.SendAsync(req, cancellationToken);
        var body = await resp.Content.ReadAsStringAsync(cancellationToken);

        if (!resp.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"GHN request failed ({(int)resp.StatusCode}): {body}");
        }

        var parsed = JsonSerializer.Deserialize<T>(body, JsonOptions);
        if (parsed is null)
        {
            throw new InvalidOperationException("Unable to parse GHN response.");
        }

        return parsed;
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };
}

public sealed class GhnShippingOrderDetailRequest
{
    [JsonPropertyName("order_code")]
    public string OrderCode { get; set; } = string.Empty;
}

public sealed class GhnShippingOrderDetailResponse
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("data")]
    public JsonElement Data { get; set; }
}

public sealed class GhnShippingOrderDetail
{
    public string OrderCode { get; set; } = string.Empty;

    public string? Status { get; set; }

    public string? StatusName { get; set; }

    public List<GhnShippingOrderLogItem> Logs { get; set; } = new();

    internal static GhnShippingOrderDetail FromJson(JsonElement data, string fallbackOrderCode)
    {
        var detail = new GhnShippingOrderDetail
        {
            OrderCode = GetString(data, "order_code") ?? fallbackOrderCode,
            Status = GetString(data, "status"),
            StatusName = GetString(data, "status_name")
        };

        // GHN responses are not fully consistent across environments/versions.
        // We try multiple keys and also handle nested objects that contain an array.
        var logArrays = new[]
        {
            "log",
            "logs",
            "history",
            "histories",
            "tracking_log",
            "tracking_logs",
            "order_log",
            "order_logs",
            "journey",
            "route",
            "routes"
        };

        foreach (var key in logArrays)
        {
            if (!TryGetProperty(data, key, out var el)) continue;

            if (TryExtractLogArray(el, out var arr))
            {
                detail.Logs.AddRange(ParseLogArray(arr));
            }
        }

        // De-dup (best-effort) while keeping order.
        if (detail.Logs.Count > 1)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            detail.Logs = detail.Logs
                .Where(x => seen.Add($"{x.TimeRaw}|{x.Location}|{x.StatusName}|{x.Message}"))
                .ToList();
        }

        return detail;
    }

    private static bool TryExtractLogArray(JsonElement el, out JsonElement arr)
    {
        arr = default;

        if (el.ValueKind == JsonValueKind.Array)
        {
            arr = el;
            return true;
        }

        if (el.ValueKind != JsonValueKind.Object) return false;

        // common nesting patterns: { logs: [...] } or { data: [...] }
        var nestedKeys = new[] { "logs", "log", "data", "items", "history", "tracking_logs" };
        foreach (var key in nestedKeys)
        {
            if (!TryGetProperty(el, key, out var v)) continue;
            if (v.ValueKind == JsonValueKind.Array)
            {
                arr = v;
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<GhnShippingOrderLogItem> ParseLogArray(JsonElement arr)
    {
        foreach (var item in arr.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;

            var statusName = GetString(item, "status_name") ?? GetString(item, "status") ?? GetString(item, "action") ?? GetString(item, "title");
            var location = GetString(item, "warehouse")
                           ?? GetString(item, "location")
                           ?? GetString(item, "location_name")
                           ?? GetString(item, "current_location")
                           ?? GetString(item, "hub")
                           ?? GetString(item, "office")
                           ?? GetString(item, "station")
                           ?? GetString(item, "station_name");

            var message = GetString(item, "message")
                          ?? GetString(item, "description")
                          ?? GetString(item, "note");

            var timeRaw = GetString(item, "updated_date")
                          ?? GetString(item, "time")
                          ?? GetString(item, "created_date")
                          ?? GetString(item, "action_at");

            yield return new GhnShippingOrderLogItem
            {
                StatusName = statusName,
                Location = location,
                Message = message,
                TimeRaw = timeRaw
            };
        }
    }

    private static bool TryGetProperty(JsonElement obj, string name, out JsonElement value)
    {
        value = default;
        if (obj.ValueKind != JsonValueKind.Object) return false;
        return obj.TryGetProperty(name, out value);
    }

    private static string? GetString(JsonElement obj, string name)
    {
        if (!TryGetProperty(obj, name, out var v)) return null;

        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Number => v.TryGetInt64(out var n) ? n.ToString() : v.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => v.ToString()
        };
    }
}

public sealed class GhnShippingOrderLogItem
{
    public string? StatusName { get; set; }

    public string? Location { get; set; }

    public string? Message { get; set; }

    public string? TimeRaw { get; set; }
}

public sealed class GhnPickupShift
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("from_time")]
    public int FromTime { get; set; }

    [JsonPropertyName("to_time")]
    public int ToTime { get; set; }
}

public sealed class GhnAvailableServicesRequest
{
    [JsonPropertyName("shop_id")]
    public int ShopId { get; set; }

    [JsonPropertyName("from_district")]
    public int FromDistrict { get; set; }

    [JsonPropertyName("to_district")]
    public int ToDistrict { get; set; }
}

public sealed class GhnStationGetRequest
{
    [JsonPropertyName("district_id")]
    public int DistrictId { get; set; }

    [JsonPropertyName("ward_code")]
    public string? WardCode { get; set; }

    [JsonPropertyName("offset")]
    public int Offset { get; set; } = 0;

    [JsonPropertyName("limit")]
    public int Limit { get; set; } = 1000;
}

public sealed class GhnStation
{
    [JsonPropertyName("address")]
    public string? Address { get; set; }

    [JsonPropertyName("locationCode")]
    public int LocationCode { get; set; }

    [JsonPropertyName("locationId")]
    public int LocationId { get; set; }

    [JsonPropertyName("locationName")]
    public string? LocationName { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("latitude")]
    public double? Latitude { get; set; }

    [JsonPropertyName("longitude")]
    public double? Longitude { get; set; }

    [JsonPropertyName("wardName")]
    public string? WardName { get; set; }

    [JsonPropertyName("districtName")]
    public string? DistrictName { get; set; }

    [JsonPropertyName("provinceName")]
    public string? ProvinceName { get; set; }

    [JsonPropertyName("iframeMap")]
    public string? IframeMap { get; set; }
}

public sealed class GhnAvailableService
{
    [JsonPropertyName("service_id")]
    public int ServiceId { get; set; }

    [JsonPropertyName("service_type_id")]
    public int ServiceTypeId { get; set; }

    [JsonPropertyName("short_name")]
    public string? ShortName { get; set; }
}

public sealed class GhnLeadTimeRequest
{
    [JsonPropertyName("from_district_id")]
    public int FromDistrictId { get; set; }

    [JsonPropertyName("from_ward_code")]
    public string FromWardCode { get; set; } = string.Empty;

    [JsonPropertyName("to_district_id")]
    public int ToDistrictId { get; set; }

    [JsonPropertyName("to_ward_code")]
    public string ToWardCode { get; set; } = string.Empty;

    [JsonPropertyName("service_id")]
    public int ServiceId { get; set; }
}

public sealed class GhnLeadTimeResponse
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("data")]
    public GhnLeadTimeData? Data { get; set; }
}

public sealed class GhnLeadTimeData
{
    [JsonPropertyName("leadtime")]
    public long Leadtime { get; set; }

    [JsonPropertyName("order_date")]
    public long OrderDate { get; set; }
}

public sealed class GhnCancelOrderRequest
{
    [JsonPropertyName("order_codes")]
    public string[] OrderCodes { get; set; } = Array.Empty<string>();
}

public sealed class GhnCancelOrderResponse
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("data")]
    public List<GhnCancelOrderResult>? Data { get; set; }
}

public sealed class GhnCancelOrderResult
{
    [JsonPropertyName("order_code")]
    public string OrderCode { get; set; } = string.Empty;

    [JsonPropertyName("result")]
    public bool Result { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

public sealed class GhnCreateOrderRequest
{
    [JsonPropertyName("payment_type_id")]
    public int PaymentTypeId { get; set; } = 2; // 2 = shop pays shipping fee (common default)

    [JsonPropertyName("note")]
    public string? Note { get; set; }

    [JsonPropertyName("required_note")]
    public string? RequiredNote { get; set; }

    [JsonPropertyName("from_name")]
    public string FromName { get; set; } = string.Empty;

    [JsonPropertyName("from_phone")]
    public string FromPhone { get; set; } = string.Empty;

    [JsonPropertyName("from_address")]
    public string FromAddress { get; set; } = string.Empty;

    [JsonPropertyName("from_ward_code")]
    public string FromWardCode { get; set; } = string.Empty;

    [JsonPropertyName("from_district_id")]
    public int FromDistrictId { get; set; }

    [JsonPropertyName("to_name")]
    public string ToName { get; set; } = string.Empty;

    [JsonPropertyName("to_phone")]
    public string ToPhone { get; set; } = string.Empty;

    [JsonPropertyName("to_address")]
    public string ToAddress { get; set; } = string.Empty;

    [JsonPropertyName("to_ward_code")]
    public string ToWardCode { get; set; } = string.Empty;

    [JsonPropertyName("to_district_id")]
    public int ToDistrictId { get; set; }

    [JsonPropertyName("cod_amount")]
    public int CodAmount { get; set; }

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    [JsonPropertyName("weight")]
    public int Weight { get; set; }

    [JsonPropertyName("length")]
    public int Length { get; set; }

    [JsonPropertyName("width")]
    public int Width { get; set; }

    [JsonPropertyName("height")]
    public int Height { get; set; }

    [JsonPropertyName("insurance_value")]
    public int InsuranceValue { get; set; }

    [JsonPropertyName("service_type_id")]
    public int ServiceTypeId { get; set; }

    [JsonPropertyName("items")]
    public List<GhnCreateOrderItem> Items { get; set; } = new();
}

public sealed class GhnCreateOrderItem
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("quantity")]
    public int Quantity { get; set; }

    [JsonPropertyName("price")]
    public int Price { get; set; }
}

public sealed class GhnCreateOrderResponse
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("data")]
    public GhnCreateOrderData? Data { get; set; }
}

public sealed class GhnCreateOrderData
{
    [JsonPropertyName("order_code")]
    public string OrderCode { get; set; } = string.Empty;
}

public sealed class GhnA5GenTokenRequest
{
    [JsonPropertyName("order_codes")]
    public string[] OrderCodes { get; set; } = Array.Empty<string>();
}

public sealed class GhnA5GenTokenResponse
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("data")]
    public GhnA5GenTokenData? Data { get; set; }
}

public sealed class GhnA5GenTokenData
{
    [JsonPropertyName("token")]
    public string Token { get; set; } = string.Empty;
}

public sealed class GhnCalculateFeeRequest
{
    [JsonPropertyName("service_type_id")]
    public int ServiceTypeId { get; set; }

    [JsonPropertyName("from_district_id")]
    public int FromDistrictId { get; set; }

    [JsonPropertyName("from_ward_code")]
    public string FromWardCode { get; set; } = string.Empty;

    [JsonPropertyName("to_district_id")]
    public int ToDistrictId { get; set; }

    [JsonPropertyName("to_ward_code")]
    public string ToWardCode { get; set; } = string.Empty;

    [JsonPropertyName("weight")]
    public int Weight { get; set; }

    [JsonPropertyName("length")]
    public int? Length { get; set; }

    [JsonPropertyName("width")]
    public int? Width { get; set; }

    [JsonPropertyName("height")]
    public int? Height { get; set; }

    [JsonPropertyName("insurance_value")]
    public int InsuranceValue { get; set; }
}

public sealed class GhnFeeResponse
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("data")]
    public GhnFeeData? Data { get; set; }
}

public sealed class GhnFeeData
{
    [JsonPropertyName("total")]
    public int Total { get; set; }

    [JsonPropertyName("service_fee")]
    public int ServiceFee { get; set; }

    [JsonPropertyName("insurance_fee")]
    public int InsuranceFee { get; set; }
}

public sealed class GhnListResponse<T>
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("data")]
    public List<T>? Data { get; set; }
}

public sealed class GhnProvince
{
    [JsonPropertyName("ProvinceID")]
    public int ProvinceId { get; set; }

    [JsonPropertyName("ProvinceName")]
    public string ProvinceName { get; set; } = string.Empty;
}

public sealed class GhnDistrict
{
    [JsonPropertyName("DistrictID")]
    public int DistrictId { get; set; }

    [JsonPropertyName("DistrictName")]
    public string DistrictName { get; set; } = string.Empty;
}

public sealed class GhnWard
{
    [JsonPropertyName("WardCode")]
    public string WardCode { get; set; } = string.Empty;

    [JsonPropertyName("WardName")]
    public string WardName { get; set; } = string.Empty;
}
