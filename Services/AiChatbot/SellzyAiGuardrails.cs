using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace DOAN_BANHANG.Services.AiChatbot;

/// <summary>
/// Lớp Rào chắn bảo mật (Guardrails) cho Trợ lý AI Sellzy:
/// 1. Chặn Prompt Injection đầu vào (lệnh ẩn, ép tiết lộ prompt, ép tặng voucher 100%, giả mạo Admin).
/// 2. Bọc dữ liệu ngoài từ DB trong thẻ ranh giới chống Indirect Prompt Injection.
/// 3. Che thông tin nhạy cảm (PII: số điện thoại, email) trước khi trả ra giao diện.
/// </summary>
public static class SellzyAiGuardrails
{
    private static readonly string[] InjectionPatterns =
    {
        "bo qua moi chi thi",
        "bo qua huong dan",
        "bo qua quy tac",
        "quen het chi thi",
        "ignore previous instruction",
        "ignore all previous",
        "disregard previous",
        "system prompt",
        "in ra prompt",
        "hien thi prompt",
        "cho xem prompt",
        "loi dan an",
        "jailbreak",
        "dan mode",
        "developer mode",
        "ban bay gio la admin",
        "dong vai admin",
        "tang voucher 100",
        "tang ma giam gia 100",
        "cap ma giam 100",
        "giam gia 100%",
        "tao voucher 100",
        "mien phi 100%",
        "xem don hang cua nguoi khac",
        "xem thong tin nguoi khac",
        "drop table",
        "delete from",
        "select * from"
    };

    public static (bool IsBlocked, string? Reason, string? SafeReply) CheckInput(string? userMessage)
    {
        if (string.IsNullOrWhiteSpace(userMessage))
            return (false, null, null);

        if (userMessage.Length > 1200)
        {
            return (
                true,
                "InputLengthExceeded",
                "Tin nhắn quá dài (tối đa 1.200 ký tự). Bạn vui lòng rút gọn câu hỏi để Trợ lý AI Sellzy hỗ trợ nhanh nhất nhé!"
            );
        }

        var norm = Normalize(userMessage);
        foreach (var pattern in InjectionPatterns)
        {
            if (norm.Contains(pattern, StringComparison.Ordinal))
            {
                return (
                    true,
                    $"PromptInjection:{pattern}",
                    "Xin lỗi, mình không thể thực hiện yêu cầu này vì liên quan đến quy định bảo mật và phân quyền của hệ thống **Sellzy**. " +
                    "Mình chỉ hỗ trợ tìm kiếm sản phẩm, tra cứu đơn hàng/giỏ hàng/ví của chính bạn, xem ưu đãi chính thức và giải đáp chính sách sàn nhé!"
                );
            }
        }

        return (false, null, null);
    }

    /// <summary>
    /// Bọc dữ liệu tự do từ DB (ghi chú đơn hàng, mô tả khiếu nại, mô tả sản phẩm)
    /// để ngăn chặn Indirect Prompt Injection.
    /// </summary>
    public static string WrapUntrustedData(string? rawText, int maxLen = 240)
    {
        if (string.IsNullOrWhiteSpace(rawText)) return string.Empty;
        var clean = rawText
            .Replace("<", "(")
            .Replace(">", ")")
            .Replace("\r", " ")
            .Replace("\n", " ")
            .Trim();
        if (clean.Length > maxLen) clean = clean[..maxLen] + "...";
        return $"<du_lieu_he_thong>{clean}</du_lieu_he_thong>";
    }

    /// <summary>
    /// Che PII (số điện thoại, email) và gỡ bỏ các thẻ nội bộ nếu model vô tình lặp lại.
    /// </summary>
    public static string SanitizeOutput(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        var result = text;

        // Gỡ bỏ thẻ nội bộ nếu bị lọt ra ngoài
        result = Regex.Replace(result, @"<\/?du_lieu_he_thong>", string.Empty, RegexOptions.IgnoreCase);
        result = Regex.Replace(result, @"\[Ngữ cảnh hệ thống:[^\]]*\]", string.Empty, RegexOptions.IgnoreCase);

        // Che số điện thoại Việt Nam (0xxxxxxxxx hoặc +84xxxxxxxxx -> giữ 3 số đầu và 3 số cuối)
        result = Regex.Replace(result, @"(?<!\d)(\+?84|0)(\d{2})\d{4,5}(\d{3})(?!\d)", "$1$2***$3");

        // Che địa chỉ Email (abcde@domain.com -> ab***@domain.com)
        result = Regex.Replace(
            result,
            @"\b([A-Za-z0-9._%+-]{1,2})[A-Za-z0-9._%+-]*@([A-Za-z0-9.-]+\.[A-Za-z]{2,})\b",
            "$1***@$2");

        return result.Trim();
    }

    public static string MaskPhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return string.Empty;
        return Regex.Replace(phone.Trim(), @"(\+?84|0)(\d{2})\d{4,5}(\d{3})", "$1$2***$3");
    }

    public static string MaskEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return string.Empty;
        return Regex.Replace(
            email.Trim(),
            @"^([A-Za-z0-9._%+-]{1,2})[A-Za-z0-9._%+-]*@(.+)$",
            "$1***@$2");
    }

    private static string Normalize(string input)
    {
        var formD = input.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(formD.Length);
        foreach (var ch in formD)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                sb.Append(ch);
        }
        return Regex.Replace(
            sb.ToString().Replace('đ', 'd').Replace('Đ', 'd').ToLowerInvariant(),
            @"\s+",
            " ").Trim();
    }
}
