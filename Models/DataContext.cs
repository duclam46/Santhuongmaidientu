using Microsoft.EntityFrameworkCore;

namespace DOAN_BANHANG.Models;

public class DataContext : DbContext
{
    public DataContext(DbContextOptions<DataContext> options) : base(options)
    {
    }

    public DbSet<Category> Categories => Set<Category>();

    public DbSet<Account> Accounts => Set<Account>();

    public DbSet<UserProfile> UserProfiles => Set<UserProfile>();

    public DbSet<UserAddress> UserAddresses => Set<UserAddress>();

    public DbSet<Product> Products => Set<Product>();

    public DbSet<Color> Colors => Set<Color>();

    public DbSet<ProductVariant> ProductVariants => Set<ProductVariant>();

    public DbSet<Size> Sizes => Set<Size>();

    public DbSet<ProductImage> ProductImages => Set<ProductImage>();

    public DbSet<VariantImage> VariantImages => Set<VariantImage>();

    public DbSet<Cart> Carts => Set<Cart>();

    public DbSet<CartItem> CartItems => Set<CartItem>();

    public DbSet<Order> Orders => Set<Order>();

    public DbSet<Voucher> Vouchers => Set<Voucher>();

    public DbSet<OrderItem> OrderItems => Set<OrderItem>();

    public DbSet<OrderTrackingLog> OrderTrackingLogs => Set<OrderTrackingLog>();

    public DbSet<InventoryTransaction> InventoryTransactions => Set<InventoryTransaction>();

    public DbSet<ProductReview> ProductReviews => Set<ProductReview>();

    public DbSet<SizeGuide> SizeGuides => Set<SizeGuide>();

    public DbSet<SizeGuideDetail> SizeGuideDetails => Set<SizeGuideDetail>();

    public DbSet<WishlistItem> WishlistItems => Set<WishlistItem>();

    public DbSet<Warehouse> Warehouses => Set<Warehouse>();

    public DbSet<WarehouseRoute> WarehouseRoutes => Set<WarehouseRoute>();

    public DbSet<PartnerApplication> PartnerApplications => Set<PartnerApplication>();

    public DbSet<BlogPost> BlogPosts => Set<BlogPost>();

    public DbSet<BlogComment> BlogComments => Set<BlogComment>();

    public DbSet<BlogPostProduct> BlogPostProducts => Set<BlogPostProduct>();

    // ── Messaging ──
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<SystemNotification> SystemNotifications => Set<SystemNotification>();
    public DbSet<SystemNotificationRead> SystemNotificationReads => Set<SystemNotificationRead>();

    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();

    // ── Wallet ──
    public DbSet<Wallet> Wallets => Set<Wallet>();
    public DbSet<WalletTransaction> WalletTransactions => Set<WalletTransaction>();

    // ── Return Requests ──
    public DbSet<ReturnRequest> ReturnRequests => Set<ReturnRequest>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Category>(entity =>
        {
            entity.ToTable("Categories");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Name).HasMaxLength(150).IsRequired();
            entity.Property(x => x.Slug).HasMaxLength(150).IsRequired();

            entity.Property(x => x.SortOrder).HasDefaultValue(0);
            entity.Property(x => x.IsActive).HasDefaultValue(true);
            entity.Property(x => x.IsDeleted).HasDefaultValue(false);

            entity.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.Property(x => x.UpdatedAt).HasDefaultValueSql("SYSUTCDATETIME()");

            entity.HasMany(x => x.Children)
                .WithOne(x => x.Parent)
                .HasForeignKey(x => x.ParentId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(x => x.Slug).IsUnique();
        });

