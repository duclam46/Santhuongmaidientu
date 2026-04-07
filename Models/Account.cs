using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DOAN_BANHANG.Models;

[Table("Accounts")]
public class Account
{
    [Key]
    public int Id { get; set; }

    [Required]
    [EmailAddress]
    [MaxLength(255)]
    public string Email { get; set; } = string.Empty;

    [MaxLength(255)]
    public string? PasswordHash { get; set; }

    [MaxLength(50)]
    public string? AuthProvider { get; set; } // local, google, facebook

    [MaxLength(255)]
    public string? AuthProviderUserId { get; set; }

    [Required]
    [MaxLength(30)]
    public string Role { get; set; } = "Customer"; // Admin | Shop | Customer

    [MaxLength(30)]
    public string Status { get; set; } = "Active"; // Active | Blocked | Pending

    public bool IsDeleted { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? LastLoginAt { get; set; }

    public UserProfile? Profile { get; set; }
}
