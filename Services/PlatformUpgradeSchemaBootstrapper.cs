using DOAN_BANHANG.Models;
using Microsoft.EntityFrameworkCore;

namespace DOAN_BANHANG.Services;

public static class PlatformUpgradeSchemaBootstrapper
{
    public static async Task EnsureUpgradedSchemaAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger("PlatformUpgradeSchemaBootstrapper");
        var db = scope.ServiceProvider.GetRequiredService<DataContext>();

        if (!await db.Database.CanConnectAsync())
        {
            logger.LogError("PlatformUpgradeSchemaBootstrapper: Không thể kết nối tới SQL Server. Vui lòng kiểm tra dịch vụ SQL Server (services.msc) đang chạy.");
            return;
        }

        var statements = new (string Name, string Sql)[]
        {
            ("ProductReviews.MediaUrlsJson", @"
IF OBJECT_ID(N'[dbo].[ProductReviews]', N'U') IS NOT NULL AND COL_LENGTH('dbo.ProductReviews', 'MediaUrlsJson') IS NULL
    ALTER TABLE [dbo].[ProductReviews] ADD [MediaUrlsJson] NVARCHAR(4000) NULL;"),

            ("ProductReviews.VariantName", @"
IF OBJECT_ID(N'[dbo].[ProductReviews]', N'U') IS NOT NULL AND COL_LENGTH('dbo.ProductReviews', 'VariantName') IS NULL
    ALTER TABLE [dbo].[ProductReviews] ADD [VariantName] NVARCHAR(255) NULL;"),

            ("ProductReviews.IsHidden", @"
IF OBJECT_ID(N'[dbo].[ProductReviews]', N'U') IS NOT NULL AND COL_LENGTH('dbo.ProductReviews', 'IsHidden') IS NULL
    ALTER TABLE [dbo].[ProductReviews] ADD [IsHidden] BIT NOT NULL CONSTRAINT [DF_ProductReviews_IsHidden] DEFAULT(0);"),

            ("ProductReviews.ModerationNote", @"
IF OBJECT_ID(N'[dbo].[ProductReviews]', N'U') IS NOT NULL AND COL_LENGTH('dbo.ProductReviews', 'ModerationNote') IS NULL
    ALTER TABLE [dbo].[ProductReviews] ADD [ModerationNote] NVARCHAR(500) NULL;"),

            ("UserProfiles.AutoReplyMessage", @"
IF OBJECT_ID(N'[dbo].[UserProfiles]', N'U') IS NOT NULL AND COL_LENGTH('dbo.UserProfiles', 'AutoReplyMessage') IS NULL
    ALTER TABLE [dbo].[UserProfiles] ADD [AutoReplyMessage] NVARCHAR(1000) NULL;"),

            ("SavedVouchers", @"
IF OBJECT_ID(N'[dbo].[SavedVouchers]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[SavedVouchers] (
        [Id] INT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_SavedVouchers] PRIMARY KEY,
        [AccountId] INT NOT NULL,
        [VoucherId] INT NOT NULL,
        [CreatedAt] DATETIME2 NOT NULL CONSTRAINT [DF_SavedVouchers_CreatedAt] DEFAULT(SYSUTCDATETIME()),
        CONSTRAINT [FK_SavedVouchers_Accounts] FOREIGN KEY ([AccountId]) REFERENCES [dbo].[Accounts]([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_SavedVouchers_Vouchers] FOREIGN KEY ([VoucherId]) REFERENCES [dbo].[Vouchers]([Id]) ON DELETE CASCADE
    );
    CREATE UNIQUE INDEX [IX_SavedVouchers_AccountId_VoucherId] ON [dbo].[SavedVouchers]([AccountId], [VoucherId]);
    CREATE INDEX [IX_SavedVouchers_AccountId] ON [dbo].[SavedVouchers]([AccountId]);
END"),

            ("ShopFollowers", @"
IF OBJECT_ID(N'[dbo].[ShopFollowers]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[ShopFollowers] (
        [Id] INT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_ShopFollowers] PRIMARY KEY,
        [FollowerAccountId] INT NOT NULL,
        [ShopId] INT NOT NULL,
        [CreatedAt] DATETIME2 NOT NULL CONSTRAINT [DF_ShopFollowers_CreatedAt] DEFAULT(SYSUTCDATETIME()),
        CONSTRAINT [FK_ShopFollowers_Follower] FOREIGN KEY ([FollowerAccountId]) REFERENCES [dbo].[Accounts]([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_ShopFollowers_Shop] FOREIGN KEY ([ShopId]) REFERENCES [dbo].[Accounts]([Id]) ON DELETE NO ACTION
    );
    CREATE UNIQUE INDEX [IX_ShopFollowers_Follower_Shop] ON [dbo].[ShopFollowers]([FollowerAccountId], [ShopId]);
    CREATE INDEX [IX_ShopFollowers_ShopId] ON [dbo].[ShopFollowers]([ShopId]);
END"),

            ("PayoutRequests", @"
IF OBJECT_ID(N'[dbo].[PayoutRequests]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[PayoutRequests] (
        [Id] INT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_PayoutRequests] PRIMARY KEY,
        [RequestCode] NVARCHAR(50) NOT NULL,
        [ShopId] INT NOT NULL,
        [Amount] DECIMAL(18,2) NOT NULL,
        [BankName] NVARCHAR(150) NOT NULL,
        [AccountNumber] NVARCHAR(100) NOT NULL,
        [AccountHolder] NVARCHAR(150) NOT NULL,
        [Status] NVARCHAR(30) NOT NULL CONSTRAINT [DF_PayoutRequests_Status] DEFAULT(N'Pending'),
        [AdminNote] NVARCHAR(500) NULL,
        [TransactionRef] NVARCHAR(150) NULL,
        [ProcessedByAdminId] INT NULL,
        [CreatedAt] DATETIME2 NOT NULL CONSTRAINT [DF_PayoutRequests_CreatedAt] DEFAULT(SYSUTCDATETIME()),
        [ProcessedAt] DATETIME2 NULL,
        CONSTRAINT [FK_PayoutRequests_Shop] FOREIGN KEY ([ShopId]) REFERENCES [dbo].[Accounts]([Id]) ON DELETE NO ACTION
    );
    CREATE UNIQUE INDEX [IX_PayoutRequests_RequestCode] ON [dbo].[PayoutRequests]([RequestCode]);
    CREATE INDEX [IX_PayoutRequests_ShopId] ON [dbo].[PayoutRequests]([ShopId]);
    CREATE INDEX [IX_PayoutRequests_Status] ON [dbo].[PayoutRequests]([Status]);
END"),

            ("AuditLogs", @"
IF OBJECT_ID(N'[dbo].[AuditLogs]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[AuditLogs] (
        [Id] INT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_AuditLogs] PRIMARY KEY,
        [ActorAccountId] INT NULL,
        [ActorEmail] NVARCHAR(255) NULL,
        [ActorRole] NVARCHAR(50) NULL,
        [Action] NVARCHAR(100) NOT NULL,
        [EntityType] NVARCHAR(100) NOT NULL,
        [EntityId] NVARCHAR(100) NULL,
        [Detail] NVARCHAR(2000) NULL,
        [IpAddress] NVARCHAR(80) NULL,
        [CreatedAt] DATETIME2 NOT NULL CONSTRAINT [DF_AuditLogs_CreatedAt] DEFAULT(SYSUTCDATETIME())
    );
    CREATE INDEX [IX_AuditLogs_CreatedAt] ON [dbo].[AuditLogs]([CreatedAt] DESC);
    CREATE INDEX [IX_AuditLogs_Action] ON [dbo].[AuditLogs]([Action]);
    CREATE INDEX [IX_AuditLogs_ActorAccountId] ON [dbo].[AuditLogs]([ActorAccountId]);
END"),

            ("HomeBanners", @"
IF OBJECT_ID(N'[dbo].[HomeBanners]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[HomeBanners] (
        [Id] INT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_HomeBanners] PRIMARY KEY,
        [Title] NVARCHAR(200) NOT NULL,
        [Subtitle] NVARCHAR(400) NULL,
        [BadgeText] NVARCHAR(80) NULL,
        [ImageUrl] NVARCHAR(500) NOT NULL,
        [LinkUrl] NVARCHAR(500) NULL,
        [ButtonText] NVARCHAR(80) NULL,
        [SortOrder] INT NOT NULL CONSTRAINT [DF_HomeBanners_SortOrder] DEFAULT(0),
        [IsActive] BIT NOT NULL CONSTRAINT [DF_HomeBanners_IsActive] DEFAULT(1),
        [CreatedAt] DATETIME2 NOT NULL CONSTRAINT [DF_HomeBanners_CreatedAt] DEFAULT(SYSUTCDATETIME())
    );
    CREATE INDEX [IX_HomeBanners_IsActive_SortOrder] ON [dbo].[HomeBanners]([IsActive], [SortOrder]);
END"),

            ("PlatformCampaigns", @"
IF OBJECT_ID(N'[dbo].[PlatformCampaigns]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[PlatformCampaigns] (
        [Id] INT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_PlatformCampaigns] PRIMARY KEY,
        [Name] NVARCHAR(200) NOT NULL,
        [Slug] NVARCHAR(200) NOT NULL,
        [BannerUrl] NVARCHAR(500) NULL,
        [ThemeColor] NVARCHAR(30) NULL CONSTRAINT [DF_PlatformCampaigns_ThemeColor] DEFAULT(N'#008374'),
        [Description] NVARCHAR(1000) NULL,
        [DiscountUpToText] NVARCHAR(100) NULL,
        [StartDate] DATETIME2 NOT NULL,
        [EndDate] DATETIME2 NOT NULL,
        [IsActive] BIT NOT NULL CONSTRAINT [DF_PlatformCampaigns_IsActive] DEFAULT(1),
        [CreatedAt] DATETIME2 NOT NULL CONSTRAINT [DF_PlatformCampaigns_CreatedAt] DEFAULT(SYSUTCDATETIME())
    );
    CREATE UNIQUE INDEX [IX_PlatformCampaigns_Slug] ON [dbo].[PlatformCampaigns]([Slug]);
    CREATE INDEX [IX_PlatformCampaigns_ActiveDates] ON [dbo].[PlatformCampaigns]([IsActive], [StartDate], [EndDate]);
END")
        };

        foreach (var (name, sql) in statements)
        {
            try
            {
                await db.Database.ExecuteSqlRawAsync(sql);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "PlatformUpgradeSchemaBootstrapper failed on step {StepName}.", name);
            }
        }

        logger.LogInformation("PlatformUpgradeSchemaBootstrapper: Database schema check completed.");
    }
}