        modelBuilder.Entity<Account>(entity =>
        {
            entity.ToTable("Accounts");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Email).HasMaxLength(255).IsRequired();
            entity.Property(x => x.PasswordHash).HasMaxLength(255);
            entity.Property(x => x.AuthProvider).HasMaxLength(50);
            entity.Property(x => x.AuthProviderUserId).HasMaxLength(255);
            entity.Property(x => x.Role).HasMaxLength(30).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(30).IsRequired();

            entity.Property(x => x.IsDeleted).HasDefaultValue(false);

            entity.Property(x => x.CreatedAt)
                .HasColumnType("datetime2")
                .HasDefaultValueSql("SYSUTCDATETIME()");


            entity.HasIndex(x => x.Email).IsUnique();

            entity.HasOne(x => x.Profile)
                .WithOne(p => p.Account)
                .HasForeignKey<UserProfile>(p => p.AccountId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PartnerApplication>(entity =>
        {
            entity.ToTable("PartnerApplications");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Email).HasMaxLength(255).IsRequired();
            entity.Property(x => x.FullName).HasMaxLength(150);
            entity.Property(x => x.PhoneNumber).HasMaxLength(20);
            entity.Property(x => x.ShopName).HasMaxLength(200);
            entity.Property(x => x.Address).HasMaxLength(500);

            entity.Property(x => x.ProvinceName).HasMaxLength(100);
            entity.Property(x => x.DistrictName).HasMaxLength(100);
            entity.Property(x => x.WardCode).HasMaxLength(50);
            entity.Property(x => x.WardName).HasMaxLength(100);
            entity.Property(x => x.StreetAddress).HasMaxLength(500);

            entity.Property(x => x.Status).HasMaxLength(30).IsRequired();
            entity.Property(x => x.RejectionReason).HasMaxLength(500);

            entity.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.Property(x => x.UpdatedAt).HasDefaultValueSql("SYSUTCDATETIME()");

            entity.HasIndex(x => x.AccountId);
            entity.HasIndex(x => x.Email);
            entity.HasIndex(x => x.Status);
            entity.HasIndex(x => x.CreatedAt);
        });

        modelBuilder.Entity<UserProfile>(entity =>
        {
            entity.ToTable("UserProfiles");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.FullName).HasMaxLength(150);
            entity.Property(x => x.PhoneNumber).HasMaxLength(20);
            entity.Property(x => x.AvatarUrl).HasMaxLength(500);
            entity.Property(x => x.ShopName).HasMaxLength(200);
            entity.Property(x => x.ShopSlug).HasMaxLength(200);
            entity.Property(x => x.ShopLogoUrl).HasMaxLength(500);
            entity.Property(x => x.Address).HasMaxLength(500);

            entity.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.Property(x => x.UpdatedAt).HasDefaultValueSql("SYSUTCDATETIME()");

            entity.HasIndex(x => x.AccountId).IsUnique();

            entity.HasIndex(x => x.ShopSlug)
                .IsUnique()
                .HasFilter("[ShopSlug] IS NOT NULL");
        });

