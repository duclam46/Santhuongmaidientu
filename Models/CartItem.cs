using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DOAN_BANHANG.Models;

[Table("CartItems")]
public class CartItem
{
    [Key]
    public int Id { get; set; }

    public int CartId { get; set; }

    [ForeignKey(nameof(CartId))]
    public Cart? Cart { get; set; }

    public int VariantId { get; set; }

    [ForeignKey(nameof(VariantId))]
    public ProductVariant? Variant { get; set; }

    [Range(1, int.MaxValue)]
    public int Quantity { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
