using System.Security.Claims;
using DOAN_BANHANG.Models;
using DOAN_BANHANG.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DOAN_BANHANG.Areas.Admin.Controllers;

[Area("Admin")]
[Route("Admin/[controller]/[action]")]
[Microsoft.AspNetCore.Authorization.Authorize(Roles = "Admin")]
public class AccountsController : Controller
{
    private readonly DataContext _db;

    public AccountsController(DataContext db)
    {
        _db = db;
    }

    private bool TryGetSignedInAccount(out int accountId, out string? role)
    {
        role = null;
        accountId = 0;

        if (!(User?.Identity?.IsAuthenticated ?? false)) return false;

        var idRaw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(idRaw, out accountId) || accountId <= 0) return false;

        role = User.FindFirstValue(ClaimTypes.Role);
        return true;
    }

    private IQueryable<Account> ScopedAccounts(int? accountId, string? role)
    {
        var q = _db.Accounts.AsQueryable();

        // Admin area should not leak data to anonymous or non-admin users.
        if (!accountId.HasValue || accountId.Value <= 0)
        {
            return q.Where(_ => false);
        }

        if (string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase))
        {
            return q;
        }

        return q.Where(_ => false);
    }

    private static string NormalizeCustomerRoleOrDefault(string? raw)
    {
        var s = (raw ?? string.Empty).Trim();
        if (string.Equals(s, "Shop", StringComparison.OrdinalIgnoreCase)) return "Shop";
        if (string.Equals(s, "Customer", StringComparison.OrdinalIgnoreCase)) return "Customer";
        return "Customer";
    }

    private static string NormalizeStatusOrNull(string? raw)
    {
        var s = (raw ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(s)) return string.Empty;
        if (string.Equals(s, "Active", StringComparison.OrdinalIgnoreCase)) return "Active";
        if (string.Equals(s, "Blocked", StringComparison.OrdinalIgnoreCase)) return "Blocked";
        if (string.Equals(s, "Pending", StringComparison.OrdinalIgnoreCase)) return "Pending";
        return string.Empty;
    }

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] AdminAccountListQueryVm query, CancellationToken cancellationToken)
    {
        int? accountId = null;
        string? role = null;
        if (TryGetSignedInAccount(out var id, out var r))
        {
            accountId = id;
            role = r;
        }

        var effectiveRole = NormalizeCustomerRoleOrDefault(query.Role);
        var effectiveStatus = NormalizeStatusOrNull(query.Status);

        IQueryable<Account> q = ScopedAccounts(accountId, role)
            .AsNoTracking()
            .Include(a => a.Profile);

        q = q.Where(a => !a.IsDeleted);
        q = q.Where(a => a.Role != "Admin");
        // Hide seeded/demo accounts that don't have any auth credentials.
        // Real accounts should have either a local password hash or an external auth provider.
        q = q.Where(a => a.PasswordHash != null || a.AuthProvider != null);
        q = q.Where(a => a.Role == effectiveRole);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            q = q.Where(a => a.Email.Contains(term)
                             || (a.Profile != null && a.Profile.FullName != null && a.Profile.FullName.Contains(term)));
        }

        if (!string.IsNullOrWhiteSpace(effectiveStatus))
        {
            q = q.Where(a => a.Status == effectiveStatus);
        }

        var totalItems = await q.CountAsync(cancellationToken);

        var page = query.Page <= 0 ? 1 : query.Page;
        var pageSize = query.PageSize <= 0 ? 20 : Math.Min(query.PageSize, 50);
        var skip = (page - 1) * pageSize;

        var accounts = await q
            .OrderByDescending(a => a.Id)
            .Skip(skip)
            .Take(pageSize)
            .Select(a => new AdminAccountListItemVm
            {
                Id = a.Id,
                Email = a.Email,
                Role = a.Role,
                Status = a.Status,
                AuthProvider = a.AuthProvider,
                CreatedAt = a.CreatedAt,
                LastLoginAt = a.LastLoginAt,
                FullName = a.Profile != null ? a.Profile.FullName : null,
                PhoneNumber = a.Profile != null ? a.Profile.PhoneNumber : null,
            })
            .ToListAsync(cancellationToken);

        var vm = new AdminAccountListVm
        {
            Query = new AdminAccountListQueryVm
            {
                Search = query.Search,
                Role = effectiveRole,
                Status = effectiveStatus,
                Page = page,
                PageSize = pageSize
            },
            Accounts = accounts,
            TotalItems = totalItems
        };

        return View(vm);
    }

    public sealed record ToggleLockRequest(int Id, string? ReturnUrl);

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleLock([FromForm] ToggleLockRequest req, CancellationToken cancellationToken)
    {
        int? accountId = null;
        string? role = null;
        if (TryGetSignedInAccount(out var id, out var r))
        {
            accountId = id;
            role = r;
        }

        var returnUrl = !string.IsNullOrWhiteSpace(req.ReturnUrl) && Url.IsLocalUrl(req.ReturnUrl)
            ? req.ReturnUrl!
            : Url.Action(nameof(Index), new { Role = "Customer" }) ?? "/Admin/Accounts/Index?Role=Customer";

        if (!accountId.HasValue || accountId.Value <= 0 || !string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase))
        {
            return Redirect(returnUrl);
        }

        if (req.Id <= 0)
        {
            TempData["AdminAccountError"] = "Tài khoản không hợp lệ.";
            return Redirect(returnUrl);
        }

        if (req.Id == accountId.Value)
        {
            TempData["AdminAccountError"] = "Không thể khóa tài khoản đang đăng nhập.";
            return Redirect(returnUrl);
        }

        var isAdmin = User.IsInRole("Admin");
        var account = await _db.Accounts.FirstOrDefaultAsync(a => a.Id == req.Id && !a.IsDeleted, cancellationToken);
        if (account?.Role != "Shop" && !isAdmin) return Forbid();
        if (account is null)
        {
            TempData["AdminAccountError"] = "Không tìm thấy tài khoản.";
            return Redirect(returnUrl);
        }

        if (string.Equals(account.Role, "Admin", StringComparison.OrdinalIgnoreCase))
        {
            TempData["AdminAccountError"] = "Không thể khóa tài khoản Admin.";
            return Redirect(returnUrl);
        }

        var isBlocked = string.Equals(account.Status, "Blocked", StringComparison.OrdinalIgnoreCase);
        account.Status = isBlocked ? "Active" : "Blocked";
        await _db.SaveChangesAsync(cancellationToken);

        TempData["AdminAccountSuccess"] = isBlocked ? "Đã mở khóa tài khoản." : "Đã khóa tài khoản.";
        return Redirect(returnUrl);
    }
}
