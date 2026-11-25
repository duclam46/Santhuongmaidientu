using DOAN_BANHANG.Models;
using DOAN_BANHANG.Middleware;
using DOAN_BANHANG.Services;
using DOAN_BANHANG.Hubs;
using System.Text;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;

Console.OutputEncoding = Encoding.UTF8;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<DataContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Add services to the container.
builder.Services.AddControllersWithViews();

builder.Services.AddDistributedMemoryCache();

builder.Services.AddSession(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.IdleTimeout = TimeSpan.FromDays(7);
});

builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    })
    .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
    {
        options.LoginPath = "/auth/login";
        options.AccessDeniedPath = "/auth/access-denied";
        options.Events = new CookieAuthenticationEvents
        {
            OnRedirectToLogin = context =>
            {
                if (context.Request.Path.StartsWithSegments("/api"))
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                }

                context.Response.Redirect(context.RedirectUri);
                return Task.CompletedTask;
            },
            OnRedirectToAccessDenied = context =>
            {
                if (context.Request.Path.StartsWithSegments("/api"))
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                }

                context.Response.Redirect(context.RedirectUri);
                return Task.CompletedTask;
            },
            OnValidatePrincipal = async context =>
            {
                var principal = context.Principal;
                if (principal?.Identity?.IsAuthenticated != true) return;

                var idRaw = principal.FindFirstValue(ClaimTypes.NameIdentifier);
                if (!int.TryParse(idRaw, out var accountId) || accountId <= 0)
                {
                    context.RejectPrincipal();
                    await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                    return;
                }

                // Refresh role from DB so a role change (e.g. approved partner -> Shop) is effective immediately.
                try
                {
                    var db = context.HttpContext.RequestServices.GetRequiredService<DataContext>();
                    var account = await db.Accounts
                        .AsNoTracking()
                        .Select(a => new { a.Id, a.Email, a.Role, a.Status, a.IsDeleted })
                        .FirstOrDefaultAsync(a => a.Id == accountId);

                    if (account is null || account.IsDeleted || !string.Equals(account.Status, "Active", StringComparison.OrdinalIgnoreCase))
                    {
                        context.RejectPrincipal();
                        await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                        return;
                    }

                    var currentRole = principal.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
                    var dbRole = (account.Role ?? string.Empty).Trim();
                    var email = account.Email ?? principal.FindFirstValue(ClaimTypes.Email) ?? principal.Identity?.Name ?? string.Empty;

                    if (!string.Equals(currentRole, dbRole, StringComparison.OrdinalIgnoreCase))
                    {
                        var claims = new List<Claim>
                        {
                            new(ClaimTypes.NameIdentifier, account.Id.ToString()),
                            new(ClaimTypes.Email, email),
                            new(ClaimTypes.Name, email),
                            new(ClaimTypes.Role, dbRole)
                        };

                        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                        context.ReplacePrincipal(new ClaimsPrincipal(identity));
                        context.ShouldRenew = true;
                    }
                }
                catch
                {
                    // Best-effort: if DB is temporarily unavailable, keep the current principal.
                }
            }
        };
    })
    .AddCookie("External");

builder.Services.AddAuthorization();

builder.Services.AddSignalR();

builder.Services.AddScoped<IPasswordHasher<Account>, PasswordHasher<Account>>();

builder.Services.Configure<GhnOptions>(builder.Configuration.GetSection("Ghn"));
builder.Services.AddHttpClient<GhnClient>((sp, http) =>
{
    var opts = sp.GetRequiredService<IOptions<GhnOptions>>().Value;
    http.BaseAddress = new Uri(opts.BaseUrl.TrimEnd('/') + "/");
});

builder.Services.AddScoped<GhnStationCatalog>();
builder.Services.AddMemoryCache();
builder.Services.AddScoped<StatisticsService>();
builder.Services.AddScoped<DisputeAiService>();
builder.Services.Configure<DOAN_BANHANG.Services.AiChatbot.GeminiOptions>(builder.Configuration.GetSection(DOAN_BANHANG.Services.AiChatbot.GeminiOptions.SectionName));
builder.Services.AddSingleton<DOAN_BANHANG.Services.AiChatbot.SellzyAiMonitor>();
builder.Services.AddScoped<DOAN_BANHANG.Services.AiChatbot.SellzyAiTools>();
builder.Services.AddScoped<DOAN_BANHANG.Services.AiChatbot.SellzyAiFallbackEngine>();
builder.Services.AddHttpClient<GeminiChatService>();

