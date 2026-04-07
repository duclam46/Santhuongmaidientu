using DOAN_BANHANG.Models;
using DOAN_BANHANG.Middleware;
using DOAN_BANHANG.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;

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
    .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie("External");

builder.Services.AddAuthorization();

builder.Services.AddScoped<IPasswordHasher<Account>, PasswordHasher<Account>>();

builder.Services.Configure<GhnOptions>(builder.Configuration.GetSection("Ghn"));
builder.Services.AddHttpClient<GhnClient>((sp, http) =>
{
    var opts = sp.GetRequiredService<IOptions<GhnOptions>>().Value;
    http.BaseAddress = new Uri(opts.BaseUrl.TrimEnd('/') + "/");
});

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

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
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

// Development-only: allow opening /Admin pages without manually logging in.
// Controlled by appsettings.Development.json: DevAutoLogin:Enabled/Role/Email.
app.UseMiddleware<DevelopmentAutoLoginMiddleware>();

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
    name: "my_orders",
    pattern: "my-orders",
    defaults: new { controller = "Account", action = "Orders" });

app.MapControllerRoute(
    name: "my_wishlist",
    pattern: "my-wishlist",
    defaults: new { controller = "Account", action = "Wishlist" });

app.MapControllerRoute(
    name: "shop_details",
    pattern: "shop/{slug}",
    defaults: new { controller = "Shop", action = "Details" });

app.MapControllerRoute(
    name: "areas",
    pattern: "{area:exists}/{controller=Home}/{action=Index}/{id?}");

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

if (app.Environment.IsDevelopment())
{
    await DbSeeder.MigrateAndSeedAsync(app.Services);
}

app.Run();
