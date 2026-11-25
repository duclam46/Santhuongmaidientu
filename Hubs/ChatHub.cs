using System.Collections.Concurrent;
using System.Security.Claims;
using DOAN_BANHANG.Helpers;
using DOAN_BANHANG.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace DOAN_BANHANG.Hubs;

public class ChatHub : Hub
{
    private readonly DataContext _db;

    // Theo dõi trạng thái Online theo AccountId (đếm số kết nối đang mở)
    private static readonly ConcurrentDictionary<int, int> OnlineConnections = new();

    public static bool IsAccountOnline(int accountId)
        => OnlineConnections.TryGetValue(accountId, out var count) && count > 0;

    public static HashSet<int> GetOnlineAccountIds()
        => OnlineConnections.Where(kv => kv.Value > 0).Select(kv => kv.Key).ToHashSet();

    public ChatHub(DataContext db)
    {
        _db = db;
    }

    public override async Task OnConnectedAsync()
    {
        var accountId = GetAccountId();
        if (accountId.HasValue)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"user_{accountId}");

            var newCount = OnlineConnections.AddOrUpdate(accountId.Value, 1, (_, c) => c + 1);
            if (newCount == 1)
            {
                await Clients.All.SendAsync("UserPresenceChanged", new { accountId = accountId.Value, isOnline = true });
            }

