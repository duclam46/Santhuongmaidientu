using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace DOAN_BANHANG.Services.AiChatbot;

/// <summary>
/// Bộ máy xử lý ngôn ngữ tiếng Việt nội bộ (Local Intent + Slot Engine).
/// Tự động kích hoạt khi chưa cấu hình Gemini API Key hoặc Gemini gặp lỗi mạng / giới hạn quota (429),
/// đảm bảo Chatbot Sellzy luôn trả lời nhanh và có dữ liệu thật 100%.
/// </summary>
public class SellzyAiFallbackEngine
{
    private readonly SellzyAiTools _tools;

    public SellzyAiFallbackEngine(SellzyAiTools tools)
    {
        _tools = tools;
    }

    public async Task<AiChatResult> ProcessAsync(int accountId, string message, CancellationToken ct = default)
    {
        var raw = (message ?? string.Empty).Trim();
        var norm = NormalizeText(raw);

        // 1. Chào hỏi
        if (Regex.IsMatch(norm, @"^(xin chao|chao|hello|hi|hey|alo|shop oi|ad oi)\b") && norm.Length <= 25)
        {
            return new AiChatResult
            {
                Mode = "local",
                Reply = "Xin chào! Mình là **Trợ lý AI Sellzy**. Mình có thể giúp bạn:\n" +
                        "- Tìm & tư vấn sản phẩm đa ngành theo nhu cầu, thương hiệu, tầm giá\n" +
                        "- Săn **Flash Sale** và **Mã giảm giá (Voucher)** hôm nay\n" +
                        "- Tra cứu **Đơn hàng**, **Giỏ hàng**, **Ví Sellzy** và **Khiếu nại**\n\n" +
                        "Bạn đang cần tìm sản phẩm gì hay muốn kiểm tra đơn hàng nào?",
                QuickLinks = new List<AiQuickLink>
                {
                    new() { Label = "Khám phá sản phẩm", Url = "/products" }
                }
            };
        }

        // 2. Chính sách đổi trả / thanh toán / bảo hành / phí sàn (RAG động từ DB)
        if (ContainsAny(norm, "chinh sach", "doi tra", "bao hanh", "thanh toan", "cod", "vnpay", "may ngay", "trong tai", "sellzy mall la gi"))
        {
            var res = await _tools.GetStorePoliciesAsync(raw, ct);
            return new AiChatResult { Mode = "local", Reply = res.Text, Products = res.Products, QuickLinks = res.QuickLinks };
        }

        // 3. Ví Sellzy & Khiếu nại / Trả hàng
        if (ContainsAny(norm, "vi sellzy", "so du vi", "tien trong vi", "khieu nai", "hoan tien", "tra hang", "tranh chap", "dispute"))
        {
            if (accountId <= 0)
            {
                return new AiChatResult
                {
                    Mode = "local",
                    Reply = "Để kiểm tra **Ví Sellzy** hoặc **Khiếu nại / Trả hàng**, bạn vui lòng đăng nhập tài khoản trước nhé!\n" +
                            "Sau khi đăng nhập, bạn có thể gửi khiếu nại trực tiếp từ trang chi tiết đơn hàng — hệ thống trọng tài 3 bên của Sellzy sẽ bảo vệ quyền lợi và hoàn tiền về Ví Sellzy nếu hợp lệ."
                };
            }

            var res = await _tools.GetMyWalletAndDisputesAsync(accountId, ct);
            return new AiChatResult { Mode = "local", Reply = res.Text, Products = res.Products, QuickLinks = res.QuickLinks };
        }

        // 3. Giỏ hàng
        if (ContainsAny(norm, "gio hang", "trong gio", "cart"))
        {
            var res = await _tools.GetMyCartAsync(accountId, ct);
            return new AiChatResult { Mode = "local", Reply = res.Text, Products = res.Products, QuickLinks = res.QuickLinks };
        }

        // 4. Thông tin tài khoản / Hồ sơ
        if (ContainsAny(norm, "thong tin ca nhan", "tai khoan cua toi", "ho so cua toi", "toi la ai", "profile"))
        {
            var res = await _tools.GetMyProfileAsync(accountId, ct);
            return new AiChatResult { Mode = "local", Reply = res.Text, Products = res.Products, QuickLinks = res.QuickLinks };
        }

        // 5. Đơn hàng (có mã đơn hoặc xem đơn gần nhất)
        var orderCodeMatch = Regex.Match(raw, @"\b(ORD[A-Z0-9\-]{4,}|DH[A-Z0-9\-]{4,})\b", RegexOptions.IgnoreCase);
        if (orderCodeMatch.Success)
        {
            var res = await _tools.GetOrderStatusAsync(accountId, orderCodeMatch.Groups[1].Value.ToUpperInvariant(), ct);
            return new AiChatResult { Mode = "local", Reply = res.Text, Products = res.Products, QuickLinks = res.QuickLinks };
        }

        if (ContainsAny(norm, "don hang", "kiem tra don", "tra cuu don", "giao hang", "van chuyen", "order"))
        {
            var res = await _tools.GetMyRecentOrdersAsync(accountId, 5, ct);
            return new AiChatResult { Mode = "local", Reply = res.Text, Products = res.Products, QuickLinks = res.QuickLinks };
        }

        // 6. Voucher / Mã giảm giá / Flash Sale
        if (ContainsAny(norm, "voucher", "ma giam gia", "khuyen mai", "flash sale", "giam gia", "uu dai", "sale hom nay"))
        {
            var res = await _tools.GetActiveVouchersAndFlashSalesAsync(ct);
            return new AiChatResult { Mode = "local", Reply = res.Text, Products = res.Products, QuickLinks = res.QuickLinks };
        }

        // 7. So sánh 2 sản phẩm ("so sánh A và B" / "so sánh A với B")
        var compareMatch = Regex.Match(raw, @"so\s*s[aá]nh\s+(.+?)\s+(?:v[aà]|v[oớ]i|vs\.?)\s+(.+)", RegexOptions.IgnoreCase);
        if (compareMatch.Success)
        {
            var res = await _tools.CompareProductsAsync(compareMatch.Groups[1].Value.Trim(), compareMatch.Groups[2].Value.Trim(), ct);
            return new AiChatResult { Mode = "local", Reply = res.Text, Products = res.Products, QuickLinks = res.QuickLinks };
        }

        // 8. Mặc định: Tìm kiếm & Tư vấn sản phẩm đa ngành theo khoảng giá + Mall + từ khóa
        var (minPrice, maxPrice) = ExtractPriceRange(norm);
        var onlyMall = ContainsAny(norm, "mall", "chinh hang");
        var onlySale = ContainsAny(norm, "flash sale", "dang giam gia");
        var cleanQuery = ExtractCleanKeyword(raw);

        var searchRes = await _tools.SearchProductsAsync(
            query: cleanQuery,
            minPrice: minPrice,
            maxPrice: maxPrice,
            onlyFlashSale: onlySale,
            onlyMall: onlyMall,
            limit: 5,
            ct: ct);

        return new AiChatResult
        {
            Mode = "local",
            Reply = searchRes.Text,
            Products = searchRes.Products,
            QuickLinks = searchRes.QuickLinks
        };
    }

