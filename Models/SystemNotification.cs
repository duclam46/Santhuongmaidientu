using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DOAN_BANHANG.Models;

[Table("SystemNotifications")]
public class SystemNotification
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [Required]
    [MaxLength(2000)]
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// All | Role | Account
    /// </summary>
    [Required]
    [MaxLength(20)]
    public string TargetType { get; set; } = "All";

    /// <summary>
    /// Dùng khi TargetType = "Role": "Shop" | "Customer"
    /// </summary>
    [MaxLength(30)]
    public string? TargetRole { get; set; }

    /// <summary>
    /// Dùng khi TargetType = "Account"
    /// </summary>
    public int? TargetAccountId { get; set; }

    [ForeignKey(nameof(TargetAccountId))]
    public Account? TargetAccount { get; set; }

    public int SentByAdminId { get; set; }

    [ForeignKey(nameof(SentByAdminId))]
    public Account? SentByAdmin { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<SystemNotificationRead> Reads { get; set; } = new List<SystemNotificationRead>();
}
