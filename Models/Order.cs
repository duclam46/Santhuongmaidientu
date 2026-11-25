using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DOAN_BANHANG.Models;

[Table("Orders")]
public class Order
{
    [Key]
    public int Id { get; set; }

    public int CustomerId { get; set; }

    [ForeignKey(nameof(CustomerId))]
    public Account? Customer { get; set; }

    public int ShopId { get; set; }

    [ForeignKey(nameof(ShopId))]
    public Account? Shop { get; set; }

    [Required]
    [MaxLength(50)]
    public string OrderCode { get; set; } = string.Empty;

    [Column(TypeName = "decimal(18,2)")]
    public decimal SubTotal { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal ShippingFee { get; set; }

    public int? VoucherId { get; set; }

    [ForeignKey(nameof(VoucherId))]
    public Voucher? Voucher { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal DiscountAmount { get; set; } = 0m;

    [Column(TypeName = "decimal(18,2)")]
    public decimal ShopDiscountAmount { get; set; } = 0m;

    [Column(TypeName = "decimal(18,2)")]
    public decimal PlatformDiscountAmount { get; set; } = 0m;

    [Column(TypeName = "decimal(18,2)")]
    public decimal FinalAmount { get; set; }

    [NotMapped]
    public decimal VatAmount { get; set; } = 0m;

    [NotMapped]
    public decimal PlatformFee { get; set; } = 0m;

    [NotMapped]
    public decimal SellerTaxAmount { get; set; } = 0m;

    [NotMapped]
    public decimal PayoutAmount { get; set; } = 0m;

    [Required]
    [MaxLength(30)]
    public string OrderStatus { get; set; } = "Pending";

    [Required]
    [MaxLength(30)]
    public string PaymentMethod { get; set; } = "COD";

    [Required]
    [MaxLength(30)]
    public string PaymentStatus { get; set; } = "Pending";

    [Required]
    [MaxLength(150)]
    public string RecipientName { get; set; } = string.Empty;

    [Required]
    [MaxLength(20)]
    public string RecipientPhone { get; set; } = string.Empty;

    [Required]
    [MaxLength(500)]
    public string ShippingAddress { get; set; } = string.Empty;

    // Shipping destination info used for GHN create-order API.
    public int? ShippingProvinceId { get; set; }

    public int? ShippingDistrictId { get; set; }

    [MaxLength(50)]
    public string? ShippingWardCode { get; set; }

    [MaxLength(255)]
    public string? ShippingStreetAddress { get; set; }

    [MaxLength(100)]
    public string? TrackingCode { get; set; }

    public bool IsDigitalOrder { get; set; } = false;

    public bool RequiresVatInvoice { get; set; } = false;

    [MaxLength(255)]
    public string? CompanyName { get; set; }

    [MaxLength(50)]
    public string? TaxCode { get; set; }

    [MaxLength(500)]
    public string? CompanyAddress { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<OrderItem> Items { get; set; } = new List<OrderItem>();
}
