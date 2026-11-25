-- =========================================================================
-- KỊCH BẢN NÂNG CẤP CƠ SỞ DỮ LIỆU SÀN TMĐT ĐA NGÀNH (MULTI-INDUSTRY)
-- Giữ lại toàn bộ dữ liệu Đăng nhập & Tài khoản (Accounts, UserProfiles, UserAddresses, PartnerApplications, Wallets)
-- Xóa dữ liệu sản phẩm/đơn hàng cũ và nâng cấp cấu trúc bảng
-- =========================================================================

-- Lưu ý: Đổi tên DB bên dưới thành [DOAN_TOTNGHIEP] hoặc [DOAN_BANHANG] tùy máy của bạn
-- USE [DOAN_TOTNGHIEP];
-- GO

-- 1. Nâng cấp bảng Categories
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
GO

-- 2. Tạo bảng Thương hiệu (Brands)
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
GO

-- 3. Nâng cấp bảng Products
IF COL_LENGTH('dbo.Products', 'BrandId') IS NULL
    ALTER TABLE [dbo].[Products] ADD [BrandId] INT NULL CONSTRAINT [FK_Products_Brands] FOREIGN KEY REFERENCES [dbo].[Brands]([Id]);

IF COL_LENGTH('dbo.Products', 'WarrantyMonths') IS NULL
    ALTER TABLE [dbo].[Products] ADD [WarrantyMonths] INT NOT NULL DEFAULT 0;

IF COL_LENGTH('dbo.Products', 'WarrantyType') IS NULL
    ALTER TABLE [dbo].[Products] ADD [WarrantyType] NVARCHAR(100) NULL;

IF COL_LENGTH('dbo.Products', 'IsReturnable') IS NULL
    ALTER TABLE [dbo].[Products] ADD [IsReturnable] BIT NOT NULL DEFAULT 1;
GO

-- 4. Tạo hệ thống Phân loại biến thể động 2 tầng
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
GO

-- 5. Nâng cấp ProductVariants
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
GO

-- 6. Tạo hệ thống Thông số kỹ thuật động theo Danh mục (Mô hình EAV)
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
GO

-- 7. Nâng cấp OrderItems & Orders & Products (Giai đoạn mở rộng: Sản phẩm số, Hạn sử dụng, Giá vốn, Hóa đơn VAT)
IF COL_LENGTH('dbo.OrderItems', 'VariantDescription') IS NULL
    ALTER TABLE [dbo].[OrderItems] ADD [VariantDescription] NVARCHAR(255) NULL;

IF COL_LENGTH('dbo.OrderItems', 'CommissionAmount') IS NULL
    ALTER TABLE [dbo].[OrderItems] ADD [CommissionAmount] DECIMAL(18, 2) NOT NULL DEFAULT 0;

IF COL_LENGTH('dbo.OrderItems', 'UnitCostPrice') IS NULL
    ALTER TABLE [dbo].[OrderItems] ADD [UnitCostPrice] DECIMAL(18, 2) NOT NULL DEFAULT 0;

IF COL_LENGTH('dbo.ProductVariants', 'CostPrice') IS NULL
    ALTER TABLE [dbo].[ProductVariants] ADD [CostPrice] DECIMAL(18, 2) NOT NULL DEFAULT 0;

IF COL_LENGTH('dbo.Products', 'IsDigital') IS NULL
    ALTER TABLE [dbo].[Products] ADD [IsDigital] BIT NOT NULL DEFAULT 0;

IF COL_LENGTH('dbo.Products', 'DigitalContent') IS NULL
    ALTER TABLE [dbo].[Products] ADD [DigitalContent] NVARCHAR(1000) NULL;

IF COL_LENGTH('dbo.Products', 'ManufacturingDate') IS NULL
    ALTER TABLE [dbo].[Products] ADD [ManufacturingDate] DATETIME2(7) NULL;

IF COL_LENGTH('dbo.Products', 'ExpiryDate') IS NULL
    ALTER TABLE [dbo].[Products] ADD [ExpiryDate] DATETIME2(7) NULL;

IF COL_LENGTH('dbo.Products', 'ShippingCondition') IS NULL
    ALTER TABLE [dbo].[Products] ADD [ShippingCondition] NVARCHAR(50) NULL;

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
GO

-- 8. Dọn dẹp dữ liệu sản phẩm/đơn hàng cũ (Chỉ giữ lại Accounts, UserProfiles, UserAddresses, PartnerApplications, Wallets)
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
GO
