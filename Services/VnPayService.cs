using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace DOAN_BANHANG.Services;

public sealed class VnPayOptions
{
    public string TmnCode { get; set; } = string.Empty;
    public string HashSecret { get; set; } = string.Empty;
    public string PaymentUrl { get; set; } = "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html";
    public string ReturnUrl { get; set; } = string.Empty;
}

public sealed class VnPayService
{
    private readonly VnPayOptions _options;

    public VnPayService(VnPayOptions options)
    {
        _options = options;
    }

    /// <summary>
    /// Build the VNPay payment URL that the user will be redirected to.
    /// </summary>
    public string CreatePaymentUrl(
        string txnRef,
        decimal amount,
        string orderInfo,
        string ipAddress,
        string locale = "vn",
        string? bankCode = null,
        string? customReturnUrl = null)
    {
        var returnUrl = !string.IsNullOrWhiteSpace(customReturnUrl) ? customReturnUrl : _options.ReturnUrl;
        var vnpParams = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            { "vnp_Version", "2.1.0" },
            { "vnp_Command", "pay" },
            { "vnp_TmnCode", _options.TmnCode },
            { "vnp_Amount", ((long)(amount * 100)).ToString() },
            { "vnp_CreateDate", DateTime.Now.ToString("yyyyMMddHHmmss") },
            { "vnp_CurrCode", "VND" },
            { "vnp_IpAddr", ipAddress },
            { "vnp_Locale", locale },
            { "vnp_OrderInfo", orderInfo },
            { "vnp_OrderType", "other" },
            { "vnp_ReturnUrl", returnUrl },
            { "vnp_TxnRef", txnRef },
            { "vnp_ExpireDate", DateTime.Now.AddMinutes(15).ToString("yyyyMMddHHmmss") }
        };

        if (!string.IsNullOrWhiteSpace(bankCode))
        {
            vnpParams["vnp_BankCode"] = bankCode;
        }

        var hashData = new StringBuilder();
        var queryData = new StringBuilder();
        var first = true;

        foreach (var kv in vnpParams)
        {
            if (!first)
            {
                hashData.Append('&');
                queryData.Append('&');
            }

            hashData.Append(WebUtility.UrlEncode(kv.Key));
            hashData.Append('=');
            hashData.Append(WebUtility.UrlEncode(kv.Value));

            queryData.Append(WebUtility.UrlEncode(kv.Key));
            queryData.Append('=');
            queryData.Append(WebUtility.UrlEncode(kv.Value));

            first = false;
        }

        var secureHash = HmacSha512(_options.HashSecret, hashData.ToString());
        queryData.Append("&vnp_SecureHash=");
        queryData.Append(secureHash);

        return $"{_options.PaymentUrl}?{queryData}";
    }

    /// <summary>
    /// Validate the checksum from VNPay's response (Return URL or IPN).
    /// Returns true if the signature is valid.
    /// </summary>
    public bool ValidateSignature(IQueryCollection query)
    {
        var vnpParams = new SortedDictionary<string, string>(StringComparer.Ordinal);
        string? receivedHash = null;

        foreach (var kv in query)
        {
            if (string.IsNullOrEmpty(kv.Key)) continue;

            if (kv.Key.Equals("vnp_SecureHash", StringComparison.OrdinalIgnoreCase)
                || kv.Key.Equals("vnp_SecureHashType", StringComparison.OrdinalIgnoreCase))
            {
                if (kv.Key.Equals("vnp_SecureHash", StringComparison.OrdinalIgnoreCase))
                {
                    receivedHash = kv.Value.ToString();
                }
                continue;
            }

            if (kv.Key.StartsWith("vnp_", StringComparison.OrdinalIgnoreCase))
            {
                vnpParams[kv.Key] = kv.Value.ToString();
            }
        }

        if (string.IsNullOrWhiteSpace(receivedHash))
            return false;

        var hashData = new StringBuilder();
        var first = true;

        foreach (var kv in vnpParams)
        {
            if (!first)
            {
                hashData.Append('&');
            }

            hashData.Append(WebUtility.UrlEncode(kv.Key));
            hashData.Append('=');
            hashData.Append(WebUtility.UrlEncode(kv.Value));

            first = false;
        }

        var computedHash = HmacSha512(_options.HashSecret, hashData.ToString());

        return string.Equals(computedHash, receivedHash, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Extract key fields from VNPay's response query string.
    /// </summary>
    public VnPayReturnData ParseReturnData(IQueryCollection query)
    {
        return new VnPayReturnData
        {
            TxnRef = query["vnp_TxnRef"].ToString(),
            Amount = long.TryParse(query["vnp_Amount"].ToString(), out var amt) ? amt / 100m : 0m,
            ResponseCode = query["vnp_ResponseCode"].ToString(),
            TransactionStatus = query["vnp_TransactionStatus"].ToString(),
            TransactionNo = query["vnp_TransactionNo"].ToString(),
            BankCode = query["vnp_BankCode"].ToString(),
            BankTranNo = query["vnp_BankTranNo"].ToString(),
            PayDate = query["vnp_PayDate"].ToString(),
            OrderInfo = query["vnp_OrderInfo"].ToString()
        };
    }

    private static string HmacSha512(string key, string data)
    {
        using var hmac = new HMACSHA512(Encoding.UTF8.GetBytes(key));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
        return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
    }
}

public sealed class VnPayReturnData
{
    public string TxnRef { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string ResponseCode { get; set; } = string.Empty;
    public string TransactionStatus { get; set; } = string.Empty;
    public string TransactionNo { get; set; } = string.Empty;
    public string BankCode { get; set; } = string.Empty;
    public string BankTranNo { get; set; } = string.Empty;
    public string PayDate { get; set; } = string.Empty;
    public string OrderInfo { get; set; } = string.Empty;

    public bool IsSuccess => ResponseCode == "00" && TransactionStatus == "00";
}
