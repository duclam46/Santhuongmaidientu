using DOAN_BANHANG.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DOAN_BANHANG.Services;

public static class DbSeeder
{
    private static readonly HashSet<string> AllowedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png",
        ".jpg",
        ".jpeg",
        ".webp"
    };

    private sealed record ProductSeed(
        string Slug,
        string Name,
        string CategorySlug,
        decimal BasePrice,
        int Stock,
        string Desc,
        string Status);

    private static int StableHash(string key)
    {
        if (string.IsNullOrEmpty(key)) return 0;
        unchecked
        {
            int hash = 17;
            foreach (var ch in key)
            {
                hash = (hash * 31) + ch;
            }
            return hash;
        }
    }

    private static List<string> LoadImageUrls(string webRootPath, params string[] relativeFolders)
    {
        var results = new List<string>();
        if (string.IsNullOrWhiteSpace(webRootPath) || !Directory.Exists(webRootPath)) return results;

        foreach (var folder in relativeFolders)
        {
            if (string.IsNullOrWhiteSpace(folder)) continue;

            var dir = Path.Combine(webRootPath, folder.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(dir)) continue;

            foreach (var file in Directory.EnumerateFiles(dir, "*.*", SearchOption.TopDirectoryOnly))
            {
                var ext = Path.GetExtension(file);
                if (!AllowedImageExtensions.Contains(ext)) continue;

                var url = (folder.TrimEnd('/', '\\') + "/" + Path.GetFileName(file))
                    .Replace('\\', '/');
                results.Add(url);
            }
        }

        return results
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static (decimal Average, int Count) GetSeedRating(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return (0m, 0);

        unchecked
        {
            int hash = 23;
            foreach (var ch in key)
            {
                hash = (hash * 31) + ch;
            }

            var tenth = Math.Abs(hash % 21); // 0..20
            var avg = 3.0m + (tenth / 10.0m);
            if (avg > 5.0m) avg = 5.0m;

            var count = 5 + Math.Abs(hash % 250);
            return (decimal.Round(avg, 2), count);
        }
    }

    public static async Task MigrateAndSeedAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<DataContext>();
        var logger = scope.ServiceProvider
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger(nameof(DbSeeder));
        var webHostEnv = scope.ServiceProvider.GetService<IWebHostEnvironment>();

        // Seeding can touch many rows; avoid failing on slow SQL Server operations.
        context.Database.SetCommandTimeout(180);

        await context.Database.MigrateAsync(cancellationToken);

        var resetSeed = IsTruthy(Environment.GetEnvironmentVariable("RESET_SEED"));
        if (resetSeed)
        {
            await ResetCatalogAsync(context, logger, cancellationToken);
        }

        var nowUtc = DateTime.UtcNow;
        var rng = new Random(12345);

        // ---- Colors (upsert by Name) ----
        // Some base colors have selector icons in wwwroot/assets/images/product-details/; others will render by hex swatch.
        var colorSeeds = new (string Name, string Code)[]
        {
            ("green", "#088178"),
            ("blue", "#3366FF"),
            ("yellow", "#FFC107"),
            ("red", "#CB0233"),

            ("black", "#111827"),
            ("white", "#FFFFFF"),
            ("gray", "#6B7280"),
            ("silver", "#C0C0C0"),

            ("orange", "#F97316"),
            ("purple", "#7C3AED"),
            ("pink", "#EC4899"),
            ("brown", "#8B5E3C"),

            ("navy", "#1E3A8A"),
            ("teal", "#14B8A6"),
            ("cyan", "#06B6D4"),
            ("lime", "#84CC16"),

            ("beige", "#F5F5DC"),
            ("cream", "#FFFDD0"),
            ("maroon", "#800000"),
            ("olive", "#808000"),
        };

        foreach (var (name, code) in colorSeeds)
        {
            var existing = await context.Colors.FirstOrDefaultAsync(c => c.Name == name, cancellationToken);
            if (existing is null)
            {
                context.Colors.Add(new Color
                {
                    Name = name,
                    Code = code,
                    CreatedAt = nowUtc
                });
            }
            else
            {
                existing.Code = code;
            }
        }

        await context.SaveChangesAsync(cancellationToken);

        var colorIdByName = await context.Colors
            .AsNoTracking()
            .ToDictionaryAsync(c => c.Name, c => c.Id, StringComparer.OrdinalIgnoreCase, cancellationToken);

        // Keep seeded product variants stable by using only the original base colors.
        var colorNames = new[] { "green", "blue", "yellow", "red" };

        // ---- Categories (upsert by Slug) ----
        // NOTE: Keep the category tree at 2 levels (root -> direct children).
        // The current category browse/filter logic includes only the selected category + its direct children.
        var categorySeeds = new (string Slug, string Name, string? ParentSlug)[]
        {
            ("menu-thoi-trang-nam", "Thời trang nam", null),
            ("menu-thoi-trang-nu", "Thời trang nữ", null),
            ("menu-giay-dep", "Giày dép", null),
            ("menu-phu-kien", "Phụ kiện", null),

            ("ao-nam", "Áo nam", "menu-thoi-trang-nam"),
            ("quan-nam", "Quần nam", "menu-thoi-trang-nam"),
            ("do-the-thao-nam", "Đồ thể thao nam", "menu-thoi-trang-nam"),

            ("ao-nu", "Áo nữ", "menu-thoi-trang-nu"),
            ("vay-dam", "Váy / Đầm", "menu-thoi-trang-nu"),
            ("quan-nu", "Quần nữ", "menu-thoi-trang-nu"),

            ("giay-the-thao", "Giày thể thao", "menu-giay-dep"),
            ("dep-sandal", "Dép / Sandal", "menu-giay-dep"),
            ("giay-cao-got", "Giày cao gót", "menu-giay-dep"),

            ("tui-xach", "Túi xách", "menu-phu-kien"),
            ("non-mu", "Nón / Mũ", "menu-phu-kien"),
            ("that-lung", "Thắt lưng", "menu-phu-kien"),
            ("dong-ho", "Đồng hồ", "menu-phu-kien"),
            ("kinh-mat", "Kính mắt", "menu-phu-kien"),
        };

        var existingCategories = await context.Categories
            .ToListAsync(cancellationToken);

        var categoryBySlug = existingCategories
            .Where(c => !string.IsNullOrWhiteSpace(c.Slug))
            .ToDictionary(c => c.Slug, c => c, StringComparer.OrdinalIgnoreCase);

        foreach (var (slug, name, _) in categorySeeds)
        {
            if (!categoryBySlug.TryGetValue(slug, out var cat))
            {
                context.Categories.Add(new Category
                {
                    Name = name,
                    Slug = slug,
                    ParentId = null,
                    IsActive = true,
                    CreatedAt = nowUtc,
                    UpdatedAt = nowUtc
                });
            }
            else
            {
                cat.Name = name;
                cat.IsActive = true;
                cat.UpdatedAt = nowUtc;
            }
        }

        await context.SaveChangesAsync(cancellationToken);

        var categoriesAfterInsert = await context.Categories
            .ToListAsync(cancellationToken);
        var idBySlug = categoriesAfterInsert
            .ToDictionary(c => c.Slug, c => c.Id, StringComparer.OrdinalIgnoreCase);

        foreach (var (slug, _, parentSlug) in categorySeeds)
        {
            if (string.IsNullOrWhiteSpace(parentSlug)) continue;
            if (!idBySlug.TryGetValue(slug, out var id)) continue;
            if (!idBySlug.TryGetValue(parentSlug, out var parentId)) continue;

            var cat = categoriesAfterInsert.First(c => c.Id == id);
            if (cat.ParentId != parentId)
            {
                cat.ParentId = parentId;
                cat.UpdatedAt = nowUtc;
            }
        }

        await context.SaveChangesAsync(cancellationToken);

        // ---- Admin + Customer Accounts (so we can seed carts/orders/reviews) ----
        var adminEmail = "admin@local";
        if (await context.Accounts.AllAsync(a => a.Email != adminEmail, cancellationToken))
        {
            context.Accounts.Add(new Account
            {
                Email = adminEmail,
                PasswordHash = null,
                AuthProvider = null,
                AuthProviderUserId = null,
                Role = "Admin",
                Status = "Active",
                CreatedAt = nowUtc
            });
            await context.SaveChangesAsync(cancellationToken);
        }

        var customerSeeds = new (string Email, string FullName)[]
        {
            ("customer1@local", "Khách hàng 1"),
            ("customer2@local", "Khách hàng 2"),
            ("customer3@local", "Khách hàng 3"),
            ("customer4@local", "Khách hàng 4"),
            ("customer5@local", "Khách hàng 5"),
        };

        foreach (var (email, fullName) in customerSeeds)
        {
            var account = await context.Accounts.FirstOrDefaultAsync(a => a.Email == email, cancellationToken);
            if (account is null)
            {
                account = new Account
                {
                    Email = email,
                    PasswordHash = null,
                    AuthProvider = null,
                    AuthProviderUserId = null,
                    Role = "Customer",
                    Status = "Active",
                    CreatedAt = nowUtc.AddDays(-rng.Next(0, 365))
                };
                context.Accounts.Add(account);
                await context.SaveChangesAsync(cancellationToken);
            }

            var profile = await context.UserProfiles.FirstOrDefaultAsync(p => p.AccountId == account.Id, cancellationToken);
            if (profile is null)
            {
                context.UserProfiles.Add(new UserProfile
                {
                    AccountId = account.Id,
                    FullName = fullName,
                    PhoneNumber = null,
                    AvatarUrl = null,
                    ShopName = null,
                    ShopLogoUrl = null,
                    ShopDescription = null,
                    Address = null,
                    CreatedAt = nowUtc.AddDays(-rng.Next(0, 365))
                });
            }
        }

        await context.SaveChangesAsync(cancellationToken);

        // ---- Shop Accounts + UserProfiles (shops are Accounts with Role=Shop) ----
        var shopSeeds = new (string Slug, string ShopName)[]
        {
            ("shop-a", "Shop A"),
            ("shop-b", "Shop B"),
            ("shop-c", "Shop C"),
            ("shop-d", "Shop D"),
            ("shop-e", "Shop E"),
        };

        foreach (var (slug, shopName) in shopSeeds)
        {
            var email = $"{slug}@shop.local";
            var account = await context.Accounts.FirstOrDefaultAsync(a => a.Email == email, cancellationToken);
            if (account is null)
            {
                account = new Account
                {
                    Email = email,
                    PasswordHash = null,
                    AuthProvider = null,
                    AuthProviderUserId = null,
                    Role = "Shop",
                    Status = "Active",
                    CreatedAt = nowUtc
                };
                context.Accounts.Add(account);
                await context.SaveChangesAsync(cancellationToken);
            }

            var profile = await context.UserProfiles.FirstOrDefaultAsync(p => p.AccountId == account.Id, cancellationToken);
            if (profile is null)
            {
                context.UserProfiles.Add(new UserProfile
                {
                    AccountId = account.Id,
                    FullName = shopName,
                    PhoneNumber = null,
                    AvatarUrl = null,
                    ShopName = shopName,
                    ShopSlug = slug,
                    ShopLogoUrl = null,
                    ShopDescription = $"{shopName} - cửa hàng chính hãng và uy tín.",
                    Address = null,
                    CreatedAt = nowUtc,
                    UpdatedAt = nowUtc
                });
            }
        }

        await context.SaveChangesAsync(cancellationToken);

        // ---- Sizes (upsert by Name) ----
        // IMPORTANT: seed sizes regardless of whether shop accounts exist.
        // The admin create-product UI depends on these rows to show size schemes (shoe/volume/weight).
        var clothingSizeLabels = new[] { "XXS", "XS", "S", "M", "L", "XL", "XXL", "XXXL" };
        var shoeSizeLabels = Enumerable.Range(35, 11).Select(x => x.ToString()).ToArray(); // 35..45

        // Cosmetic/liquid volumes
        var volumeLabels = new[] { "10ml", "30ml", "50ml", "100ml", "150ml", "200ml", "250ml", "500ml", "1L" };

        // Powder/solid weights
        var weightLabels = new[] { "50g", "100g", "200g", "250g", "500g", "1kg", "2kg" };

        foreach (var label in clothingSizeLabels
            .Concat(shoeSizeLabels)
            .Concat(volumeLabels)
            .Concat(weightLabels)
            .Select(x => (x ?? string.Empty).Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var exists = await context.Sizes.FirstOrDefaultAsync(s => s.Name == label, cancellationToken);
            if (exists is null)
            {
                context.Sizes.Add(new Size
                {
                    Name = label,
                    CreatedAt = nowUtc
                });
            }
        }

        await context.SaveChangesAsync(cancellationToken);

        var sizeIdByLabel = await context.Sizes
            .AsNoTracking()
            .ToDictionaryAsync(s => s.Name, s => s.Id, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var shopAccountIds = await context.Accounts
            .AsNoTracking()
            .Where(a => a.Role == "Shop")
            .OrderBy(a => a.Id)
            .Select(a => a.Id)
            .ToListAsync(cancellationToken);

        if (shopAccountIds.Count == 0)
        {
            logger.LogWarning("No shop accounts found; skipping product seed.");
            return;
        }

        // ---- Products (upsert by Slug) ----
        var productsSeed = new List<ProductSeed>
        {
            // Nam
            new("ao-thun-basic-nam", "Áo thun basic nam", "ao-nam", 199000m, 80, "Áo thun basic chất liệu cotton, phù hợp mặc hằng ngày.", "Active"),
            new("ao-thun-oversize-nam", "Áo thun oversize nam", "ao-nam", 229000m, 60, "Áo thun oversize phù hợp mặc đi học, đi chơi.", "Active"),
            new("ao-polo-pique-nam", "Áo polo pique nam", "ao-nam", 289000m, 40, "Áo polo chất liệu pique thoáng mát, lịch sự.", "Active"),
            new("ao-so-mi-oxford-nam", "Áo sơ mi Oxford nam", "ao-nam", 459000m, 35, "Áo sơ mi Oxford phù hợp đi làm và đi chơi.", "Active"),
            new("ao-khoac-gio-nam", "Áo khoác gió nam", "ao-nam", 549000m, 25, "Áo khoác gió nhẹ, tiện lợi khi thời tiết se lạnh.", "Active"),
            new("quan-jean-slimfit-nam", "Quần jean slimfit nam", "quan-nam", 399000m, 45, "Quần jean slimfit co giãn nhẹ, dễ vận động.", "Active"),
            new("quan-kaki-nam", "Quần kaki nam", "quan-nam", 429000m, 30, "Quần kaki đứng form, phù hợp môi trường công sở.", "Active"),
            new("quan-short-jean-nam", "Quần short jean nam", "quan-nam", 249000m, 55, "Quần short jean nam năng động cho ngày hè.", "Active"),
            new("bo-do-the-thao-nam", "Bộ đồ thể thao nam", "do-the-thao-nam", 599000m, 20, "Bộ đồ thể thao thấm hút tốt, phù hợp tập gym/chạy bộ.", "Active"),

            // Nữ
            new("ao-thun-basic-nu", "Áo thun basic nữ", "ao-nu", 189000m, 70, "Áo thun basic nữ, chất vải mềm mịn.", "Active"),
            new("ao-so-mi-trang-nu", "Áo sơ mi trắng nữ", "ao-nu", 359000m, 30, "Áo sơ mi trắng nữ phù hợp đi làm, đi họp.", "Active"),
            new("ao-khoac-cardigan-nu", "Áo khoác cardigan nữ", "ao-nu", 319000m, 25, "Cardigan nhẹ, phối đồ mùa thu đông rất đẹp.", "Active"),
            new("dam-cong-so-dang-om", "Đầm công sở dáng ôm", "vay-dam", 699000m, 15, "Đầm công sở dáng ôm thanh lịch, tôn dáng.", "Active"),
            new("dam-xoe-hoa-nhi", "Đầm xòe hoa nhí", "vay-dam", 649000m, 18, "Đầm xòe họa tiết hoa nhí, phù hợp đi chơi.", "Active"),
            new("chan-vay-chu-a", "Chân váy chữ A", "vay-dam", 299000m, 40, "Chân váy chữ A dễ phối với áo thun/sơ mi.", "Active"),
            new("quan-jean-nu-ong-dung", "Quần jean nữ ống đứng", "quan-nu", 389000m, 35, "Quần jean nữ ống đứng dễ phối đồ.", "Active"),
            new("quan-au-nu-cong-so", "Quần âu nữ công sở", "quan-nu", 419000m, 25, "Quần âu nữ phù hợp môi trường công sở.", "Active"),

            // Giày dép
            new("giay-the-thao-trang", "Giày thể thao trắng", "giay-the-thao", 899000m, 20, "Giày thể thao đế êm, phù hợp đi chơi và tập luyện.", "Active"),
            new("giay-the-thao-den", "Giày thể thao đen", "giay-the-thao", 799000m, 18, "Giày thể thao màu đen dễ phối đồ.", "Active"),
            new("dep-sandal-nu", "Dép sandal nữ", "dep-sandal", 249000m, 40, "Dép sandal êm chân, phù hợp đi dạo.", "Active"),
            new("giay-cao-got-co-ban", "Giày cao gót cơ bản", "giay-cao-got", 499000m, 22, "Giày cao gót cơ bản, phù hợp đi làm.", "Active"),

            // Phụ kiện
            new("tui-deo-cheo-mini", "Túi đeo chéo mini", "tui-xach", 159000m, 80, "Túi đeo chéo mini phù hợp đi dạo, đi học.", "Active"),
            new("mu-luoi-trai", "Mũ lưỡi trai", "non-mu", 99000m, 100, "Mũ lưỡi trai form chuẩn, che nắng tốt.", "Active"),
            new("that-lung-da", "Thắt lưng da", "that-lung", 179000m, 60, "Thắt lưng da, dễ phối đồ.", "Active"),
            new("dong-ho-toi-gian", "Đồng hồ tối giản", "dong-ho", 599000m, 15, "Đồng hồ thiết kế tối giản.", "Active"),
            new("kinh-mat-thoi-trang", "Kính mắt thời trang", "kinh-mat", 199000m, 50, "Kính mắt thời trang, chống chói nhẹ.", "Active"),
        };

        var categoryIdBySlug2 = await context.Categories
            .AsNoTracking()
            .ToDictionaryAsync(c => c.Slug, c => c.Id, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var imagePool = new List<string>();
        if (webHostEnv is not null && !string.IsNullOrWhiteSpace(webHostEnv.WebRootPath))
        {
            // Prefer product-related assets.
            imagePool.AddRange(LoadImageUrls(
                webHostEnv.WebRootPath,
                "assets/images/product-details",
                "assets/images/home-2"));
        }

        // Fallback (in case scanning fails for some reason).
        if (imagePool.Count == 0)
        {
            imagePool.AddRange(new[]
            {
                "assets/images/product-details/product-details-slider-1.png",
                "assets/images/product-details/product-details-slider-2.png",
                "assets/images/product-details/product-details-slider-3.png",
                "assets/images/product-details/product-details-slider-4.png",
                "assets/images/product-details/product-details-slider-5.png",
                "assets/images/product-details/product-details-slider-6.png",
            });
        }

        var shopIndex = 0;

        foreach (var seed in productsSeed)
        {
            if (!categoryIdBySlug2.TryGetValue(seed.CategorySlug, out var categoryId))
            {
                continue;
            }

            var existing = await context.Products
                .Include(p => p.Variants)
                    .ThenInclude(v => v.Images)
                .Include(p => p.Images)
                .FirstOrDefaultAsync(p => p.Slug == seed.Slug, cancellationToken);

            if (existing is null)
            {
                var (avg, count) = GetSeedRating(seed.Slug);
                var shopAccountId = shopAccountIds[shopIndex % shopAccountIds.Count];
                shopIndex++;

                existing = new Product
                {
                    Name = seed.Name,
                    Slug = seed.Slug,
                    CategoryId = categoryId,
                    ShopId = shopAccountId,
                    ShortDescription = seed.Desc.Length <= 200 ? seed.Desc : seed.Desc[..200],
                    Description = seed.Desc,
                    Status = seed.Status,
                    AverageRating = avg,
                    ReviewCount = count,
                    CreatedAt = nowUtc.AddDays(-rng.Next(0, 180)),
                    UpdatedAt = nowUtc,
                };
                context.Products.Add(existing);
            }
            else
            {
                existing.Name = seed.Name;
                existing.ShortDescription = seed.Desc.Length <= 200 ? seed.Desc : seed.Desc[..200];
                existing.Description = seed.Desc;
                existing.Status = seed.Status;
                existing.UpdatedAt = nowUtc;
            }

            await context.SaveChangesAsync(cancellationToken);

            // Variants: guarantee at least 2 colors + 2 sizes per product.
            var preferredSizeLabels = new[] { "S", "M", "L", "XL" };
            var selectedSizes = preferredSizeLabels
                .Where(sizeIdByLabel.ContainsKey)
                .Take(2)
                .ToList();

            if (selectedSizes.Count < 2)
            {
                selectedSizes = sizeIdByLabel.Keys
                    .OrderBy(x => x)
                    .Take(2)
                    .ToList();
            }

            var availableColorNames = colorNames.Length == 0
                ? new[] { "primary", "info" }
                : colorNames;

            var firstColorIndex = availableColorNames.Length == 0
                ? 0
                : Math.Abs(StableHash(existing.Slug)) % availableColorNames.Length;

            var secondColorIndex = availableColorNames.Length <= 1
                ? firstColorIndex
                : (firstColorIndex + 1 + (Math.Abs(StableHash(existing.Slug + "|c2")) % (availableColorNames.Length - 1))) % availableColorNames.Length;

            var selectedColors = new[]
            {
                availableColorNames.Length == 0 ? "primary" : availableColorNames[firstColorIndex],
                availableColorNames.Length <= 1 ? (availableColorNames.Length == 0 ? "info" : availableColorNames[firstColorIndex]) : availableColorNames[secondColorIndex]
            }
            .Select(x => (x ?? string.Empty).Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .ToList();

            if (selectedColors.Count < 2)
            {
                // Ensure we always have 2 colors even with tiny pools.
                selectedColors.Add(selectedColors.Count == 0 ? "primary" : "info");
                selectedColors = selectedColors
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(2)
                    .ToList();
            }

            var variantCountTarget = Math.Max(1, selectedSizes.Count * selectedColors.Count);
            var perVariantStock = Math.Max(0, seed.Stock / variantCountTarget);

            foreach (var sizeLabel in selectedSizes)
            {
                if (!sizeIdByLabel.TryGetValue(sizeLabel, out var sizeId)) continue;

                foreach (var colorName in selectedColors)
                {
                    int? colorId = null;
                    if (colorIdByName.TryGetValue(colorName, out var tmpColorId))
                    {
                        colorId = tmpColorId;
                    }

                    var v = existing.Variants.FirstOrDefault(x => x.SizeId == sizeId && x.ColorId == colorId);
                    if (v is null)
                    {
                        var sku = $"{existing.Slug}-{colorName}-{sizeLabel}".ToUpperInvariant();
                        if (sku.Length > 100) sku = sku[..100];

                        existing.Variants.Add(new ProductVariant
                        {
                            ProductId = existing.Id,
                            SizeId = sizeId,
                            Price = seed.BasePrice,
                            SalePrice = null,
                            SaleStartDate = null,
                            SaleEndDate = null,
                            ColorId = colorId,
                            Sku = sku,
                            StockQuantity = perVariantStock,
                            ReservedStock = 0,
                            IsDefault = existing.Variants.All(x => !x.IsDefault),
                            IsActive = true,
                            CreatedAt = nowUtc,
                            UpdatedAt = nowUtc
                        });
                    }
                    else
                    {
                        v.Price = seed.BasePrice;
                        v.ColorId ??= colorId;
                        if (string.IsNullOrWhiteSpace(v.Sku))
                        {
                            var sku = $"{existing.Slug}-{colorName}-{sizeLabel}".ToUpperInvariant();
                            if (sku.Length > 100) sku = sku[..100];
                            v.Sku = sku;
                        }
                        if (v.StockQuantity <= 0) v.StockQuantity = perVariantStock;
                        v.IsActive = true;
                        v.UpdatedAt = nowUtc;
                    }
                }
            }

            // Images: ensure at least 2.
            if (existing.Images.Count < 4)
            {
                var target = 4;

                var start = imagePool.Count == 0
                    ? 0
                    : Math.Abs(StableHash(existing.Slug)) % imagePool.Count;

                var safety = 0;
                while (existing.Images.Count < target && safety < imagePool.Count + 10)
                {
                    safety++;
                    var url = imagePool.Count == 0
                        ? string.Empty
                        : imagePool[(start + existing.Images.Count) % imagePool.Count];

                    if (string.IsNullOrWhiteSpace(url))
                    {
                        url = imagePool[rng.Next(imagePool.Count)];
                    }

                    if (existing.Images.Any(x => string.Equals(x.ImageUrl, url, StringComparison.OrdinalIgnoreCase)))
                    {
                        // Try another.
                        url = imagePool[rng.Next(imagePool.Count)];
                        if (existing.Images.Any(x => string.Equals(x.ImageUrl, url, StringComparison.OrdinalIgnoreCase)))
                        {
                            continue;
                        }
                    }

                    existing.Images.Add(new ProductImage
                    {
                        ProductId = existing.Id,
                        ImageUrl = url,
                        IsPrimary = existing.Images.Count == 0,
                        SortOrder = existing.Images.Count,
                        CreatedAt = nowUtc
                    });
                }

                // Ensure at least one primary image.
                if (existing.Images.All(i => !i.IsPrimary) && existing.Images.Count > 0)
                {
                    existing.Images.First().IsPrimary = true;
                }
            }

            // Variant images: ensure at least 1 image per variant (so đổi màu/size vẫn có ảnh).
            var variantImageFallback = existing.Images
                .OrderByDescending(i => i.IsPrimary)
                .ThenBy(i => i.SortOrder)
                .Select(i => i.ImageUrl)
                .Where(u => !string.IsNullOrWhiteSpace(u))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(2)
                .ToList();

            foreach (var v in existing.Variants.Where(v => v.IsActive))
            {
                if (v.Images.Count != 0) continue;

                var urls = variantImageFallback;
                if (urls.Count == 0)
                {
                    urls = imagePool
                        .Where(u => !string.IsNullOrWhiteSpace(u))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Take(2)
                        .ToList();
                }

                for (var idx = 0; idx < urls.Count; idx++)
                {
                    var url = urls[idx];
                    if (string.IsNullOrWhiteSpace(url)) continue;
                    if (v.Images.Any(x => string.Equals(x.ImageUrl, url, StringComparison.OrdinalIgnoreCase))) continue;

                    v.Images.Add(new VariantImage
                    {
                        ImageUrl = url,
                        IsPrimary = idx == 0,
                        SortOrder = idx,
                        CreatedAt = nowUtc
                    });
                }
            }

            await context.SaveChangesAsync(cancellationToken);
        }

        // ---- Size guides ----
        if (!await context.SizeGuides.AsNoTracking().AnyAsync(cancellationToken))
        {
            var products = await context.Products
                .AsNoTracking()
                .OrderBy(p => p.Id)
                .Take(12)
                .ToListAsync(cancellationToken);

            var guideSizes = new[] { "S", "M", "L", "XL" };
            var measurements = new[]
            {
                (Name: "Dài áo", Base: 64),
                (Name: "Rộng vai", Base: 42),
                (Name: "Vòng ngực", Base: 96)
            };

            foreach (var p in products)
            {
                var guideSort = 0;
                foreach (var sz in guideSizes)
                {
                    var bump = sz switch
                    {
                        "S" => 0,
                        "M" => 2,
                        "L" => 4,
                        "XL" => 6,
                        _ => 0
                    };

                    var guide = new SizeGuide
                    {
                        ProductId = p.Id,
                        SizeLabel = sz,
                        Note = null,
                        SortOrder = guideSort++,
                        CreatedAt = nowUtc
                    };

                    var detailSort = 0;
                    foreach (var m in measurements)
                    {
                        guide.Details.Add(new SizeGuideDetail
                        {
                            Name = m.Name,
                            Value = (m.Base + bump).ToString(),
                            Unit = "cm",
                            SortOrder = detailSort++
                        });
                    }

                    context.SizeGuides.Add(guide);
                }
            }

            await context.SaveChangesAsync(cancellationToken);
        }

        // ---- Carts + cart items ----
        var customerAccountIds = await context.Accounts
            .AsNoTracking()
            .Where(a => a.Role == "Customer")
            .OrderBy(a => a.Id)
            .Select(a => a.Id)
            .ToListAsync(cancellationToken);

        if (customerAccountIds.Count != 0 && !await context.Carts.AsNoTracking().AnyAsync(cancellationToken))
        {
            var allVariantIds = await context.ProductVariants
                .AsNoTracking()
                .OrderBy(v => v.Id)
                .Select(v => v.Id)
                .ToListAsync(cancellationToken);

            foreach (var customerId in customerAccountIds)
            {
                var cart = new Cart
                {
                    AccountId = customerId,
                    CreatedAt = nowUtc,
                    UpdatedAt = nowUtc
                };
                context.Carts.Add(cart);
                await context.SaveChangesAsync(cancellationToken);

                if (allVariantIds.Count == 0) continue;

                var itemsCount = rng.Next(1, 4);
                var chosen = new HashSet<int>();
                for (var i = 0; i < itemsCount; i++)
                {
                    var variantId = allVariantIds[rng.Next(allVariantIds.Count)];
                    if (!chosen.Add(variantId)) continue;

                    context.CartItems.Add(new CartItem
                    {
                        CartId = cart.Id,
                        VariantId = variantId,
                        Quantity = rng.Next(1, 4),
                        CreatedAt = nowUtc,
                        UpdatedAt = nowUtc
                    });
                }
            }

            await context.SaveChangesAsync(cancellationToken);
        }

        // ---- Orders + OrderItems ----
        if (customerAccountIds.Count != 0 && !await context.Orders.AsNoTracking().AnyAsync(cancellationToken))
        {
            var variants = await context.ProductVariants
                .AsNoTracking()
                .Where(v => v.IsActive)
                .Select(v => new
                {
                    v.Id,
                    v.Price,
                    ShopId = v.Product != null ? v.Product.ShopId : 0,
                    ProductName = v.Product != null ? v.Product.Name : string.Empty,
                    ColorName = v.Color != null ? v.Color.Name : null,
                    SizeName = v.Size != null ? v.Size.Name : null
                })
                .Where(v => v.ShopId != 0 && v.ProductName != "")
                .ToListAsync(cancellationToken);

            var byShop = variants
                .GroupBy(x => x.ShopId)
                .ToDictionary(g => g.Key, g => g.ToList());

            if (byShop.Count != 0)
            {
                var orderCount = 12;
                for (var i = 0; i < orderCount; i++)
                {
                    var customerId = customerAccountIds[rng.Next(customerAccountIds.Count)];
                    var shopId = shopAccountIds[rng.Next(shopAccountIds.Count)];

                    if (!byShop.TryGetValue(shopId, out var shopVariants) || shopVariants.Count == 0)
                    {
                        shopId = byShop.Keys.First();
                        shopVariants = byShop[shopId];
                    }

                    var itemsToAdd = rng.Next(1, 4);
                    var usedVariantIds = new HashSet<int>();

                    var createdAt = nowUtc.AddDays(-rng.Next(0, 60));
                    var order = new Order
                    {
                        CustomerId = customerId,
                        ShopId = shopId,
                        OrderCode = $"ORD-{nowUtc:yyyyMMdd}-{i + 1:0000}-{rng.Next(1000, 9999)}",
                        OrderStatus = "Pending",
                        PaymentMethod = "COD",
                        PaymentStatus = "Pending",
                        RecipientName = "Khách hàng",
                        RecipientPhone = "0900000000",
                        ShippingAddress = "TP.HCM",
                        CreatedAt = createdAt,
                        UpdatedAt = createdAt,
                        SubTotal = 0m,
                        ShippingFee = 0m,
                        VoucherId = null,
                        DiscountAmount = 0m,
                        FinalAmount = 0m
                    };

                    context.Orders.Add(order);
                    await context.SaveChangesAsync(cancellationToken);

                    decimal total = 0m;
                    for (var j = 0; j < itemsToAdd; j++)
                    {
                        var pick = shopVariants[rng.Next(shopVariants.Count)];
                        if (!usedVariantIds.Add(pick.Id)) continue;

                        var qty = rng.Next(1, 4);
                        var lineTotal = pick.Price * qty;
                        total += lineTotal;

                        context.OrderItems.Add(new OrderItem
                        {
                            OrderId = order.Id,
                            VariantId = pick.Id,
                            ProductName = pick.ProductName,
                            ColorName = pick.ColorName,
                            SizeName = pick.SizeName,
                            UnitPrice = pick.Price,
                            Quantity = qty,
                            LineTotal = lineTotal
                        });
                    }

                    order.SubTotal = total;
                    order.FinalAmount = order.SubTotal + order.ShippingFee - order.DiscountAmount;
                    order.UpdatedAt = nowUtc;
                    await context.SaveChangesAsync(cancellationToken);
                }
            }
        }

        // ---- Product reviews (then recompute rating aggregates) ----
        if (customerAccountIds.Count != 0 && !await context.ProductReviews.AsNoTracking().AnyAsync(cancellationToken))
        {
            var products = await context.Products
                .AsNoTracking()
                .Where(p => p.Status == "Active")
                .OrderBy(p => p.Id)
                .ToListAsync(cancellationToken);

            foreach (var p in products)
            {
                var reviewsForProduct = rng.Next(1, Math.Min(6, customerAccountIds.Count + 1));
                var usedCustomers = new HashSet<int>();

                for (var i = 0; i < reviewsForProduct; i++)
                {
                    var customerId = customerAccountIds[rng.Next(customerAccountIds.Count)];
                    if (!usedCustomers.Add(customerId)) continue;

                    var rating = rng.Next(3, 6); // 3..5
                    context.ProductReviews.Add(new ProductReview
                    {
                        ProductId = p.Id,
                        AccountId = customerId,
                        Rating = rating,
                        Comment = rating >= 5 ? "Sản phẩm rất tốt!" : "Sản phẩm ổn trong tầm giá.",
                        CreatedAt = nowUtc.AddDays(-rng.Next(0, 120)),
                        UpdatedAt = nowUtc
                    });
                }
            }

            await context.SaveChangesAsync(cancellationToken);
        }

        // Recompute aggregates based on actual reviews.
        var ratingAgg = await context.ProductReviews
            .AsNoTracking()
            .GroupBy(r => r.ProductId)
            .Select(g => new { ProductId = g.Key, Avg = g.Average(x => (decimal)x.Rating), Cnt = g.Count() })
            .ToListAsync(cancellationToken);

        if (ratingAgg.Count != 0)
        {
            var productIds = ratingAgg.Select(x => x.ProductId).ToList();
            var productsToUpdate = await context.Products
                .Where(p => productIds.Contains(p.Id))
                .ToListAsync(cancellationToken);

            var map = ratingAgg.ToDictionary(x => x.ProductId);
            foreach (var p in productsToUpdate)
            {
                if (!map.TryGetValue(p.Id, out var agg)) continue;
                p.AverageRating = decimal.Round(agg.Avg, 2);
                p.ReviewCount = agg.Cnt;
                p.UpdatedAt = nowUtc;
            }

            await context.SaveChangesAsync(cancellationToken);
        }

        var totalProducts = await context.Products.CountAsync(cancellationToken);
        var totalCategories = await context.Categories.CountAsync(cancellationToken);
        var totalSizes = await context.Sizes.CountAsync(cancellationToken);
        var totalVariants = await context.ProductVariants.CountAsync(cancellationToken);
        var totalProductImages = await context.ProductImages.CountAsync(cancellationToken);
        var totalAccounts = await context.Accounts.CountAsync(cancellationToken);
        var totalProfiles = await context.UserProfiles.CountAsync(cancellationToken);
        var totalGuides = await context.SizeGuides.CountAsync(cancellationToken);
        var totalReviews = await context.ProductReviews.CountAsync(cancellationToken);
        var totalCarts = await context.Carts.CountAsync(cancellationToken);
        var totalCartItems = await context.CartItems.CountAsync(cancellationToken);
        var totalOrders = await context.Orders.CountAsync(cancellationToken);
        var totalOrderItems = await context.OrderItems.CountAsync(cancellationToken);

        logger.LogInformation(
            "Seed complete: Products={Products}, Categories={Categories}, Sizes={Sizes}, Variants={Variants}, ProductImages={ProductImages}, Guides={Guides}, Reviews={Reviews}, Carts={Carts}, CartItems={CartItems}, Orders={Orders}, OrderItems={OrderItems}, Accounts={Accounts}, UserProfiles={UserProfiles}.",
            totalProducts,
            totalCategories,
            totalSizes,
            totalVariants,
            totalProductImages,
            totalGuides,
            totalReviews,
            totalCarts,
            totalCartItems,
            totalOrders,
            totalOrderItems,
            totalAccounts,
            totalProfiles);
    }

    private static bool IsTruthy(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        value = value.Trim();

        return value.Equals("1", StringComparison.OrdinalIgnoreCase)
            || value.Equals("true", StringComparison.OrdinalIgnoreCase)
            || value.Equals("yes", StringComparison.OrdinalIgnoreCase)
            || value.Equals("y", StringComparison.OrdinalIgnoreCase)
            || value.Equals("on", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task ResetCatalogAsync(DataContext context, ILogger logger, CancellationToken cancellationToken)
    {
        logger.LogWarning("RESET_SEED is enabled. Wiping catalog data (products/categories + dependent tables) before seeding.");

        // Delete in FK-safe order.
        await context.InventoryTransactions.ExecuteDeleteAsync(cancellationToken);
        await context.CartItems.ExecuteDeleteAsync(cancellationToken);
        await context.Carts.ExecuteDeleteAsync(cancellationToken);
        await context.OrderTrackingLogs.ExecuteDeleteAsync(cancellationToken);
        await context.OrderItems.ExecuteDeleteAsync(cancellationToken);
        await context.Orders.ExecuteDeleteAsync(cancellationToken);
        await context.ProductReviews.ExecuteDeleteAsync(cancellationToken);
        await context.SizeGuideDetails.ExecuteDeleteAsync(cancellationToken);
        await context.SizeGuides.ExecuteDeleteAsync(cancellationToken);
        await context.ProductImages.ExecuteDeleteAsync(cancellationToken);
        await context.VariantImages.ExecuteDeleteAsync(cancellationToken);
        await context.ProductVariants.ExecuteDeleteAsync(cancellationToken);
        await context.Products.ExecuteDeleteAsync(cancellationToken);

        // Categories is self-referencing; delete bottom-up.
        while (true)
        {
            var cats = await context.Categories
                .AsNoTracking()
                .Select(c => new { c.Id, c.ParentId })
                .ToListAsync(cancellationToken);

            if (cats.Count == 0) break;

            var parentIds = cats
                .Where(c => c.ParentId.HasValue)
                .Select(c => c.ParentId!.Value)
                .ToHashSet();

            var leafIds = cats
                .Where(c => !parentIds.Contains(c.Id))
                .Select(c => c.Id)
                .ToList();

            if (leafIds.Count == 0) break;

            await context.Categories
                .Where(c => leafIds.Contains(c.Id))
                .ExecuteDeleteAsync(cancellationToken);
        }

        await context.Colors.ExecuteDeleteAsync(cancellationToken);
        await context.Sizes.ExecuteDeleteAsync(cancellationToken);
        await context.Vouchers.ExecuteDeleteAsync(cancellationToken);
    }
}