            var account = await _db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == accountId.Value);
            if (account?.Role == "Shop" || account?.Role == "Admin")
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, $"shop_{accountId}");
            }
        }
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var accountId = GetAccountId();
        if (accountId.HasValue)
        {
            var remaining = OnlineConnections.AddOrUpdate(accountId.Value, 0, (_, c) => Math.Max(0, c - 1));
            if (remaining == 0)
            {
                OnlineConnections.TryRemove(accountId.Value, out _);
                await Clients.All.SendAsync("UserPresenceChanged", new { accountId = accountId.Value, isOnline = false });
            }
        }
        await base.OnDisconnectedAsync(exception);
    }

    public async Task JoinConversation(int conversationId)
    {
        var accountId = GetAccountId();
        if (!accountId.HasValue) return;

        var conv = await _db.Conversations.FindAsync(conversationId);
        if (conv == null) return;
        var isAdmin = Context.User?.IsInRole("Admin") ?? false;
        if (conv.CustomerId != accountId && conv.ShopId != accountId && !isAdmin) return;

        await Groups.AddToGroupAsync(Context.ConnectionId, $"conversation_{conversationId}");
        await MarkReadInternal(conversationId, accountId.Value, conv);
    }

    public async Task LeaveConversation(int conversationId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"conversation_{conversationId}");
    }

    public async Task SendTyping(int conversationId, bool isTyping)
    {
        var accountId = GetAccountId();
        if (!accountId.HasValue) return;

        await Clients.OthersInGroup($"conversation_{conversationId}")
            .SendAsync("UserTyping", new { conversationId, accountId = accountId.Value, isTyping });
    }

    public async Task SendTextMessage(int conversationId, string content)
    {
        var accountId = GetAccountId();
        if (!accountId.HasValue || string.IsNullOrWhiteSpace(content)) return;

        var trimmed = content.Trim();
        if (trimmed.Length > 2000) trimmed = trimmed[..2000];

        var conv = await _db.Conversations.FindAsync(conversationId);
        var isAdmin = Context.User?.IsInRole("Admin") ?? false;
        if (conv == null || (conv.CustomerId != accountId && conv.ShopId != accountId && !isAdmin)) return;

        var senderIsCustomer = conv.CustomerId == accountId;
        var msg = new Message
        {
            ConversationId = conversationId,
            SenderId = accountId.Value,
            Content = trimmed,
            MessageType = "text",
            IsReadByCustomer = senderIsCustomer,
            IsReadByShop = !senderIsCustomer,
            CreatedAt = DateTime.UtcNow
        };
        _db.Messages.Add(msg);
        conv.LastMessageAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        await BroadcastSavedMessageAsync(msg.Id, conv, accountId.Value, trimmed);

        if (senderIsCustomer)
        {
            await TrySendShopAutoReplyAsync(conv);
        }
    }

    private async Task TrySendShopAutoReplyAsync(Conversation conv)
    {
        try
        {
            var autoReply = await _db.UserProfiles
                .AsNoTracking()
                .Where(p => p.AccountId == conv.ShopId)
                .Select(p => p.AutoReplyMessage)
                .FirstOrDefaultAsync();

            if (string.IsNullOrWhiteSpace(autoReply)) return;

            var recentShopMsg = await _db.Messages
                .AsNoTracking()
                .AnyAsync(m => m.ConversationId == conv.Id
                            && m.SenderId == conv.ShopId
                            && !m.IsDeleted
                            && m.CreatedAt >= DateTime.UtcNow.AddHours(-12));

            if (recentShopMsg) return;

            var replyText = autoReply.Trim();
            var autoMsg = new Message
            {
                ConversationId = conv.Id,
                SenderId = conv.ShopId,
                Content = replyText,
                MessageType = "text",
                IsReadByCustomer = false,
                IsReadByShop = true,
                CreatedAt = DateTime.UtcNow.AddMilliseconds(250)
            };
            _db.Messages.Add(autoMsg);
            conv.LastMessageAt = autoMsg.CreatedAt;
            await _db.SaveChangesAsync();

            await BroadcastSavedMessageAsync(autoMsg.Id, conv, conv.ShopId, replyText);
        }
        catch
        {
            // Non-blocking auto-reply
        }
    }

    public async Task SendProductCard(int conversationId, int productId, string? message)
    {
        var accountId = GetAccountId();
        if (!accountId.HasValue) return;

        var conv = await _db.Conversations.FindAsync(conversationId);
        var isAdmin = Context.User?.IsInRole("Admin") ?? false;
        if (conv == null || (conv.CustomerId != accountId && conv.ShopId != accountId && !isAdmin)) return;

        var product = await _db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == productId && !p.IsDeleted);
        if (product == null) return;

        var senderIsCustomer = conv.CustomerId == accountId;
        var text = string.IsNullOrWhiteSpace(message)
            ? (senderIsCustomer ? "Tôi muốn hỏi về sản phẩm này." : $"Shop gửi bạn tham khảo sản phẩm: {product.Name}")
            : message.Trim();

        var msg = new Message
        {
            ConversationId = conversationId,
            SenderId = accountId.Value,
            Content = text,
            MessageType = "product_card",
            AttachedProductId = productId,
            IsReadByCustomer = senderIsCustomer,
            IsReadByShop = !senderIsCustomer,
            CreatedAt = DateTime.UtcNow
        };
        _db.Messages.Add(msg);
        conv.LastMessageAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        await BroadcastSavedMessageAsync(msg.Id, conv, accountId.Value, text);

        if (senderIsCustomer)
        {
            await TrySendShopAutoReplyAsync(conv);
        }
    }

    public async Task SendVoucherCard(int conversationId, int voucherId, string? message)
    {
        var accountId = GetAccountId();
        if (!accountId.HasValue) return;

        var conv = await _db.Conversations.FindAsync(conversationId);
        var isAdmin = Context.User?.IsInRole("Admin") ?? false;
        if (conv == null || (conv.ShopId != accountId && !isAdmin)) return;

        var voucher = await _db.Vouchers.AsNoTracking().FirstOrDefaultAsync(v => v.Id == voucherId && v.IsActive);
        if (voucher == null || (voucher.ShopId.HasValue && voucher.ShopId != conv.ShopId)) return;

        var text = string.IsNullOrWhiteSpace(message)
            ? $"Mã giảm giá dành riêng cho bạn: {voucher.Code}"
            : message.Trim();

        var msg = new Message
        {
            ConversationId = conversationId,
            SenderId = accountId.Value,
            Content = text,
            MessageType = "voucher_card",
            AttachedVoucherId = voucherId,
            IsReadByCustomer = false,
            IsReadByShop = true,
            CreatedAt = DateTime.UtcNow
        };
        _db.Messages.Add(msg);
        conv.LastMessageAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        await BroadcastSavedMessageAsync(msg.Id, conv, accountId.Value, text);
    }

    public async Task SendMediaMessage(int conversationId, string mediaUrl, string mediaType)
    {
        var accountId = GetAccountId();
        if (!accountId.HasValue || string.IsNullOrWhiteSpace(mediaUrl)) return;

        // Chỉ chấp nhận đường dẫn upload nội bộ hợp lệ
        var cleanUrl = mediaUrl.Trim();
        if (!cleanUrl.StartsWith("/uploads/chat/", StringComparison.OrdinalIgnoreCase)) return;

        var conv = await _db.Conversations.FindAsync(conversationId);
        var isAdmin = Context.User?.IsInRole("Admin") ?? false;
        if (conv == null || (conv.CustomerId != accountId && conv.ShopId != accountId && !isAdmin)) return;

        var senderIsCustomer = conv.CustomerId == accountId;
        var msg = new Message
        {
            ConversationId = conversationId,
            SenderId = accountId.Value,
            Content = cleanUrl,
            MessageType = mediaType == "video" ? "video" : "image",
            IsReadByCustomer = senderIsCustomer,
            IsReadByShop = !senderIsCustomer,
            CreatedAt = DateTime.UtcNow
        };
        _db.Messages.Add(msg);
        conv.LastMessageAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var preview = mediaType == "video" ? "[Video]" : "[Hình ảnh]";
        await BroadcastSavedMessageAsync(msg.Id, conv, accountId.Value, preview);
    }

    public async Task RecallMessage(int messageId)
    {
        var accountId = GetAccountId();
        if (!accountId.HasValue) return;

        var msg = await _db.Messages.FirstOrDefaultAsync(m => m.Id == messageId && !m.IsDeleted);
        if (msg == null || msg.SenderId != accountId.Value) return;

        // Chỉ cho phép thu hồi trong vòng 5 phút
        if ((DateTime.UtcNow - msg.CreatedAt).TotalMinutes > 5) return;

        msg.IsDeleted = true;
        await _db.SaveChangesAsync();

        await Clients.Group($"conversation_{msg.ConversationId}")
            .SendAsync("MessageRecalled", new { conversationId = msg.ConversationId, messageId = msg.Id });
    }

    public async Task MarkRead(int conversationId)
    {
        var accountId = GetAccountId();
        if (!accountId.HasValue) return;

        var conv = await _db.Conversations.FindAsync(conversationId);
        if (conv == null) return;
        if (conv.CustomerId != accountId.Value && conv.ShopId != accountId.Value) return;

        await MarkReadInternal(conversationId, accountId.Value, conv);
    }

    // ─── Internal helpers ───────────────────────────────────────────────────

    private async Task BroadcastSavedMessageAsync(int messageId, Conversation conv, int senderId, string previewText)
    {
        var saved = await BuildMessageDtoAsync(_db, messageId);
        if (saved == null) return;

        await Clients.Group($"conversation_{conv.Id}").SendAsync("ReceiveMessage", saved);

        if (conv.CustomerId == senderId)
        {
            await Clients.Group($"shop_{conv.ShopId}")
                .SendAsync("NewMessageFromCustomer", new { conversationId = conv.Id, content = previewText, createdAt = DateTime.UtcNow.ToString("o") });
        }
        else
        {
            await Clients.Group($"user_{conv.CustomerId}")
                .SendAsync("NewMessageFromShop", new { conversationId = conv.Id, content = previewText, createdAt = DateTime.UtcNow.ToString("o") });
        }
    }

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

    public static async Task<object?> BuildMessageDtoAsync(DataContext db, int messageId)
    {
        var m = await db.Messages
            .AsNoTracking()
            .Include(x => x.Sender).ThenInclude(a => a!.Profile)
            .Include(x => x.AttachedProduct).ThenInclude(p => p!.Images)
            .Include(x => x.AttachedProduct).ThenInclude(p => p!.Variants)
            .Include(x => x.AttachedVoucher)
            .FirstOrDefaultAsync(x => x.Id == messageId);

        if (m == null) return null;

        var prodImg = m.AttachedProduct?.Images
            .OrderByDescending(i => i.IsPrimary)
            .Select(i => i.ImageUrl)
            .FirstOrDefault();

        var activePrices = m.AttachedProduct?.Variants
            .Where(v => v.IsActive && !v.IsDeleted)
            .Select(v => v.SalePrice ?? v.Price)
            .ToList();

        return new
        {
            id = m.Id,
            conversationId = m.ConversationId,
            senderId = m.SenderId,
            senderName = m.Sender?.Profile?.FullName ?? m.Sender?.Profile?.ShopName ?? m.Sender?.Email,
            senderAvatar = ImageUrlHelper.NormalizeStaticFileUrl(m.Sender?.Profile?.AvatarUrl ?? m.Sender?.Profile?.ShopLogoUrl),
            content = m.Content,
            messageType = m.MessageType,
            isReadByCustomer = m.IsReadByCustomer,
            isReadByShop = m.IsReadByShop,
            createdAt = m.CreatedAt.ToString("o"),
            product = m.AttachedProduct == null ? null : new
            {
                id = m.AttachedProduct.Id,
                name = m.AttachedProduct.Name,
                slug = m.AttachedProduct.Slug,
                imageUrl = ImageUrlHelper.NormalizeStaticFileUrl(prodImg),
                price = activePrices != null && activePrices.Count > 0 ? activePrices.Min() : 0m
            },
            voucher = m.AttachedVoucher == null ? null : new
            {
                id = m.AttachedVoucher.Id,
                code = m.AttachedVoucher.Code,
                discountType = m.AttachedVoucher.DiscountType,
                discountValue = m.AttachedVoucher.DiscountValue,
                minOrderValue = m.AttachedVoucher.MinOrderValue,
                endDate = m.AttachedVoucher.EndDate.AddHours(7).ToString("dd/MM/yyyy")
            }
        };
    }
}
