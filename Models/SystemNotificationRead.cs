using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DOAN_BANHANG.Models;

[Table("SystemNotificationReads")]
public class SystemNotificationRead
{
    [Key]
    public int Id { get; set; }

    public int SystemNotificationId { get; set; }

    [ForeignKey(nameof(SystemNotificationId))]
    public SystemNotification? SystemNotification { get; set; }

    public int AccountId { get; set; }

    [ForeignKey(nameof(AccountId))]
    public Account? Account { get; set; }

    public DateTime ReadAt { get; set; } = DateTime.UtcNow;
}
