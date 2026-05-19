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

    // GHN docs and many samples use header name "token".
    // HTTP header names are case-insensitive, but we keep it identical to samples.
    private const string TokenHeaderName = "token";

    public GhnClient(HttpClient http, IOptions<GhnOptions> options)
    {
        _http = http;
        _options = options.Value;
    }

    private string GetNormalizedTokenOrThrow(string? tokenOverride = null)
    {
        // Only use override token when it is actually provided.
        // Important: configuration often binds missing strings as "" (empty),
        // and we must not treat an empty StationToken as overriding the main Token.
        var effective = !string.IsNullOrWhiteSpace(tokenOverride)
            ? tokenOverride
            : (_options.Token ?? string.Empty);

        var token = effective.Trim();
        if (token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            token = token[7..].Trim();
        }

        // Defend against copy/paste mistakes (embedded newline/space).
        token = string.Concat(token.Where(c => !char.IsWhiteSpace(c)));

        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("GHN token is missing. Configure Ghn:Token in appsettings or user-secrets.");
        }

        return token;
    }

    private static bool LooksLikeJwt(string token)
        => token.Count(c => c == '.') == 2;

    private string GetEffectiveBaseUrlForStationGet()
        => string.IsNullOrWhiteSpace(_options.StationBaseUrl) ? _options.BaseUrl : _options.StationBaseUrl;

    public GhnAuthDebugInfo GetAuthDebugInfo(bool forStationGet = false)
    {
        var tokenOverride = forStationGet && !string.IsNullOrWhiteSpace(_options.StationToken)
            ? _options.StationToken
            : null;
        var token = GetNormalizedTokenOrThrow(tokenOverride);
        var dots = token.Count(c => c == '.');

        var effectiveBaseUrl = forStationGet ? GetEffectiveBaseUrlForStationGet() : _options.BaseUrl;

        string masked;
        if (token.Length <= 8)
        {
            masked = new string('*', token.Length);
        }
        else
        {
            masked = token[..3] + "…" + token[^3..];
        }

        return new GhnAuthDebugInfo(
            BaseUrl: effectiveBaseUrl,
            ShopId: _options.ShopId,
            TokenLength: token.Length,
            DotCount: dots,
            LooksLikeJwt: LooksLikeJwt(token),
            TokenMasked: masked,
            TokenSource: forStationGet && !string.IsNullOrWhiteSpace(_options.StationToken) ? "StationToken" : "Token");
    }

    private void ApplyAuthHeaders(
        HttpRequestMessage req,
        int? shopId = null,
        string? tokenOverride = null,
        bool forceAuthorizationHeader = false,
        bool suppressAuthorizationHeader = false)
    {
        var token = GetNormalizedTokenOrThrow(tokenOverride);

        req.Headers.TryAddWithoutValidation(TokenHeaderName, token);

        // Some gateways/auth layers expect JWT tokens in Authorization header.
        // We only add this when it actually looks like a JWT to avoid confusing UUID-like tokens.
        if (!suppressAuthorizationHeader && (forceAuthorizationHeader || LooksLikeJwt(token)) && req.Headers.Authorization is null)
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        var resolvedShopId = shopId.GetValueOrDefault(_options.ShopId);
        if (resolvedShopId > 0)
        {
            req.Headers.TryAddWithoutValidation("ShopId", resolvedShopId.ToString());
        }
    }

    private static bool IsSecurityCheckFailed(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return false;
        return body.Contains("SECURITY_CHECK_FAILED", StringComparison.OrdinalIgnoreCase)
            || body.Contains("invalid number of segments", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAuthorizationRequired(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return false;
        return body.Contains("Authorization header is required", StringComparison.OrdinalIgnoreCase);
    }

    private static string? GetAlternativeBaseUrl(string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl)) return null;

        // Common GHN environment hosts.
        // dev: dev-online-gateway.ghn.vn
        // prod: online-gateway.ghn.vn
        if (baseUrl.Contains("dev-online-gateway.ghn.vn", StringComparison.OrdinalIgnoreCase))
        {
            return baseUrl.Replace("dev-online-gateway.ghn.vn", "online-gateway.ghn.vn", StringComparison.OrdinalIgnoreCase);
        }

        if (baseUrl.Contains("online-gateway.ghn.vn", StringComparison.OrdinalIgnoreCase)
            && !baseUrl.Contains("dev-online-gateway.ghn.vn", StringComparison.OrdinalIgnoreCase))
        {
            return baseUrl.Replace("online-gateway.ghn.vn", "dev-online-gateway.ghn.vn", StringComparison.OrdinalIgnoreCase);
        }

        return null;
    }

    private async Task<(HttpResponseMessage Resp, string Body)> SendJsonAsync(
        HttpMethod method,
        string url,
        string jsonBody,
        int? shopId,
        string? tokenOverride,
        bool forceAuthorizationHeader,
        bool suppressAuthorizationHeader,
        CancellationToken cancellationToken)
    {
        using var req = new HttpRequestMessage(method, url);
        ApplyAuthHeaders(req, shopId, tokenOverride, forceAuthorizationHeader, suppressAuthorizationHeader);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        req.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

        var resp = await _http.SendAsync(req, cancellationToken);
        var body = await resp.Content.ReadAsStringAsync(cancellationToken);
        return (resp, body);
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
        _ = GetNormalizedTokenOrThrow();

        var resolvedShopId = shopId.GetValueOrDefault(_options.ShopId);
        if (resolvedShopId <= 0)
        {
            throw new InvalidOperationException("GHN ShopId is missing. Configure Ghn:ShopId in appsettings or user-secrets.");
        }

        var json = JsonSerializer.Serialize(reqBody, JsonOptions);
        using var req = new HttpRequestMessage(HttpMethod.Post, "v2/shipping-order/fee");
        ApplyAuthHeaders(req, shopId: resolvedShopId);
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
        _ = GetNormalizedTokenOrThrow();

        var resolvedShopId = shopId.GetValueOrDefault(_options.ShopId);
        if (resolvedShopId <= 0)
        {
            throw new InvalidOperationException("GHN ShopId is missing. Configure Ghn:ShopId in appsettings or user-secrets.");
        }

        var json = JsonSerializer.Serialize(reqBody, JsonOptions);
        using var req = new HttpRequestMessage(HttpMethod.Post, "v2/shipping-order/create");
        ApplyAuthHeaders(req, shopId: resolvedShopId);
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
        _ = GetNormalizedTokenOrThrow();

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
        ApplyAuthHeaders(req);
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
        using var req = new HttpRequestMessage(HttpMethod.Get, "v2/shift/date");
        ApplyAuthHeaders(req);
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
    /// Get list of GHN stations (bưu cục).
    /// Endpoint: POST v2/station/get
    /// </summary>
    public async Task<IReadOnlyList<GhnStation>> GetStationsAsync(GhnStationGetRequest reqBody, CancellationToken cancellationToken)
    {
        var stationToken = string.IsNullOrWhiteSpace(_options.StationToken) ? null : _options.StationToken;
        _ = GetNormalizedTokenOrThrow(stationToken);
        var shopId = _options.ShopId;

        if (reqBody is null) throw new ArgumentNullException(nameof(reqBody));
        if (reqBody.DistrictId <= 0) throw new InvalidOperationException("district_id is required.");

        if (reqBody.Limit <= 0) reqBody.Limit = 1000;
        if (reqBody.Limit > 1000) reqBody.Limit = 1000;
        if (reqBody.Offset < 0) reqBody.Offset = 0;

        var json = JsonSerializer.Serialize(reqBody, JsonOptions);

        var stationBaseUrl = GetEffectiveBaseUrlForStationGet();
        var stationUrl = stationBaseUrl.TrimEnd('/') + "/v2/station/get";

        // Some docs/samples use GET with JSON body for station/get.
        // Some gateways treat POST differently (and may enforce different security rules), so we try GET first.
        // Strategy:
        // - Try without Authorization header first (some gateways treat non-JWT Bearer as invalid segments).
        // - If GHN explicitly requires Authorization, retry with Authorization: Bearer <token>.
        var (resp, body) = await SendJsonAsync(
            HttpMethod.Get,
            stationUrl,
            json,
            shopId: shopId > 0 ? shopId : null,
            tokenOverride: stationToken,
            forceAuthorizationHeader: false,
            suppressAuthorizationHeader: true,
            cancellationToken);

        // If GET isn't supported by the gateway, fall back to POST.
        if (!resp.IsSuccessStatusCode && (int)resp.StatusCode is 404 or 405)
        {
            resp.Dispose();
            (resp, body) = await SendJsonAsync(
                HttpMethod.Post,
                stationUrl,
                json,
                shopId: shopId > 0 ? shopId : null,
                tokenOverride: stationToken,
                forceAuthorizationHeader: false,
                suppressAuthorizationHeader: true,
                cancellationToken);
        }

        if (!resp.IsSuccessStatusCode && IsAuthorizationRequired(body))
        {
            resp.Dispose();
            (resp, body) = await SendJsonAsync(
                HttpMethod.Get,
                stationUrl,
                json,
                shopId: shopId > 0 ? shopId : null,
                tokenOverride: stationToken,
                forceAuthorizationHeader: true,
                suppressAuthorizationHeader: false,
                cancellationToken);

            if (!resp.IsSuccessStatusCode && (int)resp.StatusCode is 404 or 405)
            {
                resp.Dispose();
                (resp, body) = await SendJsonAsync(
                    HttpMethod.Post,
                    stationUrl,
                    json,
                    shopId: shopId > 0 ? shopId : null,
                    tokenOverride: stationToken,
                    forceAuthorizationHeader: true,
                    suppressAuthorizationHeader: false,
                    cancellationToken);
            }
        }

        // If the configured BaseUrl is wrong (dev vs prod), GHN's security layer may reject the token.
        // In that case, we automatically retry the same request against the alternative gateway.
        if (!resp.IsSuccessStatusCode && IsSecurityCheckFailed(body))
        {
            var altBase = GetAlternativeBaseUrl(stationBaseUrl);
            if (!string.IsNullOrWhiteSpace(altBase))
            {
                var altUrl = altBase.TrimEnd('/') + "/v2/station/get";

                // Retry on alternative gateway using the same method pattern: GET first, then POST if needed.
                var (altResp, altBody) = await SendJsonAsync(
                    HttpMethod.Get,
                    altUrl,
                    json,
                    shopId: shopId > 0 ? shopId : null,
                    tokenOverride: stationToken,
                    forceAuthorizationHeader: false,
                    suppressAuthorizationHeader: true,
                    cancellationToken);
                if (!altResp.IsSuccessStatusCode && (int)altResp.StatusCode is 404 or 405)
                {
                    altResp.Dispose();
                    (altResp, altBody) = await SendJsonAsync(
                        HttpMethod.Post,
                        altUrl,
                        json,
                        shopId: shopId > 0 ? shopId : null,
                        tokenOverride: stationToken,
                        forceAuthorizationHeader: false,
                        suppressAuthorizationHeader: true,
                        cancellationToken);
                }

                if (!altResp.IsSuccessStatusCode && IsAuthorizationRequired(altBody))
                {
                    altResp.Dispose();
                    (altResp, altBody) = await SendJsonAsync(
                        HttpMethod.Get,
                        altUrl,
                        json,
                        shopId: shopId > 0 ? shopId : null,
                        tokenOverride: stationToken,
                        forceAuthorizationHeader: true,
                        suppressAuthorizationHeader: false,
                        cancellationToken);
                    if (!altResp.IsSuccessStatusCode && (int)altResp.StatusCode is 404 or 405)
                    {
                        altResp.Dispose();
                        (altResp, altBody) = await SendJsonAsync(
                            HttpMethod.Post,
                            altUrl,
                            json,
                            shopId: shopId > 0 ? shopId : null,
                            tokenOverride: stationToken,
                            forceAuthorizationHeader: true,
                            suppressAuthorizationHeader: false,
                            cancellationToken);
                    }
                }
                if (altResp.IsSuccessStatusCode)
                {
                    resp.Dispose();
                    resp = altResp;
                    body = altBody;
                }
                else
                {
                    altResp.Dispose();
                    throw new HttpRequestException(
                        $"GHN station/get request failed ({(int)resp.StatusCode}) on '{_options.BaseUrl}': {body} " +
                        $"| Retry on '{altBase}' failed ({(int)altResp.StatusCode}): {altBody}");
                }
            }
        }

        if (!resp.IsSuccessStatusCode)
        {
            resp.Dispose();
            throw new HttpRequestException($"GHN station/get request failed ({(int)resp.StatusCode}): {body}");
        }

        resp.Dispose();

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
        _ = GetNormalizedTokenOrThrow();

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
        ApplyAuthHeaders(req, shopId: resolvedShopId);
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
        _ = GetNormalizedTokenOrThrow();

        var resolvedShopId = shopId.GetValueOrDefault(_options.ShopId);
        if (resolvedShopId <= 0)
        {
            throw new InvalidOperationException("GHN ShopId is missing. Configure Ghn:ShopId in appsettings or user-secrets.");
        }

        var json = JsonSerializer.Serialize(reqBody, JsonOptions);
        using var req = new HttpRequestMessage(HttpMethod.Post, "v2/shipping-order/leadtime");
        ApplyAuthHeaders(req, shopId: resolvedShopId);
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
        _ = GetNormalizedTokenOrThrow();

        var code = (orderCode ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new InvalidOperationException("order_code is required.");
        }

        // Some GHN endpoints require ShopId; we provide it when available.
        var resolvedShopId = shopId.GetValueOrDefault(_options.ShopId);

        var json = JsonSerializer.Serialize(new GhnShippingOrderDetailRequest { OrderCode = code }, JsonOptions);
        using var req = new HttpRequestMessage(HttpMethod.Post, "v2/shipping-order/detail");
        ApplyAuthHeaders(req, shopId: resolvedShopId > 0 ? resolvedShopId : null);
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
        _ = GetNormalizedTokenOrThrow();

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
        ApplyAuthHeaders(req, shopId: resolvedShopId);
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
        using var req = new HttpRequestMessage(HttpMethod.Get, relativeUrl);
        ApplyAuthHeaders(req);
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

public sealed record GhnAuthDebugInfo(
    string BaseUrl,
    int ShopId,
    int TokenLength,
    int DotCount,
    bool LooksLikeJwt,
    string TokenMasked,
    string TokenSource);

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
