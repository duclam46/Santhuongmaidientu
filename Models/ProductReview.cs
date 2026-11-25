using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DOAN_BANHANG.Models;

[Table("ProductReviews")]
public class ProductReview
{
    [Key]
    public int Id { get; set; }

    public int ProductId { get; set; }

    [ForeignKey(nameof(ProductId))]
    public Product? Product { get; set; }

    public int AccountId { get; set; }

    [ForeignKey(nameof(AccountId))]
    public Account? Account { get; set; }

    [Range(1, 5)]
    public int Rating { get; set; }

    public string? Comment { get; set; }

    [MaxLength(1000)]
    public string? SellerReply { get; set; }

    public DateTime? RepliedAt { get; set; }

    [MaxLength(4000)]
    public string? MediaUrlsJson { get; set; }

    [MaxLength(255)]
    public string? VariantName { get; set; }

    public bool IsHidden { get; set; } = false;

    [MaxLength(500)]
    public string? ModerationNote { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