    private static bool ContainsAny(string haystack, params string[] needles)
        => needles.Any(n => haystack.Contains(n, StringComparison.Ordinal));

    private static string NormalizeText(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        var formD = input.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(formD.Length);
        foreach (var ch in formD)
        {
            var uc = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (uc != UnicodeCategory.NonSpacingMark)
                sb.Append(ch);
        }
        return sb.ToString()
            .Replace('đ', 'd')
            .Replace('Đ', 'd')
            .ToLowerInvariant()
            .Trim();
    }

    private static (decimal? MinPrice, decimal? MaxPrice) ExtractPriceRange(string norm)
    {
        decimal? minPrice = null;
        decimal? maxPrice = null;

        static decimal? ParseAmount(string numStr, string unit)
        {
            var cleaned = numStr.Replace(",", ".");
            if (!decimal.TryParse(cleaned, NumberStyles.Any, CultureInfo.InvariantCulture, out var val))
                return null;
            return unit switch
            {
                "tr" or "trieu" => val * 1_000_000m,
                "k" or "nghin" or "ngan" => val * 1_000m,
                _ => val < 1000 ? val * 1_000m : val
            };
        }

        var between = Regex.Match(norm, @"(?:tu)\s+(\d+(?:[.,]\d+)?)\s*(tr|trieu|k|nghin|ngan|d|vnd)?\s+(?:den|toi|-)\s+(\d+(?:[.,]\d+)?)\s*(tr|trieu|k|nghin|ngan|d|vnd)?");
        if (between.Success)
        {
            var unit2 = between.Groups[4].Value;
            var unit1 = string.IsNullOrEmpty(between.Groups[2].Value) ? unit2 : between.Groups[2].Value;
            minPrice = ParseAmount(between.Groups[1].Value, unit1);
            maxPrice = ParseAmount(between.Groups[3].Value, unit2);
            return (minPrice, maxPrice);
        }

        var under = Regex.Match(norm, @"(?:duoi|toi da|khong qua|nho hon|<)\s*(\d+(?:[.,]\d+)?)\s*(tr|trieu|k|nghin|ngan|d|vnd)?");
        if (under.Success)
            maxPrice = ParseAmount(under.Groups[1].Value, under.Groups[2].Value);

        var over = Regex.Match(norm, @"(?:tren|tu|toi thieu|lon hon|>)\s*(\d+(?:[.,]\d+)?)\s*(tr|trieu|k|nghin|ngan|d|vnd)?");
        if (over.Success)
            minPrice = ParseAmount(over.Groups[1].Value, over.Groups[2].Value);

        return (minPrice, maxPrice);
    }

