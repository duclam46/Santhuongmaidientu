using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DOAN_BANHANG.Models;

[Table("Messages")]
public class Message
{
    [Key]
    public int Id { get; set; }

    public int ConversationId { get; set; }

    [ForeignKey(nameof(ConversationId))]
    public Conversation? Conversation { get; set; }

    public int SenderId { get; set; }

    [ForeignKey(nameof(SenderId))]
    public Account? Sender { get; set; }

    [MaxLength(2000)]
    public string? Content { get; set; }

    /// <summary>
    /// text | product_card | voucher_card | system
    /// </summary>
    [MaxLength(30)]
    public string MessageType { get; set; } = "text";

    // Đính kèm sản phẩm (khi MessageType = product_card)
    public int? AttachedProductId { get; set; }

    [ForeignKey(nameof(AttachedProductId))]
    public Product? AttachedProduct { get; set; }

    // Đính kèm voucher (khi MessageType = voucher_card)
    public int? AttachedVoucherId { get; set; }

    [ForeignKey(nameof(AttachedVoucherId))]
    public Voucher? AttachedVoucher { get; set; }

    public bool IsReadByCustomer { get; set; } = false;

    public bool IsReadByShop { get; set; } = false;

    public bool IsDeleted { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