// VNPay
var vnpaySection = builder.Configuration.GetSection("VnPay");
var vnpayOptions = new VnPayOptions();
vnpaySection.Bind(vnpayOptions);
builder.Services.AddSingleton(vnpayOptions);
builder.Services.AddSingleton<VnPayService>();
builder.Services.AddHostedService<VnPayExpiredOrderCleaner>();
builder.Services.AddHostedService<OrderAutoSettlementWorker>();
builder.Services.AddScoped<WalletService>();
builder.Services.AddScoped<AuditLogService>();

var googleClientId = builder.Configuration["Authentication:Google:ClientId"];
var googleClientSecret = builder.Configuration["Authentication:Google:ClientSecret"];

if (!string.IsNullOrWhiteSpace(googleClientId) && !string.IsNullOrWhiteSpace(googleClientSecret))
{
    builder.Services
        .AddAuthentication()
        .AddGoogle(options =>
        {
            options.ClientId = googleClientId;
            options.ClientSecret = googleClientSecret;
            options.SignInScheme = "External";
        });
}

var app = builder.Build();

// Setup global Vietnamese localization
var defaultCulture = new System.Globalization.CultureInfo("vi-VN");
defaultCulture.NumberFormat.CurrencyDecimalDigits = 0;
defaultCulture.NumberFormat.NumberDecimalDigits = 0;
defaultCulture.NumberFormat.CurrencySymbol = "đ";

var localizationOptions = new RequestLocalizationOptions
{
    DefaultRequestCulture = new Microsoft.AspNetCore.Localization.RequestCulture(defaultCulture),
    SupportedCultures = new List<System.Globalization.CultureInfo> { defaultCulture },
    SupportedUICultures = new List<System.Globalization.CultureInfo> { defaultCulture }
};
app.UseRequestLocalization(localizationOptions);

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
    app.UseMiddleware<RequestExceptionLoggingMiddleware>();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseSession();

app.UseAuthentication();



app.UseAuthorization();

app.MapControllers();

app.MapControllerRoute(
    name: "product_details",
    pattern: "product/{slug}",
    defaults: new { controller = "Products", action = "Details" });

app.MapControllerRoute(
    name: "category",
    pattern: "products/{slug}",
    defaults: new { controller = "Products", action = "Index" });

app.MapControllerRoute(
    name: "my_account",
    pattern: "my-account",
    defaults: new { controller = "Account", action = "Index" });

app.MapControllerRoute(
    name: "my_wallet",
    pattern: "my-wallet",
    defaults: new { controller = "Wallet", action = "Index" });

app.MapControllerRoute(
    name: "my_orders",
    pattern: "my-orders",
    defaults: new { controller = "Account", action = "Orders" });

app.MapControllerRoute(
    name: "my_wishlist",
    pattern: "my-wishlist",
    defaults: new { controller = "Account", action = "Wishlist" });

app.MapControllerRoute(
    name: "shop_index",
    pattern: "shop",
    defaults: new { controller = "Shop", action = "Index" });

app.MapControllerRoute(
    name: "shop_details",
    pattern: "shop/{slug}",
    defaults: new { controller = "Shop", action = "Details" });

app.MapControllerRoute(
    name: "blog_index",
    pattern: "blog",
    defaults: new { controller = "Blog", action = "Index" });

app.MapControllerRoute(
    name: "blog_details",
    pattern: "blog/{slug}",
    defaults: new { controller = "Blog", action = "Details" });

app.MapControllerRoute(
    name: "areas",
    pattern: "{area:exists}/{controller=Home}/{action=Index}/{id?}");

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

// SignalR Hub
app.MapHub<ChatHub>("/chathub");

await PlatformUpgradeSchemaBootstrapper.EnsureUpgradedSchemaAsync(app.Services);

if (app.Environment.IsDevelopment())
{
    await DbSeeder.MigrateAndSeedAsync(app.Services);
}

app.Run();
