using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DOAN_BANHANG.Models;

[Table("BlogComments")]
public class BlogComment
{
    [Key]
    public int Id { get; set; }

    public int BlogPostId { get; set; }

    [ForeignKey(nameof(BlogPostId))]
    public BlogPost? BlogPost { get; set; }

    public int AccountId { get; set; }

    [ForeignKey(nameof(AccountId))]
    public Account? Account { get; set; }

    public int? ParentCommentId { get; set; }

    [ForeignKey(nameof(ParentCommentId))]
    public BlogComment? ParentComment { get; set; }

    [Required]
    [MaxLength(1000)]
    public string Content { get; set; } = string.Empty;

    public bool IsDeleted { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<BlogComment> Replies { get; set; } = new List<BlogComment>();
}
