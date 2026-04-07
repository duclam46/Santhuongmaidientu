using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DOAN_BANHANG.Models;

[Table("InventoryTransactions")]
public class InventoryTransaction
{
    [Key]
    public int Id { get; set; }

    public int VariantId { get; set; }

    [ForeignKey(nameof(VariantId))]
    public ProductVariant? Variant { get; set; }

    public int QuantityChanged { get; set; }

    [Required]
    [MaxLength(50)]
    public string TransactionType { get; set; } = string.Empty; // Restock | Sale | Return | Adjust

    [MaxLength(100)]
    public string? ReferenceId { get; set; }

    [MaxLength(255)]
    public string? Note { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