        modelBuilder.Entity<Product>(entity =>
        {
            entity.ToTable("Products");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Name).HasMaxLength(255).IsRequired();
            entity.Property(x => x.Slug).HasMaxLength(200).IsRequired();
            entity.Property(x => x.ShortDescription).HasMaxLength(500);
            entity.Property(x => x.AverageRating)
                .HasColumnType("decimal(3,2)")
                .HasDefaultValue(0m);
            entity.Property(x => x.Status).HasMaxLength(30).IsRequired();

            entity.Property(x => x.IsNewArrival).HasDefaultValue(false);

            entity.Property(x => x.ReviewCount)
                .HasDefaultValue(0);

            entity.Property(x => x.IsDeleted).HasDefaultValue(false);

            entity.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.Property(x => x.UpdatedAt).HasDefaultValueSql("SYSUTCDATETIME()");

            entity.HasIndex(x => x.Slug).IsUnique();

            entity.HasIndex(x => x.ShopId);

            entity.HasOne(x => x.ShopAccount)
                .WithMany()
                .HasForeignKey(x => x.ShopId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(x => x.CategoryId);

            entity.HasOne(x => x.Category)
                .WithMany()
                .HasForeignKey(x => x.CategoryId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasMany(x => x.Variants)
                .WithOne(v => v.Product)
                .HasForeignKey(v => v.ProductId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(x => x.SizeGuides)
                .WithOne(g => g.Product)
                .HasForeignKey(g => g.ProductId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(x => x.Reviews)
                .WithOne(r => r.Product)
                .HasForeignKey(r => r.ProductId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Color>(entity =>
        {
            entity.ToTable("Colors");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(50).IsRequired();
            entity.Property(x => x.Code).HasMaxLength(20);
            entity.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        });

        modelBuilder.Entity<WishlistItem>(entity =>
        {
            entity.ToTable("WishlistItems");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");

            entity.HasIndex(x => new { x.AccountId, x.ProductId }).IsUnique();

            entity.HasOne(x => x.Account)
                .WithMany()
                .HasForeignKey(x => x.AccountId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.Product)
                .WithMany()
                .HasForeignKey(x => x.ProductId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Size>(entity =>
        {
            entity.ToTable("Sizes");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Name).HasMaxLength(50).IsRequired();
            entity.Property(x => x.SortOrder).HasDefaultValue(0);
            entity.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        });

        modelBuilder.Entity<ProductVariant>(entity =>
        {
            entity.ToTable("ProductVariants");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Sku).HasMaxLength(100).IsRequired();
            entity.HasIndex(x => x.Sku).IsUnique();

            entity.Property(x => x.Price).HasColumnType("decimal(18,2)");
            entity.Property(x => x.SalePrice).HasColumnType("decimal(18,2)");

            entity.Property(x => x.StockQuantity).HasDefaultValue(0);
            entity.Property(x => x.ReservedStock).HasDefaultValue(0);
            entity.Property(x => x.IsDefault).HasDefaultValue(false);
            entity.Property(x => x.IsActive).HasDefaultValue(true);
            entity.Property(x => x.IsDeleted).HasDefaultValue(false);

            entity.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.Property(x => x.UpdatedAt).HasDefaultValueSql("SYSUTCDATETIME()");

            entity.HasOne(x => x.Color)
                .WithMany(c => c.Variants)
                .HasForeignKey(x => x.ColorId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.Size)
                .WithMany(s => s.Variants)
                .HasForeignKey(x => x.SizeId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.Product)
                .WithMany(p => p.Variants)
                .HasForeignKey(x => x.ProductId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ProductImage>(entity =>
        {
            entity.ToTable("ProductImages");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.ImageUrl).HasMaxLength(500).IsRequired();
            entity.Property(x => x.IsPrimary).HasDefaultValue(false);
            entity.Property(x => x.SortOrder).HasDefaultValue(0);
            entity.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");

            entity.HasOne(x => x.Product)
                .WithMany(p => p.Images)
                .HasForeignKey(x => x.ProductId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<VariantImage>(entity =>
        {
            entity.ToTable("VariantImages");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ImageUrl).HasMaxLength(500).IsRequired();
            entity.Property(x => x.IsPrimary).HasDefaultValue(false);
            entity.Property(x => x.SortOrder).HasDefaultValue(0);
            entity.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");

            entity.HasOne(x => x.Variant)
                .WithMany(v => v.Images)
                .HasForeignKey(x => x.VariantId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Cart>(entity =>
        {
            entity.ToTable("Carts");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.AccountId).IsUnique();

            entity.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.Property(x => x.UpdatedAt).HasDefaultValueSql("SYSUTCDATETIME()");

            entity.HasOne(x => x.Account)
                .WithMany()
                .HasForeignKey(x => x.AccountId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasMany(x => x.Items)
                .WithOne(i => i.Cart)
                .HasForeignKey(i => i.CartId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CartItem>(entity =>
        {
            entity.ToTable("CartItems");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.Property(x => x.UpdatedAt).HasDefaultValueSql("SYSUTCDATETIME()");

            entity.HasOne(x => x.Variant)
                .WithMany()
                .HasForeignKey(x => x.VariantId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Order>(entity =>
        {
            entity.ToTable("Orders");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.OrderCode).HasMaxLength(50).IsRequired();
            entity.HasIndex(x => x.OrderCode).IsUnique();

            entity.Property(x => x.SubTotal).HasColumnType("decimal(18,2)");
            entity.Property(x => x.ShippingFee).HasColumnType("decimal(18,2)");
            entity.Property(x => x.DiscountAmount).HasColumnType("decimal(18,2)").HasDefaultValue(0m);
            entity.Property(x => x.FinalAmount).HasColumnType("decimal(18,2)");

            entity.Property(x => x.OrderStatus).HasMaxLength(30).IsRequired();
            entity.Property(x => x.PaymentMethod).HasMaxLength(30).IsRequired();
            entity.Property(x => x.PaymentStatus).HasMaxLength(30).IsRequired();
            entity.Property(x => x.RecipientName).HasMaxLength(150).IsRequired();
            entity.Property(x => x.RecipientPhone).HasMaxLength(20).IsRequired();
            entity.Property(x => x.ShippingAddress).HasMaxLength(500).IsRequired();
            entity.Property(x => x.TrackingCode).HasMaxLength(100);

            entity.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.Property(x => x.UpdatedAt).HasDefaultValueSql("SYSUTCDATETIME()");

            entity.HasIndex(x => x.CustomerId);
            entity.HasIndex(x => x.ShopId);

            entity.HasOne(x => x.Customer)
                .WithMany()
                .HasForeignKey(x => x.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.Shop)
                .WithMany()
                .HasForeignKey(x => x.ShopId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.Voucher)
                .WithMany()
                .HasForeignKey(x => x.VoucherId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasMany(x => x.Items)
                .WithOne(i => i.Order)
                .HasForeignKey(i => i.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<OrderItem>(entity =>
        {
            entity.ToTable("OrderItems");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ProductName).HasMaxLength(255).IsRequired();
            entity.Property(x => x.ColorName).HasMaxLength(50);
            entity.Property(x => x.SizeName).HasMaxLength(50);
            entity.Property(x => x.UnitPrice).HasColumnType("decimal(18,2)");
            entity.Property(x => x.LineTotal).HasColumnType("decimal(18,2)");
            entity.HasIndex(x => x.OrderId);

            entity.HasOne(x => x.Variant)
                .WithMany()
                .HasForeignKey(x => x.VariantId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ProductReview>(entity =>
        {
            entity.ToTable("ProductReviews", tb =>
                tb.HasCheckConstraint("CK_ProductReviews_Rating_1_5", "[Rating] >= 1 AND [Rating] <= 5"));
            entity.HasKey(x => x.Id);

            entity.Property(x => x.SellerReply).HasMaxLength(1000);

            entity.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.Property(x => x.UpdatedAt).HasDefaultValueSql("SYSUTCDATETIME()");

            entity.HasOne(x => x.Product)
                .WithMany(p => p.Reviews)
                .HasForeignKey(x => x.ProductId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.Account)
                .WithMany()
                .HasForeignKey(x => x.AccountId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Voucher>(entity =>
        {
            entity.ToTable("Vouchers");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Code).HasMaxLength(50).IsRequired();
            entity.HasIndex(x => x.Code).IsUnique();

            entity.Property(x => x.DiscountType).HasMaxLength(20).IsRequired();
            entity.Property(x => x.DiscountValue).HasColumnType("decimal(18,2)");
            entity.Property(x => x.MinOrderValue).HasColumnType("decimal(18,2)").HasDefaultValue(0m);
            entity.Property(x => x.MaxDiscountValue).HasColumnType("decimal(18,2)");
            entity.Property(x => x.UsageLimit).HasDefaultValue(0);
            entity.Property(x => x.UsedCount).HasDefaultValue(0);
            entity.Property(x => x.IsActive).HasDefaultValue(true);
            entity.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");

            entity.HasOne(x => x.Shop)
                .WithMany()
                .HasForeignKey(x => x.ShopId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<OrderTrackingLog>(entity =>
        {
            entity.ToTable("OrderTrackingLogs");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Status).HasMaxLength(50).IsRequired();
            entity.Property(x => x.Note).HasMaxLength(255);
            entity.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");

            entity.HasIndex(x => x.OrderId);
            entity.HasOne(x => x.Order)
                .WithMany()
                .HasForeignKey(x => x.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<InventoryTransaction>(entity =>
        {
            entity.ToTable("InventoryTransactions");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TransactionType).HasMaxLength(50).IsRequired();
            entity.Property(x => x.ReferenceId).HasMaxLength(100);
            entity.Property(x => x.Note).HasMaxLength(255);
            entity.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");

            entity.HasIndex(x => x.VariantId);
            entity.HasOne(x => x.Variant)
                .WithMany()
                .HasForeignKey(x => x.VariantId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SizeGuide>(entity =>
        {
            entity.ToTable("SizeGuides");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.SizeLabel).HasMaxLength(50);
            entity.Property(x => x.HeightRange).HasMaxLength(50);
            entity.Property(x => x.WeightRange).HasMaxLength(50);
            entity.Property(x => x.Note).HasMaxLength(255);
            entity.Property(x => x.SortOrder).HasDefaultValue(0);
            entity.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.HasIndex(x => x.ProductId);

            entity.HasOne(x => x.Product)
                .WithMany(p => p.SizeGuides)
                .HasForeignKey(x => x.ProductId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(x => x.Details)
                .WithOne(d => d.SizeGuide)
                .HasForeignKey(d => d.SizeGuideId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SizeGuideDetail>(entity =>
        {
            entity.ToTable("SizeGuideDetails");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Value).HasMaxLength(50).IsRequired();
            entity.Property(x => x.Unit).HasMaxLength(20);
            entity.Property(x => x.SortOrder).HasDefaultValue(0);
            entity.HasIndex(x => x.SizeGuideId);
        });

        modelBuilder.Entity<UserAddress>(entity =>
        {
            entity.ToTable("UserAddresses");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.FullName).HasMaxLength(150).IsRequired();
            entity.Property(x => x.PhoneNumber).HasMaxLength(20).IsRequired();

            entity.Property(x => x.ProvinceName).HasMaxLength(100).IsRequired();
            entity.Property(x => x.DistrictName).HasMaxLength(100).IsRequired();
            entity.Property(x => x.WardCode).HasMaxLength(50).IsRequired();
            entity.Property(x => x.WardName).HasMaxLength(100).IsRequired();
            entity.Property(x => x.StreetAddress).HasMaxLength(500).IsRequired();

            entity.Property(x => x.AddressType)
                .HasConversion<int>()
                .HasDefaultValue(AddressType.Personal);

            entity.Property(x => x.IsDefault).HasDefaultValue(false);

            entity.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.Property(x => x.UpdatedAt).HasDefaultValueSql("SYSUTCDATETIME()");

            entity.HasIndex(x => x.AccountId);
            entity.HasIndex(x => new { x.AccountId, x.AddressType, x.IsDefault })
                .IsUnique()
                .HasFilter("[IsDefault] = 1");

            entity.HasOne(x => x.Account)
                .WithMany()
                .HasForeignKey(x => x.AccountId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Warehouse>(entity =>
        {
            entity.ToTable("Warehouses");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Code).HasMaxLength(50).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(255).IsRequired();
            entity.Property(x => x.NodeType).HasMaxLength(30).IsRequired();
            entity.Property(x => x.Region).HasMaxLength(20);

            entity.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.Property(x => x.UpdatedAt).HasDefaultValueSql("SYSUTCDATETIME()");

            entity.HasIndex(x => x.Code).IsUnique();
            entity.HasIndex(x => x.GhnProvinceId);
            entity.HasIndex(x => x.GhnDistrictId);

            entity.HasMany(x => x.Children)
                .WithOne(x => x.Parent)
                .HasForeignKey(x => x.ParentId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<WarehouseRoute>(entity =>
        {
            entity.ToTable("WarehouseRoutes");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.RouteKind).HasMaxLength(30).IsRequired();
            entity.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.Property(x => x.UpdatedAt).HasDefaultValueSql("SYSUTCDATETIME()");

            entity.HasOne(x => x.FromWarehouse)
                .WithMany()
                .HasForeignKey(x => x.FromWarehouseId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.ToWarehouse)
                .WithMany()
                .HasForeignKey(x => x.ToWarehouseId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(x => new { x.FromWarehouseId, x.ToWarehouseId }).IsUnique();
        });

        modelBuilder.Entity<BlogPost>(entity =>
        {
            entity.ToTable("BlogPosts");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Title).HasMaxLength(300).IsRequired();
            entity.Property(x => x.Slug).HasMaxLength(300).IsRequired();
            entity.Property(x => x.Summary).HasMaxLength(500);
            entity.Property(x => x.CoverImageUrl).HasMaxLength(500);
            entity.Property(x => x.Status).HasMaxLength(30).IsRequired();
            entity.Property(x => x.ViewCount).HasDefaultValue(0);
            entity.Property(x => x.IsDeleted).HasDefaultValue(false);

            entity.Property(x => x.PublishedAt)
                .HasColumnType("datetime2");

            entity.Property(x => x.CreatedAt)
                .HasColumnType("datetime2")
                .HasDefaultValueSql("SYSUTCDATETIME()");

            entity.Property(x => x.UpdatedAt)
                .HasColumnType("datetime2")
                .HasDefaultValueSql("SYSUTCDATETIME()");

            entity.HasIndex(x => x.Slug).IsUnique();
            entity.HasIndex(x => x.ShopId);
            entity.HasIndex(x => x.Status);
            entity.HasIndex(x => x.PublishedAt);

            entity.HasOne(x => x.Shop)
                .WithMany()
                .HasForeignKey(x => x.ShopId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasMany(x => x.Comments)
                .WithOne(c => c.BlogPost)
                .HasForeignKey(c => c.BlogPostId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(x => x.BlogPostProducts)
                .WithOne(bp => bp.BlogPost)
                .HasForeignKey(bp => bp.BlogPostId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<BlogComment>(entity =>
        {
            entity.ToTable("BlogComments");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Content).HasMaxLength(1000).IsRequired();
            entity.Property(x => x.IsDeleted).HasDefaultValue(false);

            entity.Property(x => x.CreatedAt)
                .HasColumnType("datetime2")
                .HasDefaultValueSql("SYSUTCDATETIME()");

            entity.Property(x => x.UpdatedAt)
                .HasColumnType("datetime2")
                .HasDefaultValueSql("SYSUTCDATETIME()");


            entity.HasIndex(x => x.BlogPostId);
            entity.HasIndex(x => x.AccountId);

            entity.HasOne(x => x.Account)
                .WithMany()
                .HasForeignKey(x => x.AccountId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasMany(x => x.Replies)
                .WithOne(x => x.ParentComment)
                .HasForeignKey(x => x.ParentCommentId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<BlogPostProduct>(entity =>
        {
            entity.ToTable("BlogPostProducts");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.SortOrder).HasDefaultValue(0);

            entity.HasIndex(x => new { x.BlogPostId, x.ProductId }).IsUnique();
            entity.HasIndex(x => x.BlogPostId);

            entity.HasOne(x => x.Product)
                .WithMany()
                .HasForeignKey(x => x.ProductId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ── Messaging: Conversations ──
        modelBuilder.Entity<Conversation>(entity =>
        {
            entity.ToTable("Conversations");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.LastMessageAt)
                .HasColumnType("datetime2")
                .HasDefaultValueSql("SYSUTCDATETIME()");
            entity.Property(x => x.CreatedAt)
                .HasColumnType("datetime2")
                .HasDefaultValueSql("SYSUTCDATETIME()");
            entity.Property(x => x.IsArchivedByShop).HasDefaultValue(false);
            entity.Property(x => x.IsArchivedByCustomer).HasDefaultValue(false);

            // Unique: 1 pair (Customer, Shop) = 1 conversation
            entity.HasIndex(x => new { x.CustomerId, x.ShopId }).IsUnique();
            entity.HasIndex(x => x.ShopId);
            entity.HasIndex(x => x.LastMessageAt);

            entity.HasOne(x => x.Customer)
                .WithMany()
                .HasForeignKey(x => x.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.Shop)
                .WithMany()
                .HasForeignKey(x => x.ShopId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasMany(x => x.Messages)
                .WithOne(m => m.Conversation)
                .HasForeignKey(m => m.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ── Messaging: Messages ──
        modelBuilder.Entity<Message>(entity =>
        {
            entity.ToTable("Messages");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Content).HasMaxLength(2000);
            entity.Property(x => x.MessageType).HasMaxLength(30).HasDefaultValue("text");
            entity.Property(x => x.IsReadByCustomer).HasDefaultValue(false);
            entity.Property(x => x.IsReadByShop).HasDefaultValue(false);
            entity.Property(x => x.IsDeleted).HasDefaultValue(false);

            entity.Property(x => x.CreatedAt)
                .HasColumnType("datetime2")
                .HasDefaultValueSql("SYSUTCDATETIME()");

            entity.HasIndex(x => x.ConversationId);
            entity.HasIndex(x => x.CreatedAt);

            entity.HasOne(x => x.Sender)
                .WithMany()
                .HasForeignKey(x => x.SenderId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.AttachedProduct)
                .WithMany()
                .HasForeignKey(x => x.AttachedProductId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(x => x.AttachedVoucher)
                .WithMany()
                .HasForeignKey(x => x.AttachedVoucherId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // ── Messaging: SystemNotifications ──
        modelBuilder.Entity<SystemNotification>(entity =>
        {
            entity.ToTable("SystemNotifications");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Title).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Content).HasMaxLength(2000).IsRequired();
            entity.Property(x => x.TargetType).HasMaxLength(20).IsRequired();
            entity.Property(x => x.TargetRole).HasMaxLength(30);

            entity.Property(x => x.CreatedAt)
                .HasColumnType("datetime2")
                .HasDefaultValueSql("SYSUTCDATETIME()");

            entity.HasIndex(x => x.TargetType);
            entity.HasIndex(x => x.CreatedAt);

            entity.HasOne(x => x.SentByAdmin)
                .WithMany()
                .HasForeignKey(x => x.SentByAdminId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.TargetAccount)
                .WithMany()
                .HasForeignKey(x => x.TargetAccountId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasMany(x => x.Reads)
                .WithOne(r => r.SystemNotification)
                .HasForeignKey(r => r.SystemNotificationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ── Messaging: SystemNotificationReads ──
        modelBuilder.Entity<SystemNotificationRead>(entity =>
        {
            entity.ToTable("SystemNotificationReads");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.ReadAt)
                .HasColumnType("datetime2")
                .HasDefaultValueSql("SYSUTCDATETIME()");

            entity.HasIndex(x => new { x.SystemNotificationId, x.AccountId }).IsUnique();
            entity.HasIndex(x => x.AccountId);

            entity.HasOne(x => x.Account)
                .WithMany()
                .HasForeignKey(x => x.AccountId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ── Wallet ──
        modelBuilder.Entity<Wallet>(entity =>
        {
            entity.ToTable("Wallets");
            entity.HasKey(x => x.WalletId);

            entity.Property(x => x.Balance)
                .HasColumnType("decimal(18,2)")
                .HasDefaultValue(0m);

            entity.Property(x => x.Status).HasDefaultValue(1);

            entity.Property(x => x.UpdatedAt)
                .HasColumnType("datetime2")
                .HasDefaultValueSql("SYSUTCDATETIME()");

            // Mỗi user chỉ có 1 ví
            entity.HasIndex(x => x.UserId).IsUnique();

            entity.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasMany(x => x.Transactions)
                .WithOne(t => t.Wallet)
                .HasForeignKey(t => t.WalletId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ── WalletTransactions ──
        modelBuilder.Entity<WalletTransaction>(entity =>
        {
            entity.ToTable("WalletTransactions");
            entity.HasKey(x => x.TransactionId);

            entity.Property(x => x.Amount).HasColumnType("decimal(18,2)");
            entity.Property(x => x.BalanceAfter).HasColumnType("decimal(18,2)");

            entity.Property(x => x.TransactionType).HasMaxLength(30).IsRequired();
            entity.Property(x => x.ReferenceId).HasMaxLength(100);
            entity.Property(x => x.Description).HasMaxLength(500);

            entity.Property(x => x.CreatedAt)
                .HasColumnType("datetime2")
                .HasDefaultValueSql("SYSUTCDATETIME()");

            entity.HasIndex(x => x.WalletId);
            entity.HasIndex(x => x.CreatedAt);
            entity.HasIndex(x => x.TransactionType);
        });

        // ── ReturnRequests ──
        modelBuilder.Entity<ReturnRequest>(entity =>
        {
            entity.ToTable("ReturnRequests");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Reason).HasMaxLength(500).IsRequired();
            entity.Property(x => x.MediaUrlsJson).HasMaxLength(4000);
            entity.Property(x => x.Status).HasMaxLength(30).IsRequired().HasDefaultValue("Pending");
            entity.Property(x => x.RejectionNote).HasMaxLength(500);
            entity.Property(x => x.ReturnTrackingCode).HasMaxLength(100);
            entity.Property(x => x.EstimatedPickupTime).HasMaxLength(100);

            entity.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.Property(x => x.UpdatedAt).HasDefaultValueSql("SYSUTCDATETIME()");

            entity.HasIndex(x => x.OrderId);
            entity.HasIndex(x => x.CustomerId);
            entity.HasIndex(x => x.Status);

            entity.HasOne(x => x.Order)
                .WithMany()
                .HasForeignKey(x => x.OrderId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.Customer)
                .WithMany()
                .HasForeignKey(x => x.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
