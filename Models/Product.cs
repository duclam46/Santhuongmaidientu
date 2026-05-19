using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DOAN_BANHANG.Models;

[Table("Products")]
public class Product
{
    [Key]
    public int Id { get; set; }

    public int ShopId { get; set; }

    [ForeignKey(nameof(ShopId))]
    public Account? ShopAccount { get; set; }

    public int CategoryId { get; set; }

    [ForeignKey(nameof(CategoryId))]
    public Category? Category { get; set; }

    [Required]
    [MaxLength(255)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(200)]
    public string Slug { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? ShortDescription { get; set; }

    [Column(TypeName = "decimal(3,2)")]
    [Range(typeof(decimal), "0", "5")]
    public decimal AverageRating { get; set; } = 0m;

    public int ReviewCount { get; set; } = 0;

    [MaxLength(30)]
    public string Status { get; set; } = "Active"; // Draft | Active | Hidden

    public bool IsNewArrival { get; set; } = false;

    public string? Description { get; set; }

    [NotMapped]
    public decimal VatRate { get; set; } = 0.08m;


    public bool IsDeleted { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<ProductImage> Images { get; set; } = new List<ProductImage>();

    public ICollection<ProductVariant> Variants { get; set; } = new List<ProductVariant>();

    public ICollection<SizeGuide> SizeGuides { get; set; } = new List<SizeGuide>();

    public ICollection<ProductReview> Reviews { get; set; } = new List<ProductReview>();
}
