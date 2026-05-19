using System.Text;
using System.Text.Json;

namespace DOAN_BANHANG.Services;

public class GeminiChatService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<GeminiChatService> _logger;

    public GeminiChatService(HttpClient httpClient, ILogger<GeminiChatService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        // Cấu hình URL mặc định của Python Flask Server
        _httpClient.BaseAddress = new Uri("http://127.0.0.1:5000/");
    }

    public async Task<AiChatResult> AskGeminiAsync(int accountId, string message, CancellationToken ct = default)
    {
        try
        {
            // Match Flask contract in AI_TMDT/api_server.py: expects `message` and `accountId` (optional `userName`).
            var payload = new { message, accountId };
            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync("api/chat", content, ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Chatbot AI Server returned status code {StatusCode}", response.StatusCode);
                return new AiChatResult
                {
                    Reply = "Xin lỗi, hiện tại AI Server đang bảo trì hoặc địa chỉ API chưa đúng. Vui lòng thử lại sau.",
                    Products = null
                };
            }

            var responseString = await response.Content.ReadAsStringAsync(ct);
            using var jsonDoc = JsonDocument.Parse(responseString);

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var result = new AiChatResult { Reply = string.Empty, Products = null };
            
            // Giả định Python Server trả về JSON có key "reply" hoặc "response" hoặc "message"
            if (jsonDoc.RootElement.TryGetProperty("reply", out var replyElement))
            {
                result.Reply = replyElement.GetString() ?? "";
            }
            else if (jsonDoc.RootElement.TryGetProperty("response", out var respElement))
            {
                result.Reply = respElement.GetString() ?? "";
            }
            else if (jsonDoc.RootElement.TryGetProperty("message", out var msgElement))
            {
                result.Reply = msgElement.GetString() ?? "";
            }

            if (jsonDoc.RootElement.TryGetProperty("products", out var productsElement)
                && productsElement.ValueKind == JsonValueKind.Array)
            {
                try
                {
                    var products = JsonSerializer.Deserialize<List<AiProductCard>>(productsElement.GetRawText(), options);
                    result.Products = products;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to parse products payload from AI server.");
                    result.Products = null;
                }
            }

            if (string.IsNullOrWhiteSpace(result.Reply))
                result.Reply = "Không thể đọc phản hồi từ AI Server. (Sai định dạng JSON)";

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi gọi AI Chatbot Server");
            return new AiChatResult
            {
                Reply = "Không thể kết nối đến AI Chatbot Server (Port 5000). Vui lòng đảm bảo server đang chạy.",
                Products = null
            };
        }
    }
}

public class AiChatResult
{
    public string Reply { get; set; } = string.Empty;
    public List<AiProductCard>? Products { get; set; }
}

public class AiProductCard
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public int MinPrice { get; set; }
    public int MaxPrice { get; set; }
    public double Rating { get; set; }
}
