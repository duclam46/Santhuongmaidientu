using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DOAN_BANHANG.Models;

[Table("BlogPosts")]
public class BlogPost
{
    [Key]
    public int Id { get; set; }

    public int ShopId { get; set; }

    [ForeignKey(nameof(ShopId))]
    public Account? Shop { get; set; }

    [Required]
    [MaxLength(300)]
    public string Title { get; set; } = string.Empty;

    [Required]
    [MaxLength(300)]
    public string Slug { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Summary { get; set; }

    public string? Content { get; set; }

    [MaxLength(500)]
    public string? CoverImageUrl { get; set; }

    [MaxLength(30)]
    public string Status { get; set; } = "Draft"; // Draft | Published | Hidden

    public int ViewCount { get; set; } = 0;

    public bool IsDeleted { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? PublishedAt { get; set; }

    public ICollection<BlogComment> Comments { get; set; } = new List<BlogComment>();

    public ICollection<BlogPostProduct> BlogPostProducts { get; set; } = new List<BlogPostProduct>();
}
