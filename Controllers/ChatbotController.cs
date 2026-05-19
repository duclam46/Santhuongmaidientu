using System.Security.Claims;
using DOAN_BANHANG.Models;
using DOAN_BANHANG.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DOAN_BANHANG.Controllers;

[Authorize] // Bắt buộc đăng nhập mới được chat với AI
[Route("api/chatbot")]
[ApiController]
public class ChatbotController : ControllerBase
{
    private readonly DataContext _db;
    private readonly GeminiChatService _geminiService;
    private readonly ILogger<ChatbotController> _logger;
    
    // Giả định ID 1 là tài khoản của Admin Sàn/Hệ thống Bot. 
    // Bạn cần đảm bảo trong bảng Accounts có 1 record Id = 1 (Role = Admin) để làm BOT.
    private const int SYSTEM_BOT_ID = 1; 

    public ChatbotController(DataContext db, GeminiChatService geminiService, ILogger<ChatbotController> logger)
    {
        _db = db;
        _geminiService = geminiService;
        _logger = logger;
    }

    private async Task<int> ResolveBotAccountIdAsync(int currentAccountId, CancellationToken ct)
    {
        // Prefer the configured SYSTEM_BOT_ID if it exists.
        if (await _db.Accounts.AnyAsync(a => a.Id == SYSTEM_BOT_ID && !a.IsDeleted, ct))
            return SYSTEM_BOT_ID;

        // Otherwise, fall back to any Admin account.
        var adminId = await _db.Accounts
            .Where(a => a.Role == "Admin" && !a.IsDeleted)
            .OrderBy(a => a.Id)
            .Select(a => a.Id)
            .FirstOrDefaultAsync(ct);

        if (adminId != 0)
            return adminId;

        // Last resort: use current user id to avoid FK failures.
        return currentAccountId;
    }

    [HttpPost("ask")]
    public async Task<IActionResult> AskBot([FromBody] ChatbotRequest request, CancellationToken ct)
    {
        // 1. Lấy AccountId của khách hàng đang đăng nhập
        var idRaw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(idRaw, out var accountId)) return Unauthorized();

        if (string.IsNullOrWhiteSpace(request.Message))
            return BadRequest(new { success = false, message = "Tin nhắn không được để trống." });

        var botAccountId = await ResolveBotAccountIdAsync(accountId, ct);

        Conversation? conversation = null;
        try
        {
            // 2. Tìm phòng chat giữa Khách và BOT (Nếu chưa có thì tự động tạo mới)
            conversation = await _db.Conversations
                .FirstOrDefaultAsync(c => c.CustomerId == accountId && c.ShopId == botAccountId, ct);

            if (conversation == null)
            {
                conversation = new Conversation
                {
                    CustomerId = accountId,
                    ShopId = botAccountId,
                    CreatedAt = DateTime.UtcNow,
                    LastMessageAt = DateTime.UtcNow
                };
                _db.Conversations.Add(conversation);
                await _db.SaveChangesAsync(ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load/create conversation for account {AccountId} (bot {BotId}). Will continue without DB persistence.", accountId, botAccountId);
            conversation = null;
        }

        // 3. (Best-effort) Lưu tin nhắn của Khách hàng vào Database
        try
        {
            if (conversation != null)
            {
                var dbContent = request.Message.Length <= 2000 ? request.Message : request.Message[..2000];
                var userMsg = new Message
                {
                    ConversationId = conversation.Id,
                    SenderId = accountId,
                    Content = dbContent,
                    MessageType = "text",
                    CreatedAt = DateTime.UtcNow
                };
                _db.Messages.Add(userMsg);
                await _db.SaveChangesAsync(ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist user message for account {AccountId}.", accountId);
        }

        string aiReply;
        object? productsPayload = null;
        try
        {
            // 4. Gọi Service Gemini để phân tích và lấy câu trả lời (Áp dụng RAG - Truy xuất giỏ hàng/thông tin)
            var aiResult = await _geminiService.AskGeminiAsync(accountId, request.Message, ct);
            aiReply = aiResult.Reply;
            productsPayload = aiResult.Products;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI service failed for account {AccountId}.", accountId);
            return StatusCode(503, new { success = false, message = "AI đang bận hoặc không phản hồi. Vui lòng thử lại sau." });
        }

        // 5. (Best-effort) Lưu câu trả lời của AI vào Database
        try
        {
            if (conversation != null)
            {
                var dbReply = aiReply.Length <= 2000 ? aiReply : aiReply[..2000];
                var botMsg = new Message
                {
                    ConversationId = conversation.Id,
                    SenderId = botAccountId,
                    Content = dbReply,
                    MessageType = "text",
                    CreatedAt = DateTime.UtcNow
                };
                _db.Messages.Add(botMsg);

                conversation.LastMessageAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist bot message for account {AccountId}.", accountId);
        }

        // 6. Trả kết quả về cho Giao diện (Frontend)
        return Ok(new { success = true, reply = aiReply, products = productsPayload });
    }

    [HttpGet("history")]
    public async Task<IActionResult> GetHistory([FromQuery] int take = 30, [FromQuery] int days = 30, CancellationToken ct = default)
    {
        var idRaw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(idRaw, out var accountId)) return Unauthorized();

        take = Math.Clamp(take, 1, 50);
        days = Math.Clamp(days, 1, 365);
        var since = DateTime.UtcNow.AddDays(-days);

        var botAccountId = await ResolveBotAccountIdAsync(accountId, ct);

        var conversation = await _db.Conversations
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.CustomerId == accountId && c.ShopId == botAccountId, ct);

        if (conversation == null)
            return Ok(new { success = true, messages = Array.Empty<object>() });

        var messages = await _db.Messages
            .AsNoTracking()
            .Where(m => m.ConversationId == conversation.Id && !m.IsDeleted && m.CreatedAt >= since)
            .OrderByDescending(m => m.CreatedAt)
            .Take(take)
            .Select(m => new
            {
                sender = m.SenderId == accountId ? "user" : "bot",
                content = m.Content,
                createdAt = m.CreatedAt
            })
            .ToListAsync(ct);

        messages.Reverse();
        return Ok(new { success = true, messages });
    }
}

// Lớp chứa dữ liệu nhận từ Frontend
public class ChatbotRequest
{
    public string Message { get; set; } = string.Empty;
}