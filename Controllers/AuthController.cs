using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using DOAN_BANHANG.Helpers;
using DOAN_BANHANG.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace DOAN_BANHANG.Controllers;

[Route("auth")]
[IgnoreAntiforgeryToken]
public class AuthController : Controller
{
    private readonly DataContext _db;
    private readonly IPasswordHasher<Account> _passwordHasher;
    private readonly IAuthenticationSchemeProvider _schemeProvider;
    private readonly IMemoryCache _cache;
    private readonly IWebHostEnvironment _env;

    private const string SessionCartKey = "cart";
    private const string DefaultUserRole = "Customer";
    private const int MaxFailedLoginAttempts = 5;

    private static readonly Regex EmailRegex = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled);
    private static readonly Regex PhoneRegex = new(@"^(0|\+84)[0-9]{8,10}$", RegexOptions.Compiled);

    public AuthController(
        DataContext db,
        IPasswordHasher<Account> passwordHasher,
        IAuthenticationSchemeProvider schemeProvider,
        IMemoryCache cache,
        IWebHostEnvironment env)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _schemeProvider = schemeProvider;
        _cache = cache;
        _env = env;
    }

    private string GetSafeReturnUrl(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl))
            return Url.Action("Index", "Home") ?? "/";

        return Url.IsLocalUrl(returnUrl)
            ? returnUrl
            : (Url.Action("Index", "Home") ?? "/");
    }

    private static string AppendQuery(string url, string key, string value)
    {
        if (string.IsNullOrWhiteSpace(url)) url = "/";
        var separator = url.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        return url + separator + Uri.EscapeDataString(key) + "=" + Uri.EscapeDataString(value);
    }

    public sealed record LoginRequest(string? Email, string? Password, bool RememberMe = false);
    public sealed record RegisterRequest(
        string? FirstName,
        string? LastName,
        string? PhoneNumber,
        string? Email,
        string? Password,
        string? ConfirmPassword,
        bool AcceptTerms = true);
    public sealed record ForgotPasswordRequest(string? Email);
    public sealed record VerifyOtpRequest(string? Email, string? Otp);
    public sealed record ResetPasswordRequest(string? Email, string? ResetToken, string? NewPassword, string? ConfirmPassword);

    private sealed class OtpCacheEntry
    {
        public string Code { get; set; } = string.Empty;
        public int FailedAttempts { get; set; }
        public DateTime CreatedAtUtc { get; set; }
    }

    private static string? ValidatePasswordStrength(string password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < 6)
            return "Mật khẩu phải có ít nhất 6 ký tự.";
        if (!password.Any(char.IsLetter) || !password.Any(char.IsDigit))
            return "Mật khẩu phải bao gồm cả chữ cái và chữ số để đảm bảo an toàn.";
        return null;
    }

    private async Task SignInAccountAsync(Account account, bool rememberMe = false)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, account.Id.ToString()),
            new(ClaimTypes.Email, account.Email),
            new(ClaimTypes.Name, account.Email),
            new(ClaimTypes.Role, account.Role)
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        var props = new AuthenticationProperties
        {
            IsPersistent = rememberMe,
            ExpiresUtc = rememberMe ? DateTimeOffset.UtcNow.AddDays(14) : DateTimeOffset.UtcNow.AddHours(8)
        };

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            props);
    }

    private async Task MergeSessionCartToDbAsync(int accountId, CancellationToken cancellationToken)
    {
        var sessionCart = HttpContext.Session.GetJson<Dictionary<int, int>>(SessionCartKey);
        if (sessionCart is null || sessionCart.Count == 0) return;

        var cart = await _db.Carts.FirstOrDefaultAsync(c => c.AccountId == accountId, cancellationToken);
        if (cart is null)
        {
            cart = new Cart
            {
                AccountId = accountId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _db.Carts.Add(cart);
            await _db.SaveChangesAsync(cancellationToken);
        }

        var nowUtc = DateTime.UtcNow;

        foreach (var (variantId, qty) in sessionCart)
        {
            if (variantId <= 0 || qty <= 0) continue;

            var variantInfo = await _db.ProductVariants
                .AsNoTracking()
                .Where(v => v.Id == variantId && v.IsActive && !v.IsDeleted)
                .Select(v => new { AvailableStock = v.StockQuantity - v.ReservedStock })
                .FirstOrDefaultAsync(cancellationToken);

            if (variantInfo is null || variantInfo.AvailableStock <= 0) continue;

            var existing = await _db.CartItems
                .FirstOrDefaultAsync(i => i.CartId == cart.Id && i.VariantId == variantId, cancellationToken);

            if (existing is null)
            {
                _db.CartItems.Add(new CartItem
                {
                    CartId = cart.Id,
                    VariantId = variantId,
                    Quantity = Math.Min(qty, variantInfo.AvailableStock),
                    CreatedAt = nowUtc,
                    UpdatedAt = nowUtc
                });
            }
            else
            {
                existing.Quantity = Math.Min(existing.Quantity + qty, variantInfo.AvailableStock);
                existing.UpdatedAt = nowUtc;
            }
        }

        cart.UpdatedAt = nowUtc;
        await _db.SaveChangesAsync(cancellationToken);

        HttpContext.Session.Remove(SessionCartKey);
    }

    [HttpGet("login")]
    public IActionResult Login(string? returnUrl)
    {
        return RedirectToAction("Index", "Home", new { returnUrl = GetSafeReturnUrl(returnUrl), authPopup = "login" });
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest req, CancellationToken cancellationToken)
    {
        var email = (req.Email ?? string.Empty).Trim().ToLowerInvariant();
        var password = req.Password ?? string.Empty;

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            return BadRequest(new { message = "Vui lòng nhập đầy đủ Email và Mật khẩu." });

        if (!EmailRegex.IsMatch(email))
            return BadRequest(new { message = "Định dạng Email không hợp lệ." });

        // Brute-force lockout check
        var failKey = $"auth_fail_{email}";
        if (_cache.TryGetValue<int>(failKey, out var failedCount) && failedCount >= MaxFailedLoginAttempts)
        {
            return BadRequest(new { message = "Bạn đã nhập sai mật khẩu quá 5 lần. Vui lòng thử lại sau 5 phút hoặc sử dụng chức năng Quên mật khẩu." });
        }

        var account = await _db.Accounts
            .FirstOrDefaultAsync(a => a.Email.ToLower() == email && !a.IsDeleted, cancellationToken);

        if (account is null)
        {
            _cache.Set(failKey, failedCount + 1, TimeSpan.FromMinutes(5));
            return BadRequest(new { message = "Email hoặc mật khẩu không chính xác." });
        }

        if (!string.Equals(account.Status, "Active", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { message = "Tài khoản của bạn hiện đang bị tạm khóa hoặc chưa được kích hoạt." });
        }

        if (string.IsNullOrWhiteSpace(account.PasswordHash))
        {
            return BadRequest(new { message = "Tài khoản này đăng ký qua Google. Vui lòng bấm 'Tiếp tục với Google' hoặc dùng 'Quên mật khẩu' để tạo mật khẩu mới." });
        }

        var verify = _passwordHasher.VerifyHashedPassword(account, account.PasswordHash, password);
        if (verify == PasswordVerificationResult.Failed)
        {
            var nextFail = failedCount + 1;
            _cache.Set(failKey, nextFail, TimeSpan.FromMinutes(5));
            var remaining = Math.Max(0, MaxFailedLoginAttempts - nextFail);
            return BadRequest(new
            {
                message = remaining > 0
                    ? $"Email hoặc mật khẩu không chính xác (còn {remaining} lần thử)."
                    : "Bạn đã nhập sai quá 5 lần. Tài khoản tạm khóa đăng nhập trong 5 phút."
            });
        }

        _cache.Remove(failKey);

        account.LastLoginAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        await SignInAccountAsync(account, req.RememberMe);
        await MergeSessionCartToDbAsync(account.Id, cancellationToken);

        return Ok(new { ok = true, role = account.Role, message = "Đăng nhập thành công!" });
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest req, CancellationToken cancellationToken)
    {
        var firstName = (req.FirstName ?? string.Empty).Trim();
        var lastName = (req.LastName ?? string.Empty).Trim();
        var phone = (req.PhoneNumber ?? string.Empty).Trim().Replace(" ", "");
        var email = (req.Email ?? string.Empty).Trim().ToLowerInvariant();
        var password = req.Password ?? string.Empty;
        var confirm = req.ConfirmPassword ?? string.Empty;

        if (string.IsNullOrWhiteSpace(firstName) && string.IsNullOrWhiteSpace(lastName))
            return BadRequest(new { message = "Vui lòng nhập Họ và Tên của bạn." });

        if (string.IsNullOrWhiteSpace(email) || !EmailRegex.IsMatch(email))
            return BadRequest(new { message = "Vui lòng nhập địa chỉ Email hợp lệ." });

        if (!string.IsNullOrWhiteSpace(phone) && !PhoneRegex.IsMatch(phone))
            return BadRequest(new { message = "Số điện thoại Việt Nam không hợp lệ (ví dụ: 0912345678)." });

        var pwdError = ValidatePasswordStrength(password);
        if (pwdError is not null)
            return BadRequest(new { message = pwdError });

        if (!string.Equals(password, confirm, StringComparison.Ordinal))
            return BadRequest(new { message = "Mật khẩu xác nhận không khớp." });

        if (!req.AcceptTerms)
            return BadRequest(new { message = "Vui lòng đồng ý với Điều khoản dịch vụ & Chính sách bảo mật của Sellzy." });

        var existing = await _db.Accounts
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Email.ToLower() == email && !a.IsDeleted, cancellationToken);

        if (existing is not null)
            return BadRequest(new { message = "Email này đã được đăng ký. Vui lòng đăng nhập hoặc dùng chức năng Quên mật khẩu." });

        var nowUtc = DateTime.UtcNow;
        var account = new Account
        {
            Email = email,
            PasswordHash = null,
            AuthProvider = "Local",
            AuthProviderUserId = null,
            Role = DefaultUserRole,
            Status = "Active",
            IsDeleted = false,
            CreatedAt = nowUtc,
            LastLoginAt = nowUtc
        };
        account.PasswordHash = _passwordHasher.HashPassword(account, password);

        _db.Accounts.Add(account);
        await _db.SaveChangesAsync(cancellationToken);

        var fullName = string.Join(' ', new[] { firstName, lastName }.Where(s => !string.IsNullOrWhiteSpace(s)));

        _db.UserProfiles.Add(new UserProfile
        {
            AccountId = account.Id,
            FullName = string.IsNullOrWhiteSpace(fullName) ? null : fullName,
            PhoneNumber = string.IsNullOrWhiteSpace(phone) ? null : phone,
            CreatedAt = nowUtc,
            UpdatedAt = nowUtc
        });
        await _db.SaveChangesAsync(cancellationToken);

        await SignInAccountAsync(account, rememberMe: true);
        await MergeSessionCartToDbAsync(account.Id, cancellationToken);
        return Ok(new { ok = true, message = "Tạo tài khoản thành công! Chào mừng bạn đến với Sellzy." });
    }

    // ── QUÊN MẬT KHẨU → XÁC THỰC OTP 6 SỐ → ĐẶT LẠI MẬT KHẨU ──

    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest req, CancellationToken cancellationToken)
    {
        var email = (req.Email ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(email) || !EmailRegex.IsMatch(email))
            return BadRequest(new { message = "Vui lòng nhập địa chỉ Email hợp lệ." });

        var account = await _db.Accounts
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Email.ToLower() == email && !a.IsDeleted, cancellationToken);

        if (account is null)
            return BadRequest(new { message = "Không tìm thấy tài khoản nào đăng ký với Email này." });

        if (!string.Equals(account.Status, "Active", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "Tài khoản này đang bị tạm khóa, không thể khôi phục mật khẩu." });

        var otpKey = $"pwd_otp_{email}";
        if (_cache.TryGetValue<OtpCacheEntry>(otpKey, out var existingOtp) &&
            (DateTime.UtcNow - existingOtp!.CreatedAtUtc).TotalSeconds < 30)
        {
            var waitSec = (int)Math.Ceiling(30 - (DateTime.UtcNow - existingOtp.CreatedAtUtc).TotalSeconds);
            return BadRequest(new { message = $"Vui lòng chờ {waitSec} giây trước khi yêu cầu gửi lại mã OTP." });
        }

        var otpCode = RandomNumberGenerator.GetInt32(100000, 999999).ToString();
        _cache.Set(otpKey, new OtpCacheEntry
        {
            Code = otpCode,
            FailedAttempts = 0,
            CreatedAtUtc = DateTime.UtcNow
        }, TimeSpan.FromMinutes(5));

        return Ok(new
        {
            ok = true,
            email,
            message = "Mã xác thực OTP gồm 6 chữ số đã được tạo (có hiệu lực trong 5 phút).",
            devOtp = _env.IsDevelopment() ? otpCode : null
        });
    }

    [HttpPost("verify-otp")]
    public IActionResult VerifyOtp([FromBody] VerifyOtpRequest req)
    {
        var email = (req.Email ?? string.Empty).Trim().ToLowerInvariant();
        var otp = (req.Otp ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(email) || otp.Length != 6)
            return BadRequest(new { message = "Vui lòng nhập đủ 6 chữ số mã xác thực OTP." });

        var otpKey = $"pwd_otp_{email}";
        if (!_cache.TryGetValue<OtpCacheEntry>(otpKey, out var entry) || entry is null)
            return BadRequest(new { message = "Mã OTP đã hết hạn hoặc không tồn tại. Vui lòng yêu cầu mã mới." });

        if (entry.FailedAttempts >= 5)
        {
            _cache.Remove(otpKey);
            return BadRequest(new { message = "Bạn đã nhập sai mã OTP quá 5 lần. Vui lòng yêu cầu gửi lại mã mới." });
        }

        if (!string.Equals(entry.Code, otp, StringComparison.Ordinal))
        {
            entry.FailedAttempts++;
            _cache.Set(otpKey, entry, TimeSpan.FromMinutes(5));
            return BadRequest(new { message = $"Mã OTP không chính xác (còn {Math.Max(0, 5 - entry.FailedAttempts)} lần thử)." });
        }

        _cache.Remove(otpKey);
        var resetToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        _cache.Set($"pwd_reset_{email}", resetToken, TimeSpan.FromMinutes(10));

        return Ok(new
        {
            ok = true,
            resetToken,
            message = "Xác thực OTP thành công! Vui lòng thiết lập mật khẩu mới."
        });
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest req, CancellationToken cancellationToken)
    {
        var email = (req.Email ?? string.Empty).Trim().ToLowerInvariant();
        var token = (req.ResetToken ?? string.Empty).Trim();
        var newPassword = req.NewPassword ?? string.Empty;
        var confirmPassword = req.ConfirmPassword ?? string.Empty;

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(token))
            return BadRequest(new { message = "Phiên đặt lại mật khẩu không hợp lệ hoặc đã hết hạn." });

        var resetKey = $"pwd_reset_{email}";
        if (!_cache.TryGetValue<string>(resetKey, out var cachedToken) || !string.Equals(cachedToken, token, StringComparison.Ordinal))
            return BadRequest(new { message = "Phiên đặt lại mật khẩu đã hết hạn. Vui lòng thực hiện lại từ bước Quên mật khẩu." });

        var pwdError = ValidatePasswordStrength(newPassword);
        if (pwdError is not null)
            return BadRequest(new { message = pwdError });

        if (!string.Equals(newPassword, confirmPassword, StringComparison.Ordinal))
            return BadRequest(new { message = "Mật khẩu xác nhận không khớp." });

        var account = await _db.Accounts.FirstOrDefaultAsync(a => a.Email.ToLower() == email && !a.IsDeleted, cancellationToken);
        if (account is null)
            return BadRequest(new { message = "Không tìm thấy tài khoản." });

        account.PasswordHash = _passwordHasher.HashPassword(account, newPassword);
        await _db.SaveChangesAsync(cancellationToken);

        _cache.Remove(resetKey);
        _cache.Remove($"auth_fail_{email}");

        return Ok(new
        {
            ok = true,
            message = "Đặt lại mật khẩu thành công! Bạn có thể đăng nhập bằng mật khẩu mới ngay bây giờ."
        });
    }

    [HttpGet("logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Redirect("/");
    }

    [HttpGet("external-login")]
    public async Task<IActionResult> ExternalLogin([FromQuery] string provider = "Google", [FromQuery] string? returnUrl = null)
    {
        returnUrl = GetSafeReturnUrl(returnUrl);

        var scheme = await _schemeProvider.GetSchemeAsync(provider);
        if (scheme is null)
            return LocalRedirect(AppendQuery(returnUrl, "authError", $"provider_not_configured:{provider}"));

        var redirectUrl = Url.Action(nameof(ExternalLoginCallback));
        if (string.IsNullOrWhiteSpace(redirectUrl)) redirectUrl = returnUrl;

        var props = new AuthenticationProperties { RedirectUri = redirectUrl };
        props.Items["returnUrl"] = returnUrl;
        props.Items["provider"] = provider;

        return Challenge(props, provider);
    }

    [HttpGet("external-callback")]
    public async Task<IActionResult> ExternalLoginCallback([FromQuery] string? returnUrl = null, [FromQuery] string? remoteError = null, CancellationToken cancellationToken = default)
    {
        returnUrl = GetSafeReturnUrl(returnUrl);

        if (!string.IsNullOrWhiteSpace(remoteError))
            return LocalRedirect(AppendQuery(returnUrl, "authError", "external_login_failed"));

        var external = await HttpContext.AuthenticateAsync("External");
        if (!external.Succeeded || external.Principal is null)
            return LocalRedirect(AppendQuery(returnUrl, "authError", "external_info_missing"));

        if (external.Properties?.Items is not null && external.Properties.Items.TryGetValue("returnUrl", out var storedReturnUrl))
            returnUrl = GetSafeReturnUrl(storedReturnUrl);

        var provider = external.Properties?.Items is not null && external.Properties.Items.TryGetValue("provider", out var p)
            ? p
            : "Google";

        var email = external.Principal.FindFirstValue(ClaimTypes.Email)?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(email))
            return LocalRedirect(AppendQuery(returnUrl, "authError", "email_missing"));

        var providerUserId = external.Principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? external.Principal.FindFirstValue("sub")
            ?? external.Principal.FindFirstValue("id");

        if (string.IsNullOrWhiteSpace(providerUserId))
            return LocalRedirect(AppendQuery(returnUrl, "authError", "external_id_missing"));

        var account = await _db.Accounts
            .FirstOrDefaultAsync(a => a.AuthProvider == provider && a.AuthProviderUserId == providerUserId && !a.IsDeleted, cancellationToken);

        if (account is null)
        {
            account = await _db.Accounts
                .FirstOrDefaultAsync(a => a.Email.ToLower() == email && !a.IsDeleted, cancellationToken);
        }

        var nowUtc = DateTime.UtcNow;
        if (account is null)
        {
            account = new Account
            {
                Email = email,
                PasswordHash = null,
                AuthProvider = provider,
                AuthProviderUserId = providerUserId,
                Role = DefaultUserRole,
                Status = "Active",
                IsDeleted = false,
                CreatedAt = nowUtc,
                LastLoginAt = nowUtc
            };
            _db.Accounts.Add(account);
            await _db.SaveChangesAsync(cancellationToken);

            _db.UserProfiles.Add(new UserProfile
            {
                AccountId = account.Id,
                FullName = external.Principal.FindFirstValue(ClaimTypes.Name),
                CreatedAt = nowUtc,
                UpdatedAt = nowUtc
            });
            await _db.SaveChangesAsync(cancellationToken);
        }
        else
        {
            if (string.IsNullOrWhiteSpace(account.AuthProvider) || string.IsNullOrWhiteSpace(account.AuthProviderUserId))
            {
                account.AuthProvider = provider;
                account.AuthProviderUserId = providerUserId;
            }
            account.LastLoginAt = nowUtc;
            await _db.SaveChangesAsync(cancellationToken);
        }

        if (!string.Equals(account.Status, "Active", StringComparison.OrdinalIgnoreCase))
            return LocalRedirect(AppendQuery(returnUrl, "authError", "account_inactive"));

        await HttpContext.SignOutAsync("External");
        await SignInAccountAsync(account, rememberMe: true);
        await MergeSessionCartToDbAsync(account.Id, cancellationToken);
        return LocalRedirect(returnUrl);
    }

    [HttpGet("access-denied")]
    public IActionResult AccessDenied()
    {
        return View();
    }
}
