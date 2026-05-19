using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DOAN_BANHANG.Models;
using Microsoft.AspNetCore.SignalR;
using DOAN_BANHANG.Hubs;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace DOAN_BANHANG.Areas.Admin.Controllers;

[Area("Admin")]
[Microsoft.AspNetCore.Authorization.Authorize(Roles = "Shop")]
public class ChatController : Controller
{
    private readonly DataContext _db;
    private readonly IHubContext<ChatHub> _hub;

    public ChatController(DataContext db, IHubContext<ChatHub> hub)
    {
        _db = db;
        _hub = hub;
    }

    private int? GetAccountId()
    {
        var claim = User.FindFirst("AccountId")?.Value ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(claim, out var id) ? id : null;
    }

    // GET /Admin/Chat — Shop inbox
    [HttpGet]
    public async Task<IActionResult> Index(int? id)
    {
        var accountId = GetAccountId();
        if (!accountId.HasValue) return RedirectToAction("Login", "Auth", new { area = "" });

        var isAdmin = User.IsInRole("Admin");
        if (!User.IsInRole("Shop") && !isAdmin) return Forbid();

        var conversationsQuery = _db.Conversations.AsQueryable();
        if (!isAdmin) {
            conversationsQuery = conversationsQuery.Where(c => c.ShopId == accountId);
        }

        var conversations = await conversationsQuery
            .Include(c => c.Customer).ThenInclude(a => a!.Profile)
            .Include(c => c.Messages.Where(m => !m.IsDeleted).OrderByDescending(m => m.CreatedAt).Take(1))
            .OrderByDescending(c => c.LastMessageAt)
            .ToListAsync();

        ViewBag.CurrentAccountId = accountId;

        if (conversations.Any())
        {
            var activeConvId = id ?? conversations.First().Id;
            var activeConvQuery = _db.Conversations.AsQueryable();
            if (!isAdmin) {
                activeConvQuery = activeConvQuery.Where(c => c.ShopId == accountId);
            }
            var activeConv = await activeConvQuery
                .Include(c => c.Customer).ThenInclude(a => a!.Profile)
                .Include(c => c.Shop).ThenInclude(a => a!.Profile)
                .FirstOrDefaultAsync(c => c.Id == activeConvId);

            if (activeConv != null)
            {
                ViewBag.ActiveConversation = activeConv;

                var messages = await _db.Messages
                    .Where(m => m.ConversationId == activeConv.Id && !m.IsDeleted)
                    .Include(m => m.Sender).ThenInclude(a => a!.Profile)
                    .Include(m => m.AttachedProduct).ThenInclude(p => p!.Images)
                    .Include(m => m.AttachedVoucher)
                    .OrderBy(m => m.CreatedAt)
                    .ToListAsync();
                ViewBag.Messages = messages;

                var vouchers = await _db.Vouchers
                    .Where(v => v.ShopId == accountId && v.IsActive && v.EndDate > DateTime.UtcNow)
                    .OrderByDescending(v => v.CreatedAt)
                    .ToListAsync();
                ViewBag.Vouchers = vouchers;
            }
        }

        return View(conversations);
    }

    [HttpGet]
    public async Task<IActionResult> GetMessages(int id)
    {
        var accountId = GetAccountId();
        if (!accountId.HasValue) return Unauthorized();

        var isAdmin = User.IsInRole("Admin");
        var conv = await _db.Conversations
            .Include(c => c.Customer).ThenInclude(a => a!.Profile)
            .Include(c => c.Shop).ThenInclude(a => a!.Profile)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (conv == null || (!isAdmin && conv.ShopId != accountId)) return Forbid();

        var messages = await _db.Messages
            .Where(m => m.ConversationId == id && !m.IsDeleted)
            .Include(m => m.Sender).ThenInclude(a => a!.Profile)
            .Include(m => m.AttachedProduct).ThenInclude(p => p!.Images)
            .Include(m => m.AttachedVoucher)
            .OrderBy(m => m.CreatedAt)
            .Select(m => new {
                id = m.Id,
                conversationId = m.ConversationId,
                senderId = m.SenderId,
                content = m.Content,
                messageType = m.MessageType,
                createdAt = m.CreatedAt,
                senderAvatar = m.Sender.Profile != null ? (m.Sender.Profile.AvatarUrl ?? m.Sender.Profile.ShopLogoUrl) : null,
                senderName = m.Sender.Profile != null ? (m.Sender.Profile.FullName ?? m.Sender.Profile.ShopName) : m.Sender.Email,
                product = m.AttachedProduct == null ? null : new {
                    id = m.AttachedProduct.Id,
                    name = m.AttachedProduct.Name,
                    imageUrl = m.AttachedProduct.Images.FirstOrDefault() != null ? m.AttachedProduct.Images.FirstOrDefault().ImageUrl : null,
                    price = m.AttachedProduct.Variants.FirstOrDefault() != null ? (m.AttachedProduct.Variants.FirstOrDefault().SalePrice ?? m.AttachedProduct.Variants.FirstOrDefault().Price) : 0
                },
                voucher = m.AttachedVoucher == null ? null : new {
                    id = m.AttachedVoucher.Id,
                    code = m.AttachedVoucher.Code,
                    discountType = m.AttachedVoucher.DiscountType,
                    discountValue = m.AttachedVoucher.DiscountValue,
                    endDate = m.AttachedVoucher.EndDate.ToString("dd/MM/yyyy")
                }
            })
            .ToListAsync();

        string custName = "Người dùng ẩn";
        if (conv.Customer != null) {
            if (conv.Customer.Profile != null && !string.IsNullOrWhiteSpace(conv.Customer.Profile.FullName)) {
                custName = conv.Customer.Profile.FullName;
            } else if (!string.IsNullOrWhiteSpace(conv.Customer.Email)) {
                custName = conv.Customer.Email.Split('@')[0];
            }
        }

        return Json(new { 
            conversation = new { 
                id = conv.Id, 
                customerName = custName,
                customerAvatar = conv.Customer?.Profile?.AvatarUrl 
            }, 
            messages 
        });
    }

}

