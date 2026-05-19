using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DOAN_BANHANG.Models;

[Table("SystemSettings")]
public class SystemSetting
{
    [Key]
    [MaxLength(100)]
    public string Id { get; set; } = string.Empty; // Setting Key

    [Required]
    public string Value { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; set; }
    
    [MaxLength(50)]
    public string Type { get; set; } = "string"; // string, number, boolean
    
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