    private static string ExtractCleanKeyword(string raw)
    {
        var q = raw;
        // Bỏ cụm giá
        q = Regex.Replace(q, @"(?:từ|dưới|trên|tối đa|khoảng|tầm)\s+\d+(?:[.,]\d+)?\s*(?:tr|triệu|trieu|k|nghìn|ngàn|đ|d|vnđ|vnd)?(?:\s*(?:đến|tới|-)\s*\d+(?:[.,]\d+)?\s*(?:tr|triệu|trieu|k|nghìn|ngàn|đ|d|vnđ|vnd)?)?", " ", RegexOptions.IgnoreCase);
        // Bỏ từ đệm hội thoại (ưu tiên cụm dài trước)
        var stopWords = new[]
        {
            "bạn hãy gợi ý cho tôi", "bạn hãy gợi ý cho mình", "bạn hãy tìm giúp tôi", "bạn hãy tìm giúp mình",
            "tìm giúp mình", "tìm giúp tôi", "gợi ý cho mình", "gợi ý cho tôi", "tư vấn cho mình", "tư vấn cho tôi",
            "mình muốn mua", "tôi muốn mua", "cho mình xem", "cho tôi xem", "bạn có bán", "shop có bán",
            "bạn hãy", "hãy giúp", "giúp mình", "giúp tôi", "cho mình", "cho tôi", "một cái", "một chiếc",
            "tìm", "mua", "gợi ý", "tư vấn", "sản phẩm", "giá rẻ", "tốt nhất", "chính hãng",
            "sellzy mall", "mall", "nào tốt", "bạn", "hãy", "một", "cái", "chiếc", "con", "em",
            "mình", "tôi", "đang", "cần", "muốn", "nào", "đẹp", "xịn", "tầm", "khoảng", "giá",
            "không", "nhé", "nha", "ạ", "với", "đi", "nhỉ"
        };
        foreach (var sw in stopWords)
            q = Regex.Replace(q, $@"(?<!\p{{L}}){Regex.Escape(sw)}(?!\p{{L}})", " ", RegexOptions.IgnoreCase);

        q = Regex.Replace(q, @"[?!.,;:]", " ");
        q = Regex.Replace(q, @"\s+", " ").Trim();
        return q;
    }
}
