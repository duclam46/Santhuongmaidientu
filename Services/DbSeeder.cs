using DOAN_BANHANG.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.RegularExpressions;

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

        await ApplyMigrationsSafelyAsync(context, logger, cancellationToken);

        // Raw SQL fallback for Blog feature if migrations haven't been run manually
        try
        {
            // DIAGNOSTICS: Check column types and log them
            var diagnostics = await context.Database.SqlQueryRaw<string>(@"
                SELECT TABLE_NAME + '.' + COLUMN_NAME + ': ' + DATA_TYPE 
                FROM INFORMATION_SCHEMA.COLUMNS 
                WHERE TABLE_NAME IN ('BlogPosts', 'BlogComments', 'Accounts', 'UserProfiles') 
                AND COLUMN_NAME IN ('CreatedAt', 'UpdatedAt', 'PublishedAt')
            ").ToListAsync(cancellationToken);
            
            foreach (var diag in diagnostics)
            {
                logger.LogInformation("DATABASE SCHEMA DIAGNOSTIC: {Diag}", diag);
            }

            // AGGRESSIVE SCHEMA REPAIR: Fix columns if they are datetimeoffset
            await context.Database.ExecuteSqlRawAsync(@"
                DECLARE @sql NVARCHAR(MAX) = '';

                -- Fix BlogPosts.PublishedAt if it is datetimeoffset
                IF EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'BlogPosts' AND COLUMN_NAME = 'PublishedAt' AND DATA_TYPE = 'datetimeoffset')
                BEGIN
                    -- Drop default constraints first (if any)
                    SELECT @sql += 'ALTER TABLE BlogPosts DROP CONSTRAINT ' + d.name + ';'
                    FROM sys.default_constraints d
                    INNER JOIN sys.columns c ON d.parent_column_id = c.column_id AND d.parent_object_id = c.object_id
                    WHERE d.parent_object_id = OBJECT_ID('BlogPosts') AND c.name IN ('PublishedAt');

                    EXEC sp_executesql @sql;
                    SET @sql = '';

                    ALTER TABLE BlogPosts ALTER COLUMN PublishedAt DATETIME2 NULL;
                END
                
                -- Fix BlogPosts
                IF EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'BlogPosts' AND COLUMN_NAME = 'CreatedAt' AND DATA_TYPE = 'datetimeoffset')
                BEGIN
                    -- Drop default constraints first
                    SELECT @sql += 'ALTER TABLE BlogPosts DROP CONSTRAINT ' + d.name + ';'
                    FROM sys.default_constraints d
                    INNER JOIN sys.columns c ON d.parent_column_id = c.column_id AND d.parent_object_id = c.object_id
                    WHERE d.parent_object_id = OBJECT_ID('BlogPosts') AND c.name IN ('PublishedAt', 'CreatedAt', 'UpdatedAt');
                    
                    EXEC sp_executesql @sql;
                    SET @sql = '';

                    ALTER TABLE BlogPosts ALTER COLUMN PublishedAt DATETIME2 NULL;
                    ALTER TABLE BlogPosts ALTER COLUMN CreatedAt DATETIME2 NOT NULL;
                    ALTER TABLE BlogPosts ALTER COLUMN UpdatedAt DATETIME2 NOT NULL;
                    
                    ALTER TABLE BlogPosts ADD DEFAULT SYSUTCDATETIME() FOR CreatedAt;
                    ALTER TABLE BlogPosts ADD DEFAULT SYSUTCDATETIME() FOR UpdatedAt;
                END

                -- Fix BlogComments
                IF EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'BlogComments' AND COLUMN_NAME = 'CreatedAt' AND DATA_TYPE = 'datetimeoffset')
                BEGIN
                    SELECT @sql += 'ALTER TABLE BlogComments DROP CONSTRAINT ' + d.name + ';'
                    FROM sys.default_constraints d
                    INNER JOIN sys.columns c ON d.parent_column_id = c.column_id AND d.parent_object_id = c.object_id
                    WHERE d.parent_object_id = OBJECT_ID('BlogComments') AND c.name IN ('CreatedAt', 'UpdatedAt');
                    
                    EXEC sp_executesql @sql;
                    SET @sql = '';

                    ALTER TABLE BlogComments ALTER COLUMN CreatedAt DATETIME2 NOT NULL;
                    ALTER TABLE BlogComments ALTER COLUMN UpdatedAt DATETIME2 NOT NULL;
                    
                    ALTER TABLE BlogComments ADD DEFAULT SYSUTCDATETIME() FOR CreatedAt;
                    ALTER TABLE BlogComments ADD DEFAULT SYSUTCDATETIME() FOR UpdatedAt;
                END

                -- Ensure tables exist with correct types
                IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='BlogPosts' AND xtype='U')
                BEGIN
                    CREATE TABLE BlogPosts (
                        Id INT PRIMARY KEY IDENTITY(1,1),
                        ShopId INT NOT NULL,
                        Title NVARCHAR(300) NOT NULL,
                        Slug NVARCHAR(300) NOT NULL,
                        Summary NVARCHAR(500),
                        Content NVARCHAR(MAX),
                        CoverImageUrl NVARCHAR(500),
                        Status NVARCHAR(30) NOT NULL,
                        ViewCount INT NOT NULL DEFAULT 0,
                        IsDeleted BIT NOT NULL DEFAULT 0,
                        PublishedAt DATETIME2 NULL,
                        CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
                        UpdatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
                    );
                    CREATE UNIQUE INDEX IX_BlogPosts_Slug ON BlogPosts(Slug);
                END

                IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='BlogComments' AND xtype='U')
                BEGIN
                    CREATE TABLE BlogComments (
                        Id INT PRIMARY KEY IDENTITY(1,1),
                        BlogPostId INT NOT NULL,
                        AccountId INT NOT NULL,
                        ParentCommentId INT NULL,
                        Content NVARCHAR(1000) NOT NULL,
                        IsDeleted BIT NOT NULL DEFAULT 0,
                        CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
                        UpdatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
                    );
                END

                IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='BlogPostProducts' AND xtype='U')
                BEGIN
                    CREATE TABLE BlogPostProducts (
                        Id INT PRIMARY KEY IDENTITY(1,1),
                        BlogPostId INT NOT NULL,
                        ProductId INT NOT NULL,
                        SortOrder INT NOT NULL DEFAULT 0
                    );
                    CREATE UNIQUE INDEX IX_BlogPostProducts_BlogPostId_ProductId ON BlogPostProducts(BlogPostId, ProductId);
                END

                -- SystemSettings
                IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='SystemSettings' AND xtype='U')
                BEGIN
                    CREATE TABLE SystemSettings (
                        Id NVARCHAR(100) PRIMARY KEY,
                        Value NVARCHAR(MAX) NOT NULL,
                        Description NVARCHAR(500),
                        Type NVARCHAR(50) NOT NULL DEFAULT 'string',
                        UpdatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
                    );
                END

                -- ReturnRequests
                IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='ReturnRequests' AND xtype='U')
                BEGIN
                    CREATE TABLE ReturnRequests (
                        Id INT PRIMARY KEY IDENTITY(1,1),
                        OrderId INT NOT NULL,
                        CustomerId INT NOT NULL,
                        Reason NVARCHAR(500) NOT NULL,
                        MediaUrlsJson NVARCHAR(4000) NULL,
                        Status NVARCHAR(30) NOT NULL DEFAULT 'Pending',
                        RejectionNote NVARCHAR(500) NULL,
                        ReturnTrackingCode NVARCHAR(100) NULL,
                        EstimatedPickupTime NVARCHAR(100) NULL,
                        CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
                        UpdatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
                        CONSTRAINT FK_ReturnRequests_Orders_OrderId FOREIGN KEY (OrderId) REFERENCES Orders(Id) ON DELETE CASCADE,
                        CONSTRAINT FK_ReturnRequests_Accounts_CustomerId FOREIGN KEY (CustomerId) REFERENCES Accounts(Id)
                    );
                    CREATE INDEX IX_ReturnRequests_OrderId ON ReturnRequests(OrderId);
                    CREATE INDEX IX_ReturnRequests_CustomerId ON ReturnRequests(CustomerId);
                    CREATE INDEX IX_ReturnRequests_Status ON ReturnRequests(Status);
                END

                -- ==========================================================
                -- ORDER DISPUTES & 3-PARTY ARBITRATION (SHOPEE-LIKE + AI)
                -- ==========================================================
                IF OBJECT_ID('dbo.OrderDisputes', 'U') IS NULL
                BEGIN
                    CREATE TABLE [dbo].[OrderDisputes] (
                        [Id] INT IDENTITY(1,1) PRIMARY KEY,
                        [DisputeCode] NVARCHAR(40) NOT NULL UNIQUE,
                        [OrderId] INT NOT NULL CONSTRAINT [FK_OrderDisputes_Orders] FOREIGN KEY REFERENCES [dbo].[Orders]([Id]) ON DELETE CASCADE,
                        [CustomerId] INT NOT NULL CONSTRAINT [FK_OrderDisputes_Customer] FOREIGN KEY REFERENCES [dbo].[Accounts]([Id]),
                        [ShopId] INT NOT NULL CONSTRAINT [FK_OrderDisputes_Shop] FOREIGN KEY REFERENCES [dbo].[Accounts]([Id]),
                        [Category] NVARCHAR(30) NOT NULL DEFAULT 'Product',
                        [ReasonTitle] NVARCHAR(150) NOT NULL,
                        [Description] NVARCHAR(2000) NOT NULL,
                        [RequestedResolution] NVARCHAR(40) NOT NULL DEFAULT 'ReturnAndRefund',
                        [RequestedAmount] DECIMAL(18,2) NOT NULL DEFAULT 0,
                        [ApprovedRefundAmount] DECIMAL(18,2) NOT NULL DEFAULT 0,
                        [EvidenceUrlsJson] NVARCHAR(4000) NULL,
                        [Status] NVARCHAR(40) NOT NULL DEFAULT 'Open',
                        [ShopResponseNote] NVARCHAR(1500) NULL,
                        [ShopEvidenceUrlsJson] NVARCHAR(4000) NULL,
                        [ShopRespondedAt] DATETIME2 NULL,
                        [AdminDecisionNote] NVARCHAR(1500) NULL,
                        [ResolvedByAdminId] INT NULL,
                        [ResolvedAt] DATETIME2 NULL,
                        [IsRefundedToWallet] BIT NOT NULL DEFAULT 0,
                        [CreatedAt] DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
                        [UpdatedAt] DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
                    );
                    CREATE INDEX IX_OrderDisputes_OrderId ON [dbo].[OrderDisputes]([OrderId]);
                    CREATE INDEX IX_OrderDisputes_CustomerId ON [dbo].[OrderDisputes]([CustomerId]);
                    CREATE INDEX IX_OrderDisputes_ShopId ON [dbo].[OrderDisputes]([ShopId]);
                    CREATE INDEX IX_OrderDisputes_Status ON [dbo].[OrderDisputes]([Status]);
                END

                IF OBJECT_ID('dbo.OrderDisputeMessages', 'U') IS NULL
                BEGIN
                    CREATE TABLE [dbo].[OrderDisputeMessages] (
                        [Id] INT IDENTITY(1,1) PRIMARY KEY,
                        [DisputeId] INT NOT NULL CONSTRAINT [FK_OrderDisputeMessages_Dispute] FOREIGN KEY REFERENCES [dbo].[OrderDisputes]([Id]) ON DELETE CASCADE,
                        [SenderAccountId] INT NOT NULL CONSTRAINT [FK_OrderDisputeMessages_Account] FOREIGN KEY REFERENCES [dbo].[Accounts]([Id]),
                        [SenderRole] NVARCHAR(20) NOT NULL DEFAULT 'Customer',
                        [Content] NVARCHAR(2000) NOT NULL,
                        [AttachmentUrl] NVARCHAR(500) NULL,
                        [CreatedAt] DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
                    );
                    CREATE INDEX IX_OrderDisputeMessages_DisputeId ON [dbo].[OrderDisputeMessages]([DisputeId]);
                END

                -- ==========================================================
                -- MULTI-INDUSTRY E-COMMERCE SCHEMA UPGRADE
                -- ==========================================================
                -- 1. Categories columns
                IF COL_LENGTH('dbo.Categories', 'CommissionRate') IS NULL
                    ALTER TABLE [dbo].[Categories] ADD [CommissionRate] DECIMAL(5, 2) NULL;
                IF COL_LENGTH('dbo.Categories', 'MaxCommissionAmount') IS NULL
                    ALTER TABLE [dbo].[Categories] ADD [MaxCommissionAmount] DECIMAL(18, 2) NULL;
                IF COL_LENGTH('dbo.Categories', 'IconUrl') IS NULL
                    ALTER TABLE [dbo].[Categories] ADD [IconUrl] NVARCHAR(500) NULL;
                IF COL_LENGTH('dbo.Categories', 'HasSizeGuide') IS NULL
                    ALTER TABLE [dbo].[Categories] ADD [HasSizeGuide] BIT NOT NULL DEFAULT 0;
                IF COL_LENGTH('dbo.Categories', 'IsReturnableDefault') IS NULL
                    ALTER TABLE [dbo].[Categories] ADD [IsReturnableDefault] BIT NOT NULL DEFAULT 1;

                -- 2. Brands table
                IF OBJECT_ID('dbo.Brands', 'U') IS NULL
                BEGIN
                    CREATE TABLE [dbo].[Brands] (
                        [Id] INT IDENTITY(1,1) PRIMARY KEY,
                        [Name] NVARCHAR(150) NOT NULL,
                        [Slug] NVARCHAR(150) NOT NULL UNIQUE,
                        [LogoUrl] NVARCHAR(500) NULL,
                        [IsActive] BIT NOT NULL DEFAULT 1,
                        [CreatedAt] DATETIME2(7) NOT NULL DEFAULT SYSUTCDATETIME()
                    );
                END

                -- 3. Products columns
                IF COL_LENGTH('dbo.Products', 'BrandId') IS NULL
                    ALTER TABLE [dbo].[Products] ADD [BrandId] INT NULL CONSTRAINT [FK_Products_Brands] FOREIGN KEY REFERENCES [dbo].[Brands]([Id]);
                IF COL_LENGTH('dbo.Products', 'WarrantyMonths') IS NULL
                    ALTER TABLE [dbo].[Products] ADD [WarrantyMonths] INT NOT NULL DEFAULT 0;
                IF COL_LENGTH('dbo.Products', 'WarrantyType') IS NULL
                    ALTER TABLE [dbo].[Products] ADD [WarrantyType] NVARCHAR(100) NULL;
                IF COL_LENGTH('dbo.Products', 'IsReturnable') IS NULL
                    ALTER TABLE [dbo].[Products] ADD [IsReturnable] BIT NOT NULL DEFAULT 1;

                -- 3b. Vouchers & Orders Multi-Scope / Settlement Upgrade
                IF COL_LENGTH('dbo.Vouchers', 'ScopeType') IS NULL
                    ALTER TABLE [dbo].[Vouchers] ADD [ScopeType] NVARCHAR(40) NOT NULL DEFAULT 'All';
                IF COL_LENGTH('dbo.Vouchers', 'TargetCategoryIds') IS NULL
                    ALTER TABLE [dbo].[Vouchers] ADD [TargetCategoryIds] NVARCHAR(1000) NULL;
                IF COL_LENGTH('dbo.Vouchers', 'TargetProductIds') IS NULL
                    ALTER TABLE [dbo].[Vouchers] ADD [TargetProductIds] NVARCHAR(2000) NULL;
                IF COL_LENGTH('dbo.Vouchers', 'MaxUsagePerUser') IS NULL
                    ALTER TABLE [dbo].[Vouchers] ADD [MaxUsagePerUser] INT NOT NULL DEFAULT 1;
                IF COL_LENGTH('dbo.Vouchers', 'TargetAudience') IS NULL
                    ALTER TABLE [dbo].[Vouchers] ADD [TargetAudience] NVARCHAR(30) NOT NULL DEFAULT 'Public';
                IF COL_LENGTH('dbo.Vouchers', 'ExcludeFlashSaleItems') IS NULL
                    ALTER TABLE [dbo].[Vouchers] ADD [ExcludeFlashSaleItems] BIT NOT NULL DEFAULT 0;

                IF COL_LENGTH('dbo.Orders', 'ShopDiscountAmount') IS NULL
                    ALTER TABLE [dbo].[Orders] ADD [ShopDiscountAmount] DECIMAL(18, 2) NOT NULL DEFAULT 0;
                IF COL_LENGTH('dbo.Orders', 'PlatformDiscountAmount') IS NULL
                    ALTER TABLE [dbo].[Orders] ADD [PlatformDiscountAmount] DECIMAL(18, 2) NOT NULL DEFAULT 0;

                -- 3c. Self-heal orphaned SalePrice on multi-variant items without Flash Sale schedule & soft-deleted variants
                IF OBJECT_ID('dbo.ProductVariants', 'U') IS NOT NULL
                BEGIN
                    UPDATE [dbo].[ProductVariants]
                    SET [SalePrice] = NULL
                    WHERE [SalePrice] IS NOT NULL
                      AND ([SaleStartDate] IS NULL OR [SaleEndDate] IS NULL)
                      AND ([OptionValue1Id] IS NOT NULL OR [OptionValue2Id] IS NOT NULL);

                    UPDATE [dbo].[ProductVariants]
                    SET [IsActive] = 0, [IsDefault] = 0
                    WHERE [IsDeleted] = 1 AND ([IsActive] = 1 OR [IsDefault] = 1);
                END

                -- 4. ProductOptions & ProductOptionValues tables
                IF OBJECT_ID('dbo.ProductOptions', 'U') IS NULL
                BEGIN
                    CREATE TABLE [dbo].[ProductOptions] (
                        [Id] INT IDENTITY(1,1) PRIMARY KEY,
                        [ProductId] INT NOT NULL CONSTRAINT [FK_ProductOptions_Products] FOREIGN KEY REFERENCES [dbo].[Products]([Id]) ON DELETE CASCADE,
                        [Name] NVARCHAR(100) NOT NULL,
                        [SortOrder] INT NOT NULL DEFAULT 0
                    );
                END

                IF OBJECT_ID('dbo.ProductOptionValues', 'U') IS NULL
                BEGIN
                    CREATE TABLE [dbo].[ProductOptionValues] (
                        [Id] INT IDENTITY(1,1) PRIMARY KEY,
                        [ProductOptionId] INT NOT NULL CONSTRAINT [FK_ProductOptionValues_Options] FOREIGN KEY REFERENCES [dbo].[ProductOptions]([Id]) ON DELETE CASCADE,
                        [Value] NVARCHAR(100) NOT NULL,
                        [ImageUrl] NVARCHAR(500) NULL
                    );
                END

                -- 5. ProductVariants columns
                IF COL_LENGTH('dbo.ProductVariants', 'OptionValue1Id') IS NULL
                    ALTER TABLE [dbo].[ProductVariants] ADD [OptionValue1Id] INT NULL CONSTRAINT [FK_Variants_OptVal1] FOREIGN KEY REFERENCES [dbo].[ProductOptionValues]([Id]);
                IF COL_LENGTH('dbo.ProductVariants', 'OptionValue2Id') IS NULL
                    ALTER TABLE [dbo].[ProductVariants] ADD [OptionValue2Id] INT NULL CONSTRAINT [FK_Variants_OptVal2] FOREIGN KEY REFERENCES [dbo].[ProductOptionValues]([Id]);
                IF COL_LENGTH('dbo.ProductVariants', 'WeightGram') IS NULL
                    ALTER TABLE [dbo].[ProductVariants] ADD [WeightGram] INT NOT NULL DEFAULT 300;
                IF COL_LENGTH('dbo.ProductVariants', 'LengthCm') IS NULL
                    ALTER TABLE [dbo].[ProductVariants] ADD [LengthCm] INT NOT NULL DEFAULT 10;
                IF COL_LENGTH('dbo.ProductVariants', 'WidthCm') IS NULL
                    ALTER TABLE [dbo].[ProductVariants] ADD [WidthCm] INT NOT NULL DEFAULT 10;
                IF COL_LENGTH('dbo.ProductVariants', 'HeightCm') IS NULL
                    ALTER TABLE [dbo].[ProductVariants] ADD [HeightCm] INT NOT NULL DEFAULT 10;

                -- 6. CategoryAttributes & ProductAttributeValues tables
                IF OBJECT_ID('dbo.CategoryAttributes', 'U') IS NULL
                BEGIN
                    CREATE TABLE [dbo].[CategoryAttributes] (
                        [Id] INT IDENTITY(1,1) PRIMARY KEY,
                        [CategoryId] INT NOT NULL CONSTRAINT [FK_CatAttr_Categories] FOREIGN KEY REFERENCES [dbo].[Categories]([Id]) ON DELETE CASCADE,
                        [Name] NVARCHAR(150) NOT NULL,
                        [Unit] NVARCHAR(50) NULL,
                        [IsFilterable] BIT NOT NULL DEFAULT 1,
                        [SortOrder] INT NOT NULL DEFAULT 0
                    );
                END

                IF OBJECT_ID('dbo.ProductAttributeValues', 'U') IS NULL
                BEGIN
                    CREATE TABLE [dbo].[ProductAttributeValues] (
                        [Id] INT IDENTITY(1,1) PRIMARY KEY,
                        [ProductId] INT NOT NULL CONSTRAINT [FK_ProdAttrVal_Products] FOREIGN KEY REFERENCES [dbo].[Products]([Id]) ON DELETE CASCADE,
                        [CategoryAttributeId] INT NOT NULL CONSTRAINT [FK_ProdAttrVal_CatAttr] FOREIGN KEY REFERENCES [dbo].[CategoryAttributes]([Id]),
                        [Value] NVARCHAR(255) NOT NULL
                    );
                END

                -- 7. OrderItems columns
                IF COL_LENGTH('dbo.OrderItems', 'VariantDescription') IS NULL
                    ALTER TABLE [dbo].[OrderItems] ADD [VariantDescription] NVARCHAR(255) NULL;
                IF COL_LENGTH('dbo.OrderItems', 'CommissionAmount') IS NULL
                    ALTER TABLE [dbo].[OrderItems] ADD [CommissionAmount] DECIMAL(18, 2) NOT NULL DEFAULT 0;
                IF COL_LENGTH('dbo.OrderItems', 'UnitCostPrice') IS NULL
                    ALTER TABLE [dbo].[OrderItems] ADD [UnitCostPrice] DECIMAL(18, 2) NOT NULL DEFAULT 0;

                -- 8. Products: IsDigital, DigitalContent, ManufacturingDate, ExpiryDate, ShippingCondition
                IF COL_LENGTH('dbo.Products', 'IsDigital') IS NULL
                    ALTER TABLE [dbo].[Products] ADD [IsDigital] BIT NOT NULL DEFAULT 0;
                IF COL_LENGTH('dbo.Products', 'DigitalContent') IS NULL
                    ALTER TABLE [dbo].[Products] ADD [DigitalContent] NVARCHAR(1000) NULL;
                IF COL_LENGTH('dbo.Products', 'ManufacturingDate') IS NULL
                    ALTER TABLE [dbo].[Products] ADD [ManufacturingDate] DATETIME2 NULL;
                IF COL_LENGTH('dbo.Products', 'ExpiryDate') IS NULL
                    ALTER TABLE [dbo].[Products] ADD [ExpiryDate] DATETIME2 NULL;
                IF COL_LENGTH('dbo.Products', 'ShippingCondition') IS NULL
                    ALTER TABLE [dbo].[Products] ADD [ShippingCondition] NVARCHAR(50) NULL DEFAULT N'Tiêu chuẩn';

                -- 9. ProductVariants: CostPrice
                IF COL_LENGTH('dbo.ProductVariants', 'CostPrice') IS NULL
                    ALTER TABLE [dbo].[ProductVariants] ADD [CostPrice] DECIMAL(18, 2) NOT NULL DEFAULT 0;

                -- 10. Orders: IsDigitalOrder, RequiresVatInvoice, CompanyName, TaxCode, CompanyAddress
                IF COL_LENGTH('dbo.Orders', 'IsDigitalOrder') IS NULL
                    ALTER TABLE [dbo].[Orders] ADD [IsDigitalOrder] BIT NOT NULL DEFAULT 0;
                IF COL_LENGTH('dbo.Orders', 'RequiresVatInvoice') IS NULL
                    ALTER TABLE [dbo].[Orders] ADD [RequiresVatInvoice] BIT NOT NULL DEFAULT 0;
                IF COL_LENGTH('dbo.Orders', 'CompanyName') IS NULL
                    ALTER TABLE [dbo].[Orders] ADD [CompanyName] NVARCHAR(255) NULL;
                IF COL_LENGTH('dbo.Orders', 'TaxCode') IS NULL
                    ALTER TABLE [dbo].[Orders] ADD [TaxCode] NVARCHAR(50) NULL;
                IF COL_LENGTH('dbo.Orders', 'CompanyAddress') IS NULL
                    ALTER TABLE [dbo].[Orders] ADD [CompanyAddress] NVARCHAR(500) NULL;

                -- 11. UserProfiles: ShopTier, IsMall, MallStatus & Legal Docs
                IF COL_LENGTH('dbo.UserProfiles', 'IsMall') IS NULL
                    ALTER TABLE [dbo].[UserProfiles] ADD [IsMall] BIT NOT NULL DEFAULT 0;
                IF COL_LENGTH('dbo.UserProfiles', 'ShopTier') IS NULL
                    ALTER TABLE [dbo].[UserProfiles] ADD [ShopTier] NVARCHAR(30) NOT NULL DEFAULT N'Standard';
                IF COL_LENGTH('dbo.UserProfiles', 'MallStatus') IS NULL
                    ALTER TABLE [dbo].[UserProfiles] ADD [MallStatus] NVARCHAR(30) NOT NULL DEFAULT N'None';
                IF COL_LENGTH('dbo.UserProfiles', 'BusinessName') IS NULL
                    ALTER TABLE [dbo].[UserProfiles] ADD [BusinessName] NVARCHAR(255) NULL;
                IF COL_LENGTH('dbo.UserProfiles', 'BusinessTaxCode') IS NULL
                    ALTER TABLE [dbo].[UserProfiles] ADD [BusinessTaxCode] NVARCHAR(50) NULL;
                IF COL_LENGTH('dbo.UserProfiles', 'BrandName') IS NULL
                    ALTER TABLE [dbo].[UserProfiles] ADD [BrandName] NVARCHAR(200) NULL;
                IF COL_LENGTH('dbo.UserProfiles', 'BusinessLicenseUrl') IS NULL
                    ALTER TABLE [dbo].[UserProfiles] ADD [BusinessLicenseUrl] NVARCHAR(500) NULL;
                IF COL_LENGTH('dbo.UserProfiles', 'BrandAuthorizationUrl') IS NULL
                    ALTER TABLE [dbo].[UserProfiles] ADD [BrandAuthorizationUrl] NVARCHAR(500) NULL;
                IF COL_LENGTH('dbo.UserProfiles', 'MallRejectionReason') IS NULL
                    ALTER TABLE [dbo].[UserProfiles] ADD [MallRejectionReason] NVARCHAR(500) NULL;
                IF COL_LENGTH('dbo.UserProfiles', 'MallApprovedAt') IS NULL
                    ALTER TABLE [dbo].[UserProfiles] ADD [MallApprovedAt] DATETIME2 NULL;

                -- 12. PartnerApplications: RequestedShopTier, ApplicationType & Legal Docs
                IF COL_LENGTH('dbo.PartnerApplications', 'RequestedShopTier') IS NULL
                    ALTER TABLE [dbo].[PartnerApplications] ADD [RequestedShopTier] NVARCHAR(30) NOT NULL DEFAULT N'Standard';
                IF COL_LENGTH('dbo.PartnerApplications', 'ApplicationType') IS NULL
                    ALTER TABLE [dbo].[PartnerApplications] ADD [ApplicationType] NVARCHAR(30) NOT NULL DEFAULT N'NewRegistration';
                IF COL_LENGTH('dbo.PartnerApplications', 'BusinessName') IS NULL
                    ALTER TABLE [dbo].[PartnerApplications] ADD [BusinessName] NVARCHAR(255) NULL;
                IF COL_LENGTH('dbo.PartnerApplications', 'BusinessTaxCode') IS NULL
                    ALTER TABLE [dbo].[PartnerApplications] ADD [BusinessTaxCode] NVARCHAR(50) NULL;
                IF COL_LENGTH('dbo.PartnerApplications', 'BrandName') IS NULL
                    ALTER TABLE [dbo].[PartnerApplications] ADD [BrandName] NVARCHAR(200) NULL;
                IF COL_LENGTH('dbo.PartnerApplications', 'BusinessLicenseUrl') IS NULL
                    ALTER TABLE [dbo].[PartnerApplications] ADD [BusinessLicenseUrl] NVARCHAR(500) NULL;
                IF COL_LENGTH('dbo.PartnerApplications', 'BrandAuthorizationUrl') IS NULL
                    ALTER TABLE [dbo].[PartnerApplications] ADD [BrandAuthorizationUrl] NVARCHAR(500) NULL;
            ", cancellationToken);


            // DIAGNOSTICS: Check column types again after repair
            var diagnosticsAfter = await context.Database.SqlQueryRaw<string>(@"
                SELECT TABLE_NAME + '.' + COLUMN_NAME + ': ' + DATA_TYPE 
                FROM INFORMATION_SCHEMA.COLUMNS 
                WHERE TABLE_NAME IN ('BlogPosts', 'BlogComments', 'Accounts', 'UserProfiles') 
                AND COLUMN_NAME IN ('CreatedAt', 'UpdatedAt', 'PublishedAt')
            ").ToListAsync(cancellationToken);

            foreach (var diag in diagnosticsAfter)
            {
                logger.LogInformation("DATABASE SCHEMA DIAGNOSTIC (AFTER REPAIR): {Diag}", diag);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to ensure Blog tables exist via raw SQL.");
        }

        // Ensure Category column exists on Sizes table
        try
        {
            await context.Database.ExecuteSqlRawAsync("ALTER TABLE Sizes ADD Category NVARCHAR(50) NOT NULL DEFAULT N'Khác';", cancellationToken);
            logger.LogInformation("Successfully added Category column to Sizes table.");
        }
        catch (Exception ex)
        {
            // If the column already exists (Error 2705: Column names in each table must be unique), we ignore the exception
            if (ex.Message.Contains("unique") || ex.Message.Contains("already") || ex.Message.Contains("2705") || ex.Message.Contains("duplicate"))
            {
                logger.LogInformation("Category column already exists on Sizes table.");
            }
            else
            {
                logger.LogWarning(ex, "Failed to alter Sizes table to add Category column.");
            }
        }

        // ---- ONE-TIME MULTI-INDUSTRY RESET & SEED (Keeps Accounts/Login data intact) ----
        await EnsureMultiIndustryCatalogResetAndSeedAsync(context, logger, cancellationToken);
        await EnsureDetailedCategoryHierarchyAsync(context, logger, cancellationToken);


        // ---- CLEANUP MODE (OPT-IN) ----
        // If the user explicitly wants a clean database, wipe data tables and exit early.
        // IMPORTANT: This must run BEFORE any seeding (colors/categories/warehouses/etc.).
        var cleanupMode = IsTruthy(Environment.GetEnvironmentVariable("CLEAN_DB_ON_STARTUP"))
            || IsTruthy(Environment.GetEnvironmentVariable("PRODUCTION_CLEANUP_MODE"));

        if (cleanupMode)
        {
            logger.LogWarning(
                "CLEANUP MODE enabled (CLEAN_DB_ON_STARTUP/PRODUCTION_CLEANUP_MODE). Wiping data tables and exiting seeding early.");

            var tables = new[]
            {
                "SystemNotificationReads", "SystemNotifications", "Messages", "Conversations",
                "BlogPostProducts", "BlogComments", "BlogPosts", "PartnerApplications",
                "InventoryTransactions", "OrderTrackingLogs", "OrderItems", "Orders",
                "Vouchers", "CartItems", "Carts", "WishlistItems", "SizeGuideDetails",
                "SizeGuides", "VariantImages", "ProductImages", "ProductVariants",
                "ProductReviews", "Products", "UserAddresses", "UserProfiles"
            };

            await context.Database.ExecuteSqlRawAsync("EXEC sp_MSforeachtable 'ALTER TABLE ? NOCHECK CONSTRAINT ALL'", cancellationToken);

            foreach (var table in tables)
            {
                try
                {
                    await context.Database.ExecuteSqlRawAsync($"DELETE FROM [{table}]", cancellationToken);
                    logger.LogInformation("Deleted all records from {Table}", table);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Could not clear table {Table}", table);
                }
            }

            try
            {
                await context.Database.ExecuteSqlRawAsync("DELETE FROM [Accounts] WHERE [Role] <> 'Admin'", cancellationToken);
                logger.LogInformation("Deleted all non-admin accounts.");
            }
            catch
            {
            }

            await context.Database.ExecuteSqlRawAsync("EXEC sp_MSforeachtable 'ALTER TABLE ? WITH CHECK CHECK CONSTRAINT ALL'", cancellationToken);
            return;
        }

        // ---- SEED CONTROL SWITCHES ----
        // Disable all seeding on startup (migrate-only). Useful after you manually cleaned the DB.
        var skipDbSeed = IsTruthy(Environment.GetEnvironmentVariable("SKIP_DB_SEED"))
            || IsTruthy(Environment.GetEnvironmentVariable("DISABLE_DB_SEED"))
            || IsTruthy(Environment.GetEnvironmentVariable("MIGRATE_ONLY"));

        if (skipDbSeed)
        {
            logger.LogInformation(
                "DB seeding skipped (SKIP_DB_SEED/DISABLE_DB_SEED/MIGRATE_ONLY). Migrations are applied, but no seed data will be inserted.");
            return;
        }

        var resetSeed = IsTruthy(Environment.GetEnvironmentVariable("RESET_SEED"));
        if (resetSeed)
        {
            await ResetCatalogAsync(context, logger, cancellationToken);
        }

        var markerPath = Path.Combine(Directory.GetCurrentDirectory(), ".db-seeded");
        if (File.Exists(markerPath) && !resetSeed)
        {
            logger.LogInformation("Database was already seeded once. Skipping automatic sample data seed to prevent overwriting user deletions. Delete the '.db-seeded' file in the root folder or set RESET_SEED=true to re-seed.");
            return;
        }

        // ---- GHN Warehouse Network (dense: provinces + districts) ----
        // This is a one-time seed in Development. It uses GHN master-data endpoints (province/district)
        // and stores a local network in DB so the simulator can build realistic multi-hop routes.
        var skipWarehouseSeed = IsTruthy(Environment.GetEnvironmentVariable("SKIP_WAREHOUSE_NETWORK_SEED"));
        if (!skipWarehouseSeed)
        {
            var resetWarehouseSeed = IsTruthy(Environment.GetEnvironmentVariable("RESET_WAREHOUSE_NETWORK"));
            var provincesOnly = IsTruthy(Environment.GetEnvironmentVariable("SEED_WAREHOUSE_PROVINCES_ONLY"));
            var includeDistricts = !provincesOnly;

            try
            {
                var ghn = scope.ServiceProvider.GetRequiredService<GhnClient>();
                var warehouseSeeder = new WarehouseNetworkSeeder();
                await warehouseSeeder.SeedFromGhnAsync(
                    context,
                    ghn,
                    logger,
                    includeDistricts: includeDistricts,
                    resetExisting: resetWarehouseSeed,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Warehouse network seed failed (best-effort). You can retry by setting RESET_WAREHOUSE_NETWORK=true.");
            }
        }

        var nowUtc = DateTime.UtcNow;

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

        // Cleanup mode is handled earlier (before any seeding).

        // ---- Admin Account (only seed fallback admin@local if no Admin account exists) ----
        var adminEmail = "admin@local";
        if (!await context.Accounts.AnyAsync(a => a.Role == "Admin" && !a.IsDeleted, cancellationToken))
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

        logger.LogInformation("System configuration data seeded (Categories, Admin Account).");
    }

    private static async Task ApplyMigrationsSafelyAsync(
        DataContext context,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            // Kiểm tra trước xem bảng Accounts đã tồn tại trong DB chưa.
            // Nếu bảng đã có (ví dụ DB được restore hoặc tạo sẵn) nhưng __EFMigrationsHistory trống/thiếu,
            // ta đồng bộ danh sách migration vào __EFMigrationsHistory trước để EF Core không chạy lại lệnh CREATE TABLE [Accounts] gây lỗi đỏ 2714.
            var conn = context.Database.GetDbConnection();
            var wasClosed = conn.State != System.Data.ConnectionState.Open;
            if (wasClosed)
            {
                await conn.OpenAsync(cancellationToken);
            }

            bool accountsTableExists = false;
            try
            {
                await using var checkCmd = conn.CreateCommand();
                checkCmd.CommandText = "SELECT CASE WHEN OBJECT_ID(N'dbo.Accounts', N'U') IS NOT NULL THEN 1 ELSE 0 END;";
                var scalar = await checkCmd.ExecuteScalarAsync(cancellationToken);
                accountsTableExists = Convert.ToInt32(scalar) == 1;

                if (accountsTableExists)
                {
                    var allMigrations = context.Database.GetMigrations().ToList();
                    if (allMigrations.Count > 0)
                    {
                        await using var ensureHistoryCmd = conn.CreateCommand();
                        ensureHistoryCmd.CommandTimeout = 60;
                        ensureHistoryCmd.CommandText = @"
IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END";
                        await ensureHistoryCmd.ExecuteNonQueryAsync(cancellationToken);

                        // Đọc 1 lần các MigrationId đã có (NOLOCK) để tránh khóa ghi không cần thiết mỗi lần khởi động
                        var existingMigs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        await using (var readCmd = conn.CreateCommand())
                        {
                            readCmd.CommandTimeout = 30;
                            readCmd.CommandText = "SELECT [MigrationId] FROM [__EFMigrationsHistory] WITH (NOLOCK);";
                            await using var reader = await readCmd.ExecuteReaderAsync(cancellationToken);
                            while (await reader.ReadAsync(cancellationToken))
                            {
                                existingMigs.Add(reader.GetString(0));
                            }
                        }

                        var missingMigs = allMigrations.Where(m => !existingMigs.Contains(m)).ToList();
                        foreach (var migId in missingMigs)
                        {
                            await using var insCmd = conn.CreateCommand();
                            insCmd.CommandTimeout = 60;
                            insCmd.CommandText = @"
IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = @mId)
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion]) VALUES (@mId, N'8.0.0');";
                            var p = insCmd.CreateParameter();
                            p.ParameterName = "@mId";
                            p.Value = migId;
                            insCmd.Parameters.Add(p);
                            await insCmd.ExecuteNonQueryAsync(cancellationToken);
                        }
                    }
                    // Các cột/bảng nâng cấp sẽ được EnsureMultiIndustrySchemaAsync kiểm tra và bổ sung an toàn ngay sau bước này
                    return;
                }
            }
            finally
            {
                if (wasClosed && conn.State == System.Data.ConnectionState.Open)
                {
                    await conn.CloseAsync();
                }
            }

            await context.Database.MigrateAsync(cancellationToken);
        }
        catch (SqlException ex) when (ex.Number == 1801)
        {
            logger.LogWarning("Database already exists while applying migrations; falling back to idempotent migration script.");
            try
            {
                await ApplyMigrationsByIdempotentScriptAsync(context, logger, cancellationToken);
            }
            catch (SqlException innerEx) when (innerEx.Number == 2714)
            {
                logger.LogInformation("Tables already exist in database; continuing with EnsureMultiIndustrySchemaAsync.");
            }
        }
        catch (SqlException ex) when (ex.Number == 2714)
        {
            logger.LogInformation("Schema objects already exist in database (2714); continuing with EnsureMultiIndustrySchemaAsync.");
        }
        catch (SqlException ex) when (ex.Number == -2)
        {
            logger.LogWarning("SQL command timed out while syncing __EFMigrationsHistory; continuing startup.");
        }
    }

    private static async Task ApplyMigrationsByIdempotentScriptAsync(
        DataContext context,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var migrator = context.Database.GetService<IMigrator>();
        var script = migrator.GenerateScript(options: MigrationsSqlGenerationOptions.Idempotent);

        var batches = SplitSqlServerBatches(script);
        var applied = 0;

        foreach (var batch in batches)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await context.Database.ExecuteSqlRawAsync(batch, cancellationToken);
            applied++;
        }

        logger.LogInformation("Applied {BatchCount} migration script batches (idempotent).", applied);
    }

    private static IEnumerable<string> SplitSqlServerBatches(string script)
    {
        if (string.IsNullOrWhiteSpace(script)) yield break;

        var goRegex = new Regex(@"^\s*GO(?:\s+(\d+))?\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        var sb = new StringBuilder();

        using var reader = new StringReader(script);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            var match = goRegex.Match(line);
            if (match.Success)
            {
                var batch = sb.ToString().Trim();
                sb.Clear();

                if (batch.Length == 0) continue;

                var repeat = 1;
                if (match.Groups.Count > 1 && match.Groups[1].Success && int.TryParse(match.Groups[1].Value, out var n) && n > 1)
                {
                    repeat = n;
                }

                for (var i = 0; i < repeat; i++)
                {
                    yield return batch;
                }

                continue;
            }

            sb.AppendLine(line);
        }

        var last = sb.ToString().Trim();
        if (last.Length > 0)
        {
            yield return last;
        }
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

        await context.Vouchers.ExecuteDeleteAsync(cancellationToken);
    }

    private static async Task EnsureMultiIndustryCatalogResetAndSeedAsync(
        DataContext context,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var markerPath = Path.Combine(Directory.GetCurrentDirectory(), ".multi-industry-migrated");
        if (File.Exists(markerPath))
        {
            return;
        }

        logger.LogInformation("Running one-time Multi-Industry E-Commerce database cleanup (preserving Accounts/Login data) and seeding multi-industry catalog...");

        try
        {
            // 1. Wipe old product, order, cart, review, category data while keeping Accounts, UserProfiles, UserAddresses, PartnerApplications, Wallets
            await context.Database.ExecuteSqlRawAsync(@"
                DELETE FROM [dbo].[ReturnRequests];
                DELETE FROM [dbo].[InventoryTransactions];
                DELETE FROM [dbo].[OrderTrackingLogs];
                DELETE FROM [dbo].[OrderItems];
                DELETE FROM [dbo].[Orders];
                DELETE FROM [dbo].[CartItems];
                DELETE FROM [dbo].[Carts];
                DELETE FROM [dbo].[WishlistItems];
                DELETE FROM [dbo].[BlogPostProducts];
                DELETE FROM [dbo].[SizeGuideDetails];
                DELETE FROM [dbo].[SizeGuides];
                DELETE FROM [dbo].[VariantImages];
                DELETE FROM [dbo].[ProductImages];
                DELETE FROM [dbo].[ProductVariants];
                DELETE FROM [dbo].[ProductOptionValues];
                DELETE FROM [dbo].[ProductOptions];
                DELETE FROM [dbo].[ProductAttributeValues];
                DELETE FROM [dbo].[ProductReviews];
                DELETE FROM [dbo].[Products];
                DELETE FROM [dbo].[CategoryAttributes];
                UPDATE [dbo].[Categories] SET [ParentId] = NULL;
                DELETE FROM [dbo].[Categories];
            ", cancellationToken);

            // 2. Migrate SystemSettings: Replace Fee_Commission_Fashion & Fee_Commission_Cosmetics with Fee_Commission_Default
            var oldCommSettings = await context.SystemSettings
                .Where(s => s.Id == "Fee_Commission_Fashion" || s.Id == "Fee_Commission_Cosmetics" || s.Id == "Fee_Payment")
                .ToListAsync(cancellationToken);
            if (oldCommSettings.Count > 0)
            {
                context.SystemSettings.RemoveRange(oldCommSettings);
                await context.SaveChangesAsync(cancellationToken);
            }

            if (!await context.SystemSettings.AnyAsync(s => s.Id == "Fee_Commission_Default", cancellationToken))
            {
                context.SystemSettings.Add(new SystemSetting
                {
                    Id = "Fee_Commission_Default",
                    Value = "10",
                    Description = "Chiết khấu cố định mặc định (áp dụng khi danh mục chưa thiết lập riêng)",
                    Type = "percent",
                    UpdatedAt = DateTime.UtcNow
                });
            }
            if (!await context.SystemSettings.AnyAsync(s => s.Id == "Tax_VAT_Default", cancellationToken))
            {
                context.SystemSettings.Add(new SystemSetting { Id = "Tax_VAT_Default", Value = "8", Description = "Mức thuế VAT mặc định", Type = "percent", UpdatedAt = DateTime.UtcNow });
            }
            if (!await context.SystemSettings.AnyAsync(s => s.Id == "Tax_Seller_Threshold", cancellationToken))
            {
                context.SystemSettings.Add(new SystemSetting { Id = "Tax_Seller_Threshold", Value = "500000000", Description = "Ngưỡng doanh thu chịu thuế", Type = "money", UpdatedAt = DateTime.UtcNow });
            }
            if (!await context.SystemSettings.AnyAsync(s => s.Id == "Tax_Seller_Rate", cancellationToken))
            {
                context.SystemSettings.Add(new SystemSetting { Id = "Tax_Seller_Rate", Value = "1.5", Description = "Mức thuế áp dụng cho hộ KD > 500 triệu", Type = "percent", UpdatedAt = DateTime.UtcNow });
            }
            if (!await context.SystemSettings.AnyAsync(s => s.Id == "Fee_Marketing", cancellationToken))
            {
                context.SystemSettings.Add(new SystemSetting { Id = "Fee_Marketing", Value = "6", Description = "Phí Marketing chung (Freeship / Voucher Xtra)", Type = "percent", UpdatedAt = DateTime.UtcNow });
            }
            await context.SaveChangesAsync(cancellationToken);

            // 3. Seed Multi-Industry Brands
            var nowUtc = DateTime.UtcNow;
            var brandSeeds = new (string Name, string Slug)[]
            {
                ("Apple", "apple"),
                ("Samsung", "samsung"),
                ("Xiaomi", "xiaomi"),
                ("Sony", "sony"),
                ("Asus", "asus"),
                ("Dell", "dell"),
                ("Logitech", "logitech"),
                ("Philips", "philips"),
                ("Panasonic", "panasonic"),
                ("Sunhouse", "sunhouse"),
                ("Lock&Lock", "lock-and-lock"),
                ("Nike", "nike"),
                ("Adidas", "adidas"),
                ("Uniqlo", "uniqlo"),
                ("L'Oréal", "loreal"),
                ("La Roche-Posay", "la-roche-posay"),
                ("Unilever", "unilever"),
                ("Vinamilk", "vinamilk"),
                ("Nestlé", "nestle"),
                ("Thiên Long", "thien-long"),
                ("NXB Trẻ", "nxb-tre")
            };

            foreach (var (bName, bSlug) in brandSeeds)
            {
                if (!await context.Brands.AnyAsync(b => b.Slug == bSlug, cancellationToken))
                {
                    context.Brands.Add(new Brand
                    {
                        Name = bName,
                        Slug = bSlug,
                        IsActive = true,
                        CreatedAt = nowUtc
                    });
                }
            }
            await context.SaveChangesAsync(cancellationToken);

            // 4. Seed Multi-Industry Categories (Root & Children)
            var rootCatSeeds = new (string Slug, string Name, decimal CommRate, decimal? MaxComm, string IconUrl, bool HasSizeGuide, bool IsReturnable, int Sort)[]
            {
                ("dien-thoai-phu-kien", "Điện thoại & Phụ kiện", 3.00m, 500000m, "bx-mobile-alt", false, true, 1),
                ("may-tinh-laptop", "Máy tính & Laptop", 2.50m, 500000m, "bx-laptop", false, true, 2),
                ("thiet-bi-dien-gia-dung", "Thiết bị Điện gia dụng", 5.00m, 300000m, "bx-fridge", false, true, 3),
                ("thoi-trang-nam", "Thời trang Nam", 12.00m, null, "bx-closet", true, true, 4),
                ("thoi-trang-nu", "Thời trang Nữ", 12.00m, null, "bx-shopping-bag", true, true, 5),
                ("giay-dep-tui-xach", "Giày dép & Túi xách", 11.00m, null, "bx-walk", true, true, 6),
                ("my-pham-sac-dep", "Mỹ phẩm & Sắc đẹp", 10.00m, null, "bx-spa", false, true, 7),
                ("thuc-pham-do-uong", "Thực phẩm & Đồ uống", 8.00m, null, "bx-bowl-hot", false, false, 8),
                ("nha-sach-van-phong-pham", "Nhà sách & Văn phòng phẩm", 7.00m, null, "bx-book-open", false, true, 9),
                ("san-pham-so-evoucher", "Sản phẩm số & E-Voucher", 5.00m, null, "bx-barcode-reader", false, false, 10)
            };

            var catMap = new Dictionary<string, Category>(StringComparer.OrdinalIgnoreCase);
            foreach (var rc in rootCatSeeds)
            {
                var cat = new Category
                {
                    Name = rc.Name,
                    Slug = rc.Slug,
                    ParentId = null,
                    CommissionRate = rc.CommRate,
                    MaxCommissionAmount = rc.MaxComm,
                    IconUrl = rc.IconUrl,
                    HasSizeGuide = rc.HasSizeGuide,
                    IsReturnableDefault = rc.IsReturnable,
                    SortOrder = rc.Sort,
                    IsActive = true,
                    CreatedAt = nowUtc,
                    UpdatedAt = nowUtc
                };
                context.Categories.Add(cat);
                catMap[rc.Slug] = cat;
            }
            await context.SaveChangesAsync(cancellationToken);

            var childCatSeeds = new (string Slug, string Name, string ParentSlug, decimal? CommRate, bool HasSizeGuide, bool IsReturnable, int Sort)[]
            {
                ("dien-thoai-thong-minh", "Điện thoại thông minh", "dien-thoai-phu-kien", null, false, true, 1),
                ("may-tinh-bang", "Máy tính bảng", "dien-thoai-phu-kien", null, false, true, 2),
                ("tai-nghe-loa-bluetooth", "Tai nghe & Loa Bluetooth", "dien-thoai-phu-kien", 6.00m, false, true, 3),
                ("cap-sac-pin-du-phong", "Cáp sạc & Pin dự phòng", "dien-thoai-phu-kien", 8.00m, false, true, 4),

                ("laptop-van-phong", "Laptop Văn phòng & Học tập", "may-tinh-laptop", null, false, true, 1),
                ("laptop-gaming", "Laptop Gaming & Đồ họa", "may-tinh-laptop", null, false, true, 2),
                ("linh-kien-man-hinh", "Màn hình & Linh kiện PC", "may-tinh-laptop", 4.00m, false, true, 3),

                ("gia-dung-nha-bep", "Gia dụng Nhà bếp (Nồi chiên, Máy xay...)", "thiet-bi-dien-gia-dung", null, false, true, 1),
                ("thiet-bi-lam-mat-loc-khi", "Quạt điều hòa & Lọc không khí", "thiet-bi-dien-gia-dung", null, false, true, 2),

                ("ao-nam", "Áo Nam (Thun, Polo, Sơ mi)", "thoi-trang-nam", null, true, true, 1),
                ("quan-nam", "Quần Nam (Jean, Kaki, Short)", "thoi-trang-nam", null, true, true, 2),

                ("ao-nu", "Áo Nữ & Áo khoác", "thoi-trang-nu", null, true, true, 1),
                ("vay-dam-nu", "Váy & Đầm Nữ", "thoi-trang-nu", null, true, true, 2),

                ("giay-the-thao", "Giày Thể thao / Sneaker", "giay-dep-tui-xach", null, true, true, 1),
                ("tui-xach-balo", "Túi xách & Balo", "giay-dep-tui-xach", null, false, true, 2),

                ("cham-soc-da-mat", "Chăm sóc da mặt (Skincare)", "my-pham-sac-dep", null, false, true, 1),
                ("trang-diem-nuoc-hoa", "Trang điểm & Nước hoa", "my-pham-sac-dep", null, false, true, 2),

                ("do-an-vat-banh-keo", "Đồ ăn vặt & Bánh kẹo", "thuc-pham-do-uong", null, false, false, 1),
                ("thuc-pham-tuoi-song", "Thực phẩm tươi sống & Đông lạnh", "thuc-pham-do-uong", 5.00m, false, false, 2),
                ("ca-phe-tra-sua", "Cà phê, Trà & Sữa", "thuc-pham-do-uong", null, false, false, 3),

                ("sach-van-hoc-kinh-te", "Sách Văn học & Kinh tế", "nha-sach-van-phong-pham", null, false, true, 1),
                ("dung-cu-hoc-tap", "Dụng cụ học tập & Văn phòng", "nha-sach-van-phong-pham", null, false, true, 2)
            };

            foreach (var cc in childCatSeeds)
            {
                if (!catMap.TryGetValue(cc.ParentSlug, out var parent)) continue;
                var child = new Category
                {
                    Name = cc.Name,
                    Slug = cc.Slug,
                    ParentId = parent.Id,
                    CommissionRate = cc.CommRate, // null = inherit from parent
                    MaxCommissionAmount = null,
                    HasSizeGuide = cc.HasSizeGuide,
                    IsReturnableDefault = cc.IsReturnable,
                    SortOrder = cc.Sort,
                    IsActive = true,
                    CreatedAt = nowUtc,
                    UpdatedAt = nowUtc
                };
                context.Categories.Add(child);
                catMap[cc.Slug] = child;
            }
            await context.SaveChangesAsync(cancellationToken);

            // 5. Seed CategoryAttributes (EAV technical specs per industry)
            var attrSeeds = new (string CatSlug, string Name, string? Unit, bool IsFilterable, int Sort)[]
            {
                // Điện thoại & Phụ kiện
                ("dien-thoai-phu-kien", "RAM", "GB", true, 1),
                ("dien-thoai-phu-kien", "Bộ nhớ trong (ROM)", "GB", true, 2),
                ("dien-thoai-phu-kien", "Kích thước màn hình", "inch", true, 3),
                ("dien-thoai-phu-kien", "Dung lượng pin", "mAh", false, 4),
                ("dien-thoai-phu-kien", "Chip xử lý (CPU)", null, true, 5),

                // Máy tính & Laptop
                ("may-tinh-laptop", "Vi xử lý (CPU)", null, true, 1),
                ("may-tinh-laptop", "RAM", "GB", true, 2),
                ("may-tinh-laptop", "Ổ cứng SSD", "GB", true, 3),
                ("may-tinh-laptop", "Card đồ họa (GPU)", null, true, 4),
                ("may-tinh-laptop", "Kích thước màn hình", "inch", true, 5),

                // Thiết bị Điện gia dụng
                ("thiet-bi-dien-gia-dung", "Công suất", "W", true, 1),
                ("thiet-bi-dien-gia-dung", "Dung tích", "Lít", true, 2),
                ("thiet-bi-dien-gia-dung", "Điện áp", "V", false, 3),
                ("thiet-bi-dien-gia-dung", "Xuất xứ", null, true, 4),

                // Thời trang Nam / Nữ / Giày dép
                ("thoi-trang-nam", "Chất liệu", null, true, 1),
                ("thoi-trang-nam", "Kiểu dáng (Form)", null, true, 2),
                ("thoi-trang-nam", "Xuất xứ", null, true, 3),

                ("thoi-trang-nu", "Chất liệu", null, true, 1),
                ("thoi-trang-nu", "Phong cách", null, true, 2),
                ("thoi-trang-nu", "Xuất xứ", null, true, 3),

                ("giay-dep-tui-xach", "Chất liệu bề mặt", null, true, 1),
                ("giay-dep-tui-xach", "Chất liệu đế", null, false, 2),
                ("giay-dep-tui-xach", "Xuất xứ", null, true, 3),

                // Mỹ phẩm & Sắc đẹp
                ("my-pham-sac-dep", "Loại da phù hợp", null, true, 1),
                ("my-pham-sac-dep", "Dung tích / Trọng lượng", "ml", true, 2),
                ("my-pham-sac-dep", "Hạn sử dụng", "tháng", false, 3),
                ("my-pham-sac-dep", "Xuất xứ", null, true, 4),

                // Thực phẩm & Đồ uống
                ("thuc-pham-do-uong", "Xuất xứ", null, true, 1),
                ("thuc-pham-do-uong", "Hạn sử dụng", "tháng", true, 2),
                ("thuc-pham-do-uong", "Hướng dẫn bảo quản", null, false, 3),
                ("thuc-pham-do-uong", "Trọng lượng tịnh", "g", true, 4),

                // Nhà sách & VPP
                ("nha-sach-van-phong-pham", "Tác giả", null, true, 1),
                ("nha-sach-van-phong-pham", "Nhà xuất bản", null, true, 2),
                ("nha-sach-van-phong-pham", "Loại bìa", null, true, 3),
                ("nha-sach-van-phong-pham", "Số trang", "trang", false, 4)
            };

            foreach (var a in attrSeeds)
            {
                if (!catMap.TryGetValue(a.CatSlug, out var cat)) continue;
                context.CategoryAttributes.Add(new CategoryAttribute
                {
                    CategoryId = cat.Id,
                    Name = a.Name,
                    Unit = a.Unit,
                    IsFilterable = a.IsFilterable,
                    SortOrder = a.Sort
                });
            }
            await context.SaveChangesAsync(cancellationToken);

            // Write marker files so neither old fashion seeder nor this reset runs again
            await File.WriteAllTextAsync(markerPath, nowUtc.ToString("O"), cancellationToken);
            var legacySeededMarker = Path.Combine(Directory.GetCurrentDirectory(), ".db-seeded");
            await File.WriteAllTextAsync(legacySeededMarker, nowUtc.ToString("O"), cancellationToken);

            logger.LogInformation("Multi-Industry E-Commerce database upgrade and catalog seeding completed successfully!");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during Multi-Industry catalog reset and seed.");
        }
    }

    private static async Task EnsureDetailedCategoryHierarchyAsync(
        DataContext context,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            var nowUtc = DateTime.UtcNow;
            var allCategories = await context.Categories.ToListAsync(cancellationToken);
            var catBySlug = allCategories
                .Where(c => !string.IsNullOrWhiteSpace(c.Slug))
                .ToDictionary(c => c.Slug, c => c, StringComparer.OrdinalIgnoreCase);

            var rootCatSeeds = new (string Slug, string Name, decimal CommRate, decimal? MaxComm, string IconUrl, bool HasSizeGuide, bool IsReturnable, int Sort)[]
            {
                ("dien-thoai-phu-kien", "Điện thoại & Phụ kiện", 3.00m, 500000m, "bx-mobile-alt", false, true, 1),
                ("may-tinh-laptop", "Máy tính & Laptop", 2.50m, 500000m, "bx-laptop", false, true, 2),
                ("thiet-bi-dien-gia-dung", "Thiết bị Điện gia dụng", 5.00m, 300000m, "bx-fridge", false, true, 3),
                ("thoi-trang-nam", "Thời trang Nam", 12.00m, null, "bx-closet", true, true, 4),
                ("thoi-trang-nu", "Thời trang Nữ", 12.00m, null, "bx-shopping-bag", true, true, 5),
                ("giay-dep-tui-xach", "Giày dép & Túi xách", 11.00m, null, "bx-walk", true, true, 6),
                ("my-pham-sac-dep", "Mỹ phẩm & Sắc đẹp", 10.00m, null, "bx-spa", false, true, 7),
                ("thuc-pham-do-uong", "Thực phẩm & Đồ uống", 8.00m, null, "bx-bowl-hot", false, false, 8),
                ("nha-sach-van-phong-pham", "Nhà sách & Văn phòng phẩm", 7.00m, null, "bx-book-open", false, true, 9),
                ("san-pham-so-evoucher", "Sản phẩm số & E-Voucher", 5.00m, null, "bx-barcode-reader", false, false, 10)
            };

            foreach (var rc in rootCatSeeds)
            {
                if (!catBySlug.TryGetValue(rc.Slug, out var rootCat))
                {
                    rootCat = new Category
                    {
                        Name = rc.Name,
                        Slug = rc.Slug,
                        ParentId = null,
                        CommissionRate = rc.CommRate,
                        MaxCommissionAmount = rc.MaxComm,
                        IconUrl = rc.IconUrl,
                        HasSizeGuide = rc.HasSizeGuide,
                        IsReturnableDefault = rc.IsReturnable,
                        SortOrder = rc.Sort,
                        IsActive = true,
                        CreatedAt = nowUtc,
                        UpdatedAt = nowUtc
                    };
                    context.Categories.Add(rootCat);
                    catBySlug[rc.Slug] = rootCat;
                }
                else
                {
                    rootCat.IconUrl = rc.IconUrl;
                    rootCat.SortOrder = rc.Sort;
                    rootCat.IsActive = true;
                }
            }
            await context.SaveChangesAsync(cancellationToken);

            var childCatSeeds = new (string Slug, string Name, string ParentSlug, decimal? CommRate, bool HasSizeGuide, bool IsReturnable, int Sort)[]
            {
                // 1. Điện thoại & Phụ kiện
                ("dien-thoai-thong-minh", "Điện thoại thông minh", "dien-thoai-phu-kien", null, false, true, 1),
                ("may-tinh-bang", "Máy tính bảng (Tablet)", "dien-thoai-phu-kien", null, false, true, 2),
                ("dong-ho-thong-minh", "Đồng hồ thông minh (Smartwatch)", "dien-thoai-phu-kien", 4.00m, false, true, 3),
                ("tai-nghe-loa-bluetooth", "Tai nghe & Loa Bluetooth", "dien-thoai-phu-kien", 6.00m, false, true, 4),
                ("cap-sac-pin-du-phong", "Cáp sạc & Pin dự phòng", "dien-thoai-phu-kien", 8.00m, false, true, 5),
                ("op-lung-kinh-cuong-luc", "Ốp lưng & Kính cường lực", "dien-thoai-phu-kien", 10.00m, false, true, 6),

                // 2. Máy tính & Laptop
                ("laptop-van-phong", "Laptop Văn phòng & Học tập", "may-tinh-laptop", null, false, true, 1),
                ("laptop-gaming", "Laptop Gaming & Đồ họa", "may-tinh-laptop", null, false, true, 2),
                ("pc-may-tinh-de-ban", "PC & Máy tính để bàn", "may-tinh-laptop", null, false, true, 3),
                ("linh-kien-man-hinh", "Màn hình & Linh kiện PC", "may-tinh-laptop", 4.00m, false, true, 4),
                ("ban-phim-chuot-gaming", "Bàn phím, Chuột & Gear", "may-tinh-laptop", 6.00m, false, true, 5),
                ("thiet-bi-mang-luu-tru", "Thiết bị mạng & Ổ cứng lưu trữ", "may-tinh-laptop", 5.00m, false, true, 6),

                // 3. Thiết bị Điện gia dụng
                ("gia-dung-nha-bep", "Gia dụng Nhà bếp (Nồi chiên, Máy xay)", "thiet-bi-dien-gia-dung", null, false, true, 1),
                ("thiet-bi-lam-mat-loc-khi", "Quạt điều hòa & Lọc không khí", "thiet-bi-dien-gia-dung", null, false, true, 2),
                ("robot-hut-bui-ve-sinh", "Robot hút bụi & Máy vệ sinh", "thiet-bi-dien-gia-dung", null, false, true, 3),
                ("tivi-am-thanh-gia-dinh", "Tivi & Thiết bị âm thanh", "thiet-bi-dien-gia-dung", 4.00m, false, true, 4),
                ("cham-soc-ca-nhan-dien", "Bàn ủi, Máy sấy & Chăm sóc cá nhân", "thiet-bi-dien-gia-dung", 6.00m, false, true, 5),

                // 4. Thời trang Nam
                ("ao-nam", "Áo Thun & Áo Polo Nam", "thoi-trang-nam", null, true, true, 1),
                ("ao-so-mi-nam", "Áo Sơ mi & Vest Nam", "thoi-trang-nam", null, true, true, 2),
                ("ao-khoac-hoodie-nam", "Áo Khoác & Hoodie Nam", "thoi-trang-nam", null, true, true, 3),
                ("quan-nam", "Quần Jean, Kaki & Quần Tây", "thoi-trang-nam", null, true, true, 4),
                ("do-the-thao-do-mac-nha-nam", "Đồ Thể thao & Mặc nhà Nam", "thoi-trang-nam", null, true, true, 5),
                ("phu-kien-nam", "Thắt lưng, Ví da & Phụ kiện Nam", "thoi-trang-nam", null, false, true, 6),

                // 5. Thời trang Nữ
                ("ao-nu", "Áo Thun, Sơ mi & Kiểu Nữ", "thoi-trang-nu", null, true, true, 1),
                ("vay-dam-nu", "Váy Đầm Dự tiệc & Công sở", "thoi-trang-nu", null, true, true, 2),
                ("chan-vay-quan-nu", "Chân váy, Quần Jean & Culottes", "thoi-trang-nu", null, true, true, 3),
                ("ao-khoac-cardigan-nu", "Áo Khoác, Blazer & Cardigan", "thoi-trang-nu", null, true, true, 4),
                ("do-ngu-mac-nha-nu", "Đồ ngủ, Đồ lót & Mặc nhà Nữ", "thoi-trang-nu", null, true, false, 5),

                // 6. Giày dép & Túi xách
                ("giay-the-thao", "Giày Thể thao & Sneaker", "giay-dep-tui-xach", null, true, true, 1),
                ("giay-tay-giay-luoi-nam", "Giày Tây & Giày Lười Nam", "giay-dep-tui-xach", null, true, true, 2),
                ("giay-cao-got-sandal-nu", "Giày Cao gót, Búp bê & Sandal Nữ", "giay-dep-tui-xach", null, true, true, 3),
                ("tui-xach-vi-nu", "Túi xách & Ví thời trang Nữ", "giay-dep-tui-xach", null, false, true, 4),
                ("tui-xach-balo", "Balo Laptop & Túi đeo chéo", "giay-dep-tui-xach", null, false, true, 5),
                ("vali-tui-du-lich", "Vali & Túi du lịch", "giay-dep-tui-xach", null, false, true, 6),

                // 7. Mỹ phẩm & Sắc đẹp
                ("cham-soc-da-mat", "Chăm sóc da mặt (Skincare)", "my-pham-sac-dep", null, false, true, 1),
                ("trang-diem-nuoc-hoa", "Trang điểm (Makeup)", "my-pham-sac-dep", null, false, true, 2),
                ("nuoc-hoa-chinh-hang", "Nước hoa Nam & Nữ", "my-pham-sac-dep", null, false, true, 3),
                ("cham-soc-toc-co-the", "Chăm sóc tóc & Cơ thể (Bodycare)", "my-pham-sac-dep", null, false, true, 4),
                ("duoc-my-pham-thuc-pham-chuc-nang", "Dược mỹ phẩm & Vitamin làm đẹp", "my-pham-sac-dep", null, false, false, 5),

                // 8. Thực phẩm & Đồ uống
                ("do-an-vat-banh-keo", "Đồ ăn vặt, Bánh kẹo & Hạt dinh dưỡng", "thuc-pham-do-uong", null, false, false, 1),
                ("ca-phe-tra-sua", "Cà phê, Trà, Sữa & Đồ uống", "thuc-pham-do-uong", null, false, false, 2),
                ("thuc-pham-kho-gia-vi", "Thực phẩm khô, Mì & Gia vị nấu ăn", "thuc-pham-do-uong", null, false, false, 3),
                ("thuc-pham-tuoi-song", "Thực phẩm tươi sống & Đông lạnh", "thuc-pham-do-uong", 5.00m, false, false, 4),
                ("dac-san-qua-bieu", "Đặc sản vùng miền & Hộp quà biếu", "thuc-pham-do-uong", null, false, false, 5),

                // 9. Nhà sách & Văn phòng phẩm
                ("sach-van-hoc-kinh-te", "Sách Kinh tế, Kỹ năng & Khởi nghiệp", "nha-sach-van-phong-pham", null, false, true, 1),
                ("sach-van-hoc-tieu-thuyet", "Sách Văn học, Tiểu thuyết & Truyện", "nha-sach-van-phong-pham", null, false, true, 2),
                ("sach-ngoai-ngu-giao-khoa", "Sách Ngoại ngữ & Tham khảo", "nha-sach-van-phong-pham", null, false, true, 3),
                ("dung-cu-hoc-tap", "Bút viết, Sổ tay & Dụng cụ học tập", "nha-sach-van-phong-pham", null, false, true, 4),
                ("thiet-bi-van-phong-qua-luu-niem", "Văn phòng phẩm & Quà lưu niệm", "nha-sach-van-phong-pham", null, false, true, 5),

                // 10. Sản phẩm số & E-Voucher
                ("the-game-the-cao-vien-thong", "Thẻ Game & Thẻ cào viễn thông", "san-pham-so-evoucher", null, false, false, 1),
                ("key-ban-quyen-phan-mem", "Key bản quyền Windows, Office & Phần mềm", "san-pham-so-evoucher", null, false, false, 2),
                ("khoa-hoc-truc-tuyen-ebook", "Khóa học Online & E-Book", "san-pham-so-evoucher", null, false, false, 3),
                ("evoucher-an-uong-du-lich-spa", "E-Voucher Ăn uống, Giải trí & Spa", "san-pham-so-evoucher", null, false, false, 4)
            };

            foreach (var cc in childCatSeeds)
            {
                if (!catBySlug.TryGetValue(cc.ParentSlug, out var parent)) continue;
                if (!catBySlug.TryGetValue(cc.Slug, out var child))
                {
                    child = new Category
                    {
                        Name = cc.Name,
                        Slug = cc.Slug,
                        ParentId = parent.Id,
                        CommissionRate = cc.CommRate,
                        MaxCommissionAmount = null,
                        HasSizeGuide = cc.HasSizeGuide,
                        IsReturnableDefault = cc.IsReturnable,
                        SortOrder = cc.Sort,
                        IsActive = true,
                        CreatedAt = nowUtc,
                        UpdatedAt = nowUtc
                    };
                    context.Categories.Add(child);
                    catBySlug[cc.Slug] = child;
                }
                else
                {
                    child.Name = cc.Name;
                    child.ParentId = parent.Id;
                    child.SortOrder = cc.Sort;
                    child.IsActive = true;
                }
            }

            await context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not sync detailed category hierarchy.");
        }
    }
}
