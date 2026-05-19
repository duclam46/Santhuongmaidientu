using System.Security.Claims;
using DOAN_BANHANG.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DOAN_BANHANG.Helpers;

namespace DOAN_BANHANG.Controllers;

[Route("auth")]
[IgnoreAntiforgeryToken]
public class AuthController : Controller
{
    private readonly DataContext _db;
    private readonly IPasswordHasher<Account> _passwordHasher;
    private readonly IAuthenticationSchemeProvider _schemeProvider;

    private const string SessionCartKey = "cart";

    private const string DefaultUserRole = "Customer";

    public AuthController(
        DataContext db,
        IPasswordHasher<Account> passwordHasher,
        IAuthenticationSchemeProvider schemeProvider)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _schemeProvider = schemeProvider;
    }

    private string GetSafeReturnUrl(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl))
        {
            return Url.Action("Index", "Home") ?? "/";
        }

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

    public sealed record LoginRequest(string? Email, string? Password);
    public sealed record RegisterRequest(string? FirstName, string? LastName, string? Email, string? Password, string? ConfirmPassword);

    private async Task SignInAccountAsync(Account account)
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

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties { IsPersistent = false });
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

            var variantOk = await _db.ProductVariants
                .AsNoTracking()
                .AnyAsync(v => v.Id == variantId && v.IsActive && !v.IsDeleted, cancellationToken);

            if (!variantOk) continue;

            var existing = await _db.CartItems
                .FirstOrDefaultAsync(i => i.CartId == cart.Id && i.VariantId == variantId, cancellationToken);

            if (existing is null)
            {
                _db.CartItems.Add(new CartItem
                {
                    CartId = cart.Id,
                    VariantId = variantId,
                    Quantity = qty,
                    CreatedAt = nowUtc,
                    UpdatedAt = nowUtc
                });
            }
            else
            {
                existing.Quantity += qty;
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
        // Redirect to Home with authPopup=login to trigger the login modal/sidebar
        // We include returnUrl so that after login, the user can be redirected back.
        return RedirectToAction("Index", "Home", new { returnUrl = GetSafeReturnUrl(returnUrl), authPopup = "login" });
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest req, CancellationToken cancellationToken)
    {
        var email = (req.Email ?? string.Empty).Trim();
        var password = req.Password ?? string.Empty;

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            return BadRequest(new { message = "Email và mật khẩu là bắt buộc." });
        }

        var account = await _db.Accounts
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Email == email && !a.IsDeleted, cancellationToken);

        if (account is null)
        {
            return BadRequest(new { message = "Email hoặc mật khẩu không đúng." });
        }

        if (!string.Equals(account.Status, "Active", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { message = "Tài khoản đang bị khóa hoặc không hoạt động." });
        }

        if (string.IsNullOrWhiteSpace(account.PasswordHash))
        {
            return BadRequest(new { message = "Tài khoản này chưa đặt mật khẩu. Hãy đăng nhập bằng Google." });
        }

        var verify = _passwordHasher.VerifyHashedPassword(account, account.PasswordHash, password);
        if (verify == PasswordVerificationResult.Failed)
        {
            return BadRequest(new { message = "Email hoặc mật khẩu không đúng." });
        }

        // Re-fetch tracked entity for claims (Id/Role already known), avoid attaching AsNoTracking entity.
        var tracked = await _db.Accounts.FirstAsync(a => a.Id == account.Id, cancellationToken);
        await SignInAccountAsync(tracked);
        await MergeSessionCartToDbAsync(tracked.Id, cancellationToken);

        return Ok(new { ok = true });
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest req, CancellationToken cancellationToken)
    {
        var email = (req.Email ?? string.Empty).Trim();
        var password = req.Password ?? string.Empty;
        var confirm = req.ConfirmPassword ?? string.Empty;

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            return BadRequest(new { message = "Email và mật khẩu là bắt buộc." });
        }

        if (!string.Equals(password, confirm, StringComparison.Ordinal))
        {
            return BadRequest(new { message = "Mật khẩu xác nhận không khớp." });
        }

        var existing = await _db.Accounts
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Email == email && !a.IsDeleted, cancellationToken);

        if (existing is not null)
        {
            return BadRequest(new { message = "Email này đã được đăng ký." });
        }

        var account = new Account
        {
            Email = email,
            PasswordHash = null,
            AuthProvider = null,
            AuthProviderUserId = null,
            Role = DefaultUserRole,
            Status = "Active",
            IsDeleted = false,
            CreatedAt = DateTime.UtcNow
        };
        account.PasswordHash = _passwordHasher.HashPassword(account, password);

        _db.Accounts.Add(account);
        await _db.SaveChangesAsync(cancellationToken);

        var fullName = string.Join(' ', new[] { req.FirstName, req.LastName }
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s!.Trim()));

        _db.UserProfiles.Add(new UserProfile
        {
            AccountId = account.Id,
            FullName = string.IsNullOrWhiteSpace(fullName) ? null : fullName,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync(cancellationToken);

        await SignInAccountAsync(account);
        await MergeSessionCartToDbAsync(account.Id, cancellationToken);
        return Ok(new { ok = true });
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
        var providerConfigured = scheme is not null;
        if (!providerConfigured)
        {
            return LocalRedirect(AppendQuery(returnUrl, "authError", $"provider_not_configured:{provider}"));
        }

        var redirectUrl = Url.Action(nameof(ExternalLoginCallback));
        if (string.IsNullOrWhiteSpace(redirectUrl)) redirectUrl = returnUrl;

        var props = new AuthenticationProperties
        {
            RedirectUri = redirectUrl
        };
        props.Items["returnUrl"] = returnUrl;
        props.Items["provider"] = provider;

        return Challenge(props, provider);
    }

    [HttpGet("external-callback")]
    public async Task<IActionResult> ExternalLoginCallback([FromQuery] string? returnUrl = null, [FromQuery] string? remoteError = null, CancellationToken cancellationToken = default)
    {
        returnUrl = GetSafeReturnUrl(returnUrl);

        if (!string.IsNullOrWhiteSpace(remoteError))
        {
            return LocalRedirect(AppendQuery(returnUrl, "authError", "external_login_failed"));
        }

        var external = await HttpContext.AuthenticateAsync("External");
        if (!external.Succeeded || external.Principal is null)
        {
            return LocalRedirect(AppendQuery(returnUrl, "authError", "external_info_missing"));
        }

        // If the challenge stored returnUrl/provider, prefer that.
        if (external.Properties?.Items is not null)
        {
            if (external.Properties.Items.TryGetValue("returnUrl", out var storedReturnUrl))
            {
                returnUrl = GetSafeReturnUrl(storedReturnUrl);
            }
        }

        var provider = external.Properties?.Items is not null && external.Properties.Items.TryGetValue("provider", out var p)
            ? p
            : "Google";

        var email = external.Principal.FindFirstValue(ClaimTypes.Email);
        if (string.IsNullOrWhiteSpace(email))
        {
            return LocalRedirect(AppendQuery(returnUrl, "authError", "email_missing"));
        }

        var providerUserId = external.Principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? external.Principal.FindFirstValue("sub")
            ?? external.Principal.FindFirstValue("id");

        if (string.IsNullOrWhiteSpace(providerUserId))
        {
            return LocalRedirect(AppendQuery(returnUrl, "authError", "external_id_missing"));
        }

        // Find by provider link first, then by email.
        var account = await _db.Accounts
            .FirstOrDefaultAsync(a => a.AuthProvider == provider && a.AuthProviderUserId == providerUserId && !a.IsDeleted, cancellationToken: default);

        if (account is null)
        {
            account = await _db.Accounts
                .FirstOrDefaultAsync(a => a.Email == email && !a.IsDeleted, cancellationToken: default);
        }

        if (account is null)
        {
            account = new Account
            {
                Email = email.Trim(),
                PasswordHash = null,
                AuthProvider = provider,
                AuthProviderUserId = providerUserId,
                Role = DefaultUserRole,
                Status = "Active",
                IsDeleted = false,
                CreatedAt = DateTime.UtcNow
            };
            _db.Accounts.Add(account);
            await _db.SaveChangesAsync();

            _db.UserProfiles.Add(new UserProfile
            {
                AccountId = account.Id,
                FullName = external.Principal.FindFirstValue(ClaimTypes.Name),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();
        }
        else
        {
            // Link provider if not linked yet.
            if (string.IsNullOrWhiteSpace(account.AuthProvider) || string.IsNullOrWhiteSpace(account.AuthProviderUserId))
            {
                account.AuthProvider = provider;
                account.AuthProviderUserId = providerUserId;
                await _db.SaveChangesAsync();
            }
        }

        if (!string.Equals(account.Status, "Active", StringComparison.OrdinalIgnoreCase))
        {
            return LocalRedirect(AppendQuery(returnUrl, "authError", "account_inactive"));
        }

        await HttpContext.SignOutAsync("External");
        await SignInAccountAsync(account);
        await MergeSessionCartToDbAsync(account.Id, cancellationToken);
        return LocalRedirect(returnUrl);
    }
    [HttpGet("access-denied")]
    public IActionResult AccessDenied()
    {
        return View();
    }
}
