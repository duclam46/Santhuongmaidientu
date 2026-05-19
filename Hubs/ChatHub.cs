using Microsoft.AspNetCore.SignalR;
using DOAN_BANHANG.Models;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace DOAN_BANHANG.Hubs;

public class ChatHub : Hub
{
    private readonly DataContext _db;

    public ChatHub(DataContext db)
    {
        _db = db;
    }

    // Khi user kết nối: tự động join group cá nhân "user_{id}"
    public override async Task OnConnectedAsync()
    {
        var accountId = GetAccountId();
        if (accountId.HasValue)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"user_{accountId}");

            // Shop cũng join group riêng để nhận thông báo tin mới từ khách
            var account = await _db.Accounts.FindAsync(accountId.Value);
            if (account?.Role == "Shop")
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, $"shop_{accountId}");
            }
        }
        await base.OnConnectedAsync();
    }

    // Client gọi khi mở cửa sổ chat một conversation
    public async Task JoinConversation(int conversationId)
    {
        var accountId = GetAccountId();
        if (!accountId.HasValue) return;

        // Kiểm tra user có quyền trong conversation này không
        var conv = await _db.Conversations.FindAsync(conversationId);
        if (conv == null) return;
        var isAdmin = Context.User?.IsInRole("Admin") ?? false;
        if (conv.CustomerId != accountId && conv.ShopId != accountId && !isAdmin) return;

        await Groups.AddToGroupAsync(Context.ConnectionId, $"conversation_{conversationId}");

        // Đánh dấu đã đọc ngay khi vào room
        await MarkReadInternal(conversationId, accountId.Value, conv);
    }

    // Client rời khỏi cửa sổ chat
    public async Task LeaveConversation(int conversationId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"conversation_{conversationId}");
    }

    // Client gửi tin nhắn text thông thường
    public async Task SendTextMessage(int conversationId, string content)
    {
        var accountId = GetAccountId();
        if (!accountId.HasValue || string.IsNullOrWhiteSpace(content)) return;

        var conv = await _db.Conversations.FindAsync(conversationId);
        var isAdmin = Context.User?.IsInRole("Admin") ?? false;
        if (conv == null || (conv.CustomerId != accountId && conv.ShopId != accountId && !isAdmin)) return;

        var msg = new Message
        {
            ConversationId = conversationId,
            SenderId = accountId.Value,
            Content = content.Trim(),
            MessageType = "text",
            CreatedAt = DateTime.UtcNow
        };
        _db.Messages.Add(msg);
        conv.LastMessageAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        // Lấy lại message kèm sender profile
        var saved = await BuildMessageDto(msg.Id);
        if (saved == null) return;

        // Push đến tất cả trong conversation
        await Clients.Group($"conversation_{conversationId}")
            .SendAsync("ReceiveMessage", saved);

        // Nếu sender là customer → thông báo shop
        if (conv.CustomerId == accountId)
        {
            await Clients.Group($"shop_{conv.ShopId}")
                .SendAsync("NewMessageFromCustomer", new { conversationId, content = content.Trim() });
        }
        // Nếu sender là shop → thông báo customer
        else
        {
            await Clients.Group($"user_{conv.CustomerId}")
                .SendAsync("NewMessageFromShop", new { conversationId, content = content.Trim() });
        }
    }

    // Gửi tin nhắn đính kèm sản phẩm
    public async Task SendProductCard(int conversationId, int productId, string? message)
    {
        var accountId = GetAccountId();
        if (!accountId.HasValue) return;

        var conv = await _db.Conversations.FindAsync(conversationId);
        var isAdmin = Context.User?.IsInRole("Admin") ?? false;
        if (conv == null || (conv.CustomerId != accountId && conv.ShopId != accountId && !isAdmin)) return;

        var product = await _db.Products.FindAsync(productId);
        if (product == null) return;

        var msg = new Message
        {
            ConversationId = conversationId,
            SenderId = accountId.Value,
            Content = string.IsNullOrWhiteSpace(message) ? "Tôi muốn hỏi về sản phẩm này." : message.Trim(),
            MessageType = "product_card",
            AttachedProductId = productId,
            CreatedAt = DateTime.UtcNow
        };
        _db.Messages.Add(msg);
        conv.LastMessageAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var saved = await BuildMessageDto(msg.Id);
        if (saved == null) return;

        await Clients.Group($"conversation_{conversationId}").SendAsync("ReceiveMessage", saved);

        if (conv.CustomerId == accountId)
        {
            await Clients.Group($"shop_{conv.ShopId}")
                .SendAsync("NewMessageFromCustomer", new { conversationId, content = msg.Content });
        }
        else
        {
            await Clients.Group($"user_{conv.CustomerId}")
                .SendAsync("NewMessageFromShop", new { conversationId, content = msg.Content });
        }
    }

    // Shop gửi voucher deal cho khách
    public async Task SendVoucherCard(int conversationId, int voucherId, string? message)
    {
        var accountId = GetAccountId();
        if (!accountId.HasValue) return;

        var conv = await _db.Conversations.FindAsync(conversationId);
        var isAdmin = Context.User?.IsInRole("Admin") ?? false;
        if (conv == null || (conv.ShopId != accountId && !isAdmin)) return; // chỉ shop hoặc admin gửi được

        var voucher = await _db.Vouchers.FindAsync(voucherId);
        if (voucher == null || (voucher.ShopId != conv.ShopId)) return;

        var msg = new Message
        {
            ConversationId = conversationId,
            SenderId = accountId.Value,
            Content = message ?? $"Mã giảm giá dành riêng cho bạn: {voucher.Code}",
            MessageType = "voucher_card",
            AttachedVoucherId = voucherId,
            CreatedAt = DateTime.UtcNow
        };
        _db.Messages.Add(msg);
        conv.LastMessageAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var saved = await BuildMessageDto(msg.Id);
        if (saved == null) return;

        await Clients.Group($"conversation_{conversationId}").SendAsync("ReceiveMessage", saved);
        await Clients.Group($"user_{conv.CustomerId}")
            .SendAsync("NewMessageFromShop", new { conversationId, content = msg.Content });
    }

    // Gửi tin nhắn đa phương tiện (hình ảnh / video)
    public async Task SendMediaMessage(int conversationId, string mediaUrl, string mediaType)
    {
        var accountId = GetAccountId();
        if (!accountId.HasValue || string.IsNullOrWhiteSpace(mediaUrl)) return;

        var conv = await _db.Conversations.FindAsync(conversationId);
        var isAdmin = Context.User?.IsInRole("Admin") ?? false;
        if (conv == null || (conv.CustomerId != accountId && conv.ShopId != accountId && !isAdmin)) return;

        var msg = new Message
        {
            ConversationId = conversationId,
            SenderId = accountId.Value,
            Content = mediaUrl.Trim(),
            MessageType = mediaType == "video" ? "video" : "image",
            CreatedAt = DateTime.UtcNow
        };
        _db.Messages.Add(msg);
        conv.LastMessageAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var saved = await BuildMessageDto(msg.Id);
        if (saved == null) return;

        await Clients.Group($"conversation_{conversationId}").SendAsync("ReceiveMessage", saved);

        var notifContent = mediaType == "video" ? "Đã gửi một video" : "Đã gửi một hình ảnh";
        if (conv.CustomerId == accountId)
        {
            await Clients.Group($"shop_{conv.ShopId}")
                .SendAsync("NewMessageFromCustomer", new { conversationId, content = notifContent });
        }
        else
        {
            await Clients.Group($"user_{conv.CustomerId}")
                .SendAsync("NewMessageFromShop", new { conversationId, content = notifContent });
        }
    }

    // Đánh dấu đã đọc
    public async Task MarkRead(int conversationId)
    {
        var accountId = GetAccountId();
        if (!accountId.HasValue) return;

        var conv = await _db.Conversations.FindAsync(conversationId);
        if (conv == null) return;

        await MarkReadInternal(conversationId, accountId.Value, conv);
    }

    // ─── Internal helpers ───────────────────────────────────────────────────

    private async Task MarkReadInternal(int conversationId, int accountId, Conversation conv)
    {
        bool isCustomer = conv.CustomerId == accountId;
        bool isShop = conv.ShopId == accountId;
        if (!isCustomer && !isShop) return;

        var unread = _db.Messages.Where(m => m.ConversationId == conversationId && !m.IsDeleted);
        if (isCustomer)
            await unread.Where(m => !m.IsReadByCustomer).ExecuteUpdateAsync(s => s.SetProperty(m => m.IsReadByCustomer, true));
        else
            await unread.Where(m => !m.IsReadByShop).ExecuteUpdateAsync(s => s.SetProperty(m => m.IsReadByShop, true));

        await Clients.Group($"conversation_{conversationId}")
            .SendAsync("MessagesRead", new { conversationId, readByAccountId = accountId });
    }

    private int? GetAccountId()
    {
        var claim = Context.User?.FindFirst("AccountId")?.Value
                 ?? Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(claim, out var id) ? id : null;
    }

    private async Task<object?> BuildMessageDto(int messageId)
    {
        var m = await _db.Messages
            .Include(x => x.Sender).ThenInclude(a => a!.Profile)
            .Include(x => x.AttachedProduct).ThenInclude(p => p!.Images)
            .Include(x => x.AttachedProduct).ThenInclude(p => p!.Variants)
            .Include(x => x.AttachedVoucher)
            .FirstOrDefaultAsync(x => x.Id == messageId);

        if (m == null) return null;

        return new
        {
            id          = m.Id,
            conversationId = m.ConversationId,
            senderId    = m.SenderId,
            senderName  = m.Sender?.Profile?.FullName ?? m.Sender?.Profile?.ShopName ?? m.Sender?.Email,
            senderAvatar= m.Sender?.Profile?.AvatarUrl ?? m.Sender?.Profile?.ShopLogoUrl,
            content     = m.Content,
            messageType = m.MessageType,
            createdAt   = m.CreatedAt.ToString("o"),
            product     = m.AttachedProduct == null ? null : new
            {
                id       = m.AttachedProduct.Id,
                name     = m.AttachedProduct.Name,
                slug     = m.AttachedProduct.Slug,
                imageUrl = m.AttachedProduct.Images.FirstOrDefault(i => i.IsPrimary)?.ImageUrl
                        ?? m.AttachedProduct.Images.FirstOrDefault()?.ImageUrl,
                price    = m.AttachedProduct.Variants.Any()
                    ? m.AttachedProduct.Variants.Min(v => v.SalePrice ?? v.Price)
                    : (decimal?)null
            },
            voucher = m.AttachedVoucher == null ? null : new
            {
                id            = m.AttachedVoucher.Id,
                code          = m.AttachedVoucher.Code,
                discountType  = m.AttachedVoucher.DiscountType,
                discountValue = m.AttachedVoucher.DiscountValue,
                endDate       = m.AttachedVoucher.EndDate.ToString("dd/MM/yyyy")
            }
        };
    }
}
