using System.Security.Claims;
using System.Net;
using DOAN_BANHANG.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

namespace DOAN_BANHANG.Middleware;

public sealed class DevelopmentAutoLoginMiddleware
{
    private readonly RequestDelegate _next;

    public DevelopmentAutoLoginMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, IConfiguration config)
    {
        var enabled = config.GetValue<bool>("DevAutoLogin:Enabled");
        if (!enabled)
        {
            await _next(context);
            return;
        }

        // Safety: only allow auto-login from the local machine.
        // If you need to test from another device on LAN, set DevAutoLogin:AllowRemote=true.
        var allowRemote = config.GetValue<bool>("DevAutoLogin:AllowRemote");
        var remoteIp = context.Connection.RemoteIpAddress;
        if (!allowRemote && remoteIp is not null && !IPAddress.IsLoopback(remoteIp))
        {
            await _next(context);
            return;
        }

        if (context.User?.Identity?.IsAuthenticated ?? false)
        {
            await _next(context);
            return;
        }

        if (!context.Request.Path.StartsWithSegments("/Admin", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        var desiredRole = config.GetValue<string>("DevAutoLogin:Role") ?? "Admin";
        var desiredEmail = config.GetValue<string>("DevAutoLogin:Email");

        var db = context.RequestServices.GetRequiredService<DataContext>();

        Account? account = null;

        if (!string.IsNullOrWhiteSpace(desiredEmail))
        {
            var email = desiredEmail.Trim();
            account = await db.Accounts
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.Email == email);
        }

        if (account is null)
        {
            account = await db.Accounts
                .AsNoTracking()
                .Where(a => a.Role == desiredRole)
                .OrderBy(a => a.Id)
                .FirstOrDefaultAsync();
        }

        if (account is null)
        {
            account = await db.Accounts
                .AsNoTracking()
                .OrderBy(a => a.Id)
                .FirstOrDefaultAsync();
        }

        if (account is not null)
        {
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, account.Id.ToString()),
                new(ClaimTypes.Email, account.Email ?? string.Empty),
                new(ClaimTypes.Role, account.Role ?? desiredRole),
                new("DevAutoLogin", "true")
            };

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(identity);

            // Make current request see the principal immediately.
            context.User = principal;

            await context.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                principal,
                new AuthenticationProperties
                {
                    IsPersistent = true,
                    ExpiresUtc = DateTimeOffset.UtcNow.AddDays(7)
                });
        }

        await _next(context);
    }
}
