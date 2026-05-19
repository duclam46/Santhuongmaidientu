using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DOAN_BANHANG.Models;

/// <summary>
/// Sổ cái ghi vết mọi biến động số dư — KHÔNG BAO GIỜ XÓA.
/// Amount dương = tiền vào, âm = tiền ra.
/// </summary>
[Table("WalletTransactions")]
public class WalletTransaction
{
    [Key]
    public long TransactionId { get; set; }

    public int WalletId { get; set; }

    [ForeignKey(nameof(WalletId))]
    public Wallet? Wallet { get; set; }

    /// <summary>Dương = nạp/hoàn tiền; Âm = thanh toán.</summary>
    [Column(TypeName = "decimal(18,2)")]
    public decimal Amount { get; set; }

    /// <summary>DEPOSIT | PAYMENT | REFUND | CASHBACK</summary>
    [Required]
    [MaxLength(30)]
    public string TransactionType { get; set; } = string.Empty;

    /// <summary>Lưu OrderId hoặc VNPAY_TransactionNo để đối soát.</summary>
    [MaxLength(100)]
    public string? ReferenceId { get; set; }

    /// <summary>Mô tả lý do biến động.</summary>
    [MaxLength(500)]
    public string? Description { get; set; }

    /// <summary>Snapshot số dư sau khi thực hiện giao dịch này.</summary>
    [Column(TypeName = "decimal(18,2)")]
    public decimal BalanceAfter { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
