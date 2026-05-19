using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DOAN_BANHANG.Models;

[Table("BlogPostProducts")]
public class BlogPostProduct
{
    [Key]
    public int Id { get; set; }

    public int BlogPostId { get; set; }

    [ForeignKey(nameof(BlogPostId))]
    public BlogPost? BlogPost { get; set; }

    public int ProductId { get; set; }

    [ForeignKey(nameof(ProductId))]
    public Product? Product { get; set; }

    public int SortOrder { get; set; } = 0;
}
