using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DOAN_BANHANG.Models;

[Table("Wallets")]
public class Wallet
{
    [Key]
    public int WalletId { get; set; }

    /// <summary>FK to Accounts. Unique — mỗi user chỉ có 1 ví.</summary>
    public int UserId { get; set; }

    [ForeignKey(nameof(UserId))]
    public Account? User { get; set; }

    /// <summary>Số dư hiện tại (VND). Không bao giờ âm.</summary>
    [Column(TypeName = "decimal(18,2)")]
    public decimal Balance { get; set; } = 0m;

    /// <summary>1 = Active, 0 = Locked (khoá khi phát hiện gian lận).</summary>
    public int Status { get; set; } = 1;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<WalletTransaction> Transactions { get; set; } = new List<WalletTransaction>();
}
