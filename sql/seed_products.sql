SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRAN;

    /* ========= 0) REQUIRE TABLES ========= */
    IF OBJECT_ID('dbo.Brands','U') IS NULL
        THROW 50000, 'Missing table dbo.Brands', 1;
    IF OBJECT_ID('dbo.Products','U') IS NULL
        THROW 50000, 'Missing table dbo.Products', 1;
    IF OBJECT_ID('dbo.Categories','U') IS NULL
        THROW 50000, 'Missing table dbo.Categories', 1;
    IF OBJECT_ID('dbo.ProductCategories','U') IS NULL
        THROW 50000, 'Missing table dbo.ProductCategories', 1;
    IF OBJECT_ID('dbo.ProductImages','U') IS NULL
        THROW 50000, 'Missing table dbo.ProductImages', 1;

    IF COL_LENGTH('dbo.Categories','CategoryId') IS NULL
        THROW 50000, 'dbo.Categories must have CategoryId', 1;
    IF COL_LENGTH('dbo.Categories','Slug') IS NULL
        THROW 50000, 'dbo.Categories must have Slug', 1;

    DECLARE @Now datetime2(0) = SYSDATETIME();

    /* ========= 1) SEED BRANDS ========= */
    DROP TABLE IF EXISTS #Brands;
    CREATE TABLE #Brands
    (
        BrandSlug nvarchar(200) NOT NULL,
        BrandName nvarchar(255) NOT NULL
    );

    INSERT INTO #Brands (BrandSlug, BrandName) VALUES
    (N'zenis',        N'Zenis'),
    (N'nike',         N'Nike'),
    (N'adidas',       N'Adidas'),
    (N'uniqlo',       N'Uniqlo'),
    (N'local-brand',  N'Local Brand');

    IF COL_LENGTH('dbo.Brands','Slug') IS NOT NULL AND COL_LENGTH('dbo.Brands','Name') IS NOT NULL
    BEGIN
        INSERT INTO dbo.Brands ([Name],[Slug])
        SELECT b.BrandName, b.BrandSlug
        FROM #Brands b
        WHERE NOT EXISTS (SELECT 1 FROM dbo.Brands x WHERE x.[Slug] = b.BrandSlug);
    END
    ELSE IF COL_LENGTH('dbo.Brands','Slug') IS NOT NULL
    BEGIN
        INSERT INTO dbo.Brands ([Slug])
        SELECT b.BrandSlug
        FROM #Brands b
        WHERE NOT EXISTS (SELECT 1 FROM dbo.Brands x WHERE x.[Slug] = b.BrandSlug);
    END
    ELSE IF COL_LENGTH('dbo.Brands','Name') IS NOT NULL
    BEGIN
        INSERT INTO dbo.Brands ([Name])
        SELECT b.BrandName
        FROM #Brands b
        WHERE NOT EXISTS (SELECT 1 FROM dbo.Brands x WHERE x.[Name] = b.BrandName);
    END
    ELSE
    BEGIN
        THROW 50000, 'dbo.Brands must have Name and/or Slug columns', 1;
    END

    /* ========= 2) BRAND MAP ========= */
    IF COL_LENGTH('dbo.Brands','BrandId') IS NULL
        THROW 50000, 'dbo.Brands must have BrandId (int identity)', 1;

    DROP TABLE IF EXISTS #BrandMap;
    CREATE TABLE #BrandMap
    (
        BrandSlug nvarchar(200) NOT NULL PRIMARY KEY,
        BrandId int NOT NULL
    );

    IF COL_LENGTH('dbo.Brands','Slug') IS NOT NULL
    BEGIN
        INSERT INTO #BrandMap(BrandSlug, BrandId)
        SELECT b.BrandSlug, br.BrandId
        FROM #Brands b
        JOIN dbo.Brands br ON br.Slug = b.BrandSlug;
    END
    ELSE
    BEGIN
        INSERT INTO #BrandMap(BrandSlug, BrandId)
        SELECT b.BrandSlug, br.BrandId
        FROM #Brands b
        JOIN dbo.Brands br ON br.[Name] = b.BrandName;
    END

    /* ========= 3) SEED PRODUCTS ========= */
    DROP TABLE IF EXISTS #Products;
    CREATE TABLE #Products
    (
        SKU nvarchar(64) NULL,
        ProductSlug nvarchar(200) NOT NULL,
        ProductName nvarchar(255) NOT NULL,
        BrandSlug nvarchar(200) NOT NULL,
        Price decimal(18,2) NULL,
        OldPrice decimal(18,2) NULL,
        StockQuantity int NULL,
        ShortDescription nvarchar(500) NULL,
        Description nvarchar(max) NULL,
        IsActive bit NULL
    );

    INSERT INTO #Products (SKU, ProductSlug, ProductName, BrandSlug, Price, OldPrice, StockQuantity, ShortDescription, Description, IsActive) VALUES
    (N'SKU-TSHIRT-001', N'ao-thun-basic-nam',      N'Áo thun basic nam',        N'uniqlo',       199000, 249000,  50, N'Áo thun cotton mềm, dễ phối.', N'Áo thun basic chất liệu cotton, phù hợp mặc hằng ngày.', 1),
    (N'SKU-SHOE-001',   N'giay-the-thao-trang',    N'Giày thể thao trắng',      N'nike',         899000, 999000,  20, N'Giày thể thao màu trắng.',     N'Giày thể thao đế êm, phù hợp đi chơi và tập luyện.',     1),
    (N'SKU-JACKET-001', N'ao-khoac-du-nu',         N'Áo khoác dù nữ',           N'adidas',       499000, 599000,  15, N'Áo khoác nhẹ, chống gió.',      N'Áo khoác dù nhẹ, tiện lợi khi đi mưa gió.',              1),
    (N'SKU-BAG-001',    N'tui-deo-cheo-mini',      N'Túi đeo chéo mini',        N'local-brand',  159000, 199000,  80, N'Túi nhỏ gọn.',                  N'Túi đeo chéo mini phù hợp đi dạo, đi học.',              1),
    (N'SKU-HAT-001',    N'mu-luoi-trai-cay',       N'Mũ lưỡi trai',             N'zenis',         99000, 129000, 100, N'Mũ lưỡi trai thời trang.',      N'Mũ lưỡi trai form chuẩn, che nắng tốt.',                 1);

    IF COL_LENGTH('dbo.Products','ProductId') IS NULL
        THROW 50000, 'dbo.Products must have ProductId (int identity)', 1;

    DECLARE @ProductsHasSku bit = IIF(COL_LENGTH('dbo.Products','SKU') IS NOT NULL, 1, 0);
    DECLARE @ProductsHasSlug bit = IIF(COL_LENGTH('dbo.Products','Slug') IS NOT NULL, 1, 0);
    IF @ProductsHasSku = 0 AND @ProductsHasSlug = 0
        THROW 50000, 'dbo.Products must have SKU or Slug column', 1;

    /* Build a safe INSERT with matching column/value counts */
    DECLARE @pCols nvarchar(max) = N'';
    DECLARE @pVals nvarchar(max) = N'';

    IF COL_LENGTH('dbo.Products','BrandId') IS NOT NULL BEGIN SET @pCols += N'[BrandId],'; SET @pVals += N'bm.BrandId,'; END
    IF COL_LENGTH('dbo.Products','Name') IS NOT NULL BEGIN SET @pCols += N'[Name],'; SET @pVals += N'p.ProductName,'; END
    IF COL_LENGTH('dbo.Products','Slug') IS NOT NULL BEGIN SET @pCols += N'[Slug],'; SET @pVals += N'p.ProductSlug,'; END
    IF COL_LENGTH('dbo.Products','SKU') IS NOT NULL BEGIN SET @pCols += N'[SKU],'; SET @pVals += N'p.SKU,'; END
    IF COL_LENGTH('dbo.Products','Price') IS NOT NULL BEGIN SET @pCols += N'[Price],'; SET @pVals += N'p.Price,'; END
    IF COL_LENGTH('dbo.Products','OldPrice') IS NOT NULL BEGIN SET @pCols += N'[OldPrice],'; SET @pVals += N'p.OldPrice,'; END
    IF COL_LENGTH('dbo.Products','StockQuantity') IS NOT NULL BEGIN SET @pCols += N'[StockQuantity],'; SET @pVals += N'p.StockQuantity,'; END
    IF COL_LENGTH('dbo.Products','ShortDescription') IS NOT NULL BEGIN SET @pCols += N'[ShortDescription],'; SET @pVals += N'p.ShortDescription,'; END
    IF COL_LENGTH('dbo.Products','Description') IS NOT NULL BEGIN SET @pCols += N'[Description],'; SET @pVals += N'p.Description,'; END
    IF COL_LENGTH('dbo.Products','IsActive') IS NOT NULL BEGIN SET @pCols += N'[IsActive],'; SET @pVals += N'ISNULL(p.IsActive,1),'; END
    IF COL_LENGTH('dbo.Products','CreatedAt') IS NOT NULL BEGIN SET @pCols += N'[CreatedAt],'; SET @pVals += N'@Now,'; END
    IF COL_LENGTH('dbo.Products','UpdatedAt') IS NOT NULL BEGIN SET @pCols += N'[UpdatedAt],'; SET @pVals += N'@Now,'; END

    IF LEN(@pCols) = 0
        THROW 50000, 'No compatible columns found to insert into dbo.Products', 1;

    -- Trim trailing commas
    SET @pCols = LEFT(@pCols, LEN(@pCols) - 1);
    SET @pVals = LEFT(@pVals, LEN(@pVals) - 1);

    DECLARE @SqlInsertProducts nvarchar(max) = N'
        INSERT INTO dbo.Products (' + @pCols + N')
        SELECT ' + @pVals + N'
        FROM #Products p
        JOIN #BrandMap bm ON bm.BrandSlug = p.BrandSlug
        WHERE NOT EXISTS (
            SELECT 1 FROM dbo.Products x
            WHERE ' + CASE WHEN @ProductsHasSku = 1 THEN N'(x.SKU = p.SKU AND p.SKU IS NOT NULL)' ELSE N'(x.Slug = p.ProductSlug)' END + N'
        );
    ';

    EXEC sp_executesql @SqlInsertProducts, N'@Now datetime2(0)', @Now=@Now;

    /* ========= 4) PRODUCT MAP ========= */
    DROP TABLE IF EXISTS #ProductMap;
    CREATE TABLE #ProductMap
    (
        ProductKey nvarchar(200) NOT NULL PRIMARY KEY,
        ProductId int NOT NULL
    );

    IF @ProductsHasSku = 1
    BEGIN
        INSERT INTO #ProductMap(ProductKey, ProductId)
        SELECT p.SKU, pr.ProductId
        FROM #Products p
        JOIN dbo.Products pr ON pr.SKU = p.SKU
        WHERE p.SKU IS NOT NULL;
    END
    ELSE
    BEGIN
        INSERT INTO #ProductMap(ProductKey, ProductId)
        SELECT p.ProductSlug, pr.ProductId
        FROM #Products p
        JOIN dbo.Products pr ON pr.Slug = p.ProductSlug;
    END

    /* ========= 5) PRODUCT-CATEGORIES ========= */
    IF COL_LENGTH('dbo.ProductCategories','ProductId') IS NULL OR COL_LENGTH('dbo.ProductCategories','CategoryId') IS NULL
        THROW 50000, 'dbo.ProductCategories must have ProductId and CategoryId', 1;

    DROP TABLE IF EXISTS #PC;
    CREATE TABLE #PC
    (
        ProductKey nvarchar(200) NOT NULL,
        CategorySlug nvarchar(200) NOT NULL,
        IsPrimary bit NOT NULL
    );

    -- TODO: đổi slug này đúng với Categories của bạn
    INSERT INTO #PC(ProductKey, CategorySlug, IsPrimary) VALUES
    (N'SKU-TSHIRT-001', N'menu-thoi-trang-nam', 1),
    (N'SKU-SHOE-001',   N'menu-giay-dep',       1),
    (N'SKU-JACKET-001', N'menu-thoi-trang-nu',  1),
    (N'SKU-BAG-001',    N'menu-phu-kien',       1),
    (N'SKU-HAT-001',    N'menu-phu-kien',       1);

    ;WITH x AS
    (
        SELECT ProductKey, CategorySlug, IsPrimary,
               rn = ROW_NUMBER() OVER (PARTITION BY ProductKey ORDER BY CASE WHEN IsPrimary=1 THEN 0 ELSE 1 END, CategorySlug)
        FROM #PC
    )
    UPDATE x SET IsPrimary = CASE WHEN rn = 1 THEN 1 ELSE 0 END;

    DECLARE @PC_HasIsPrimary bit = IIF(COL_LENGTH('dbo.ProductCategories','IsPrimary') IS NOT NULL, 1, 0);

    IF @PC_HasIsPrimary = 1
    BEGIN
        INSERT INTO dbo.ProductCategories (ProductId, CategoryId, IsPrimary)
        SELECT pm.ProductId, c.CategoryId, pc.IsPrimary
        FROM #PC pc
        JOIN #ProductMap pm ON pm.ProductKey = pc.ProductKey
        JOIN dbo.Categories c ON c.Slug = pc.CategorySlug
        WHERE NOT EXISTS (
            SELECT 1 FROM dbo.ProductCategories x
            WHERE x.ProductId = pm.ProductId AND x.CategoryId = c.CategoryId
        );

        ;WITH ranked AS
        (
            SELECT pc2.*, rn = ROW_NUMBER() OVER (PARTITION BY pc2.ProductId ORDER BY CASE WHEN pc2.IsPrimary=1 THEN 0 ELSE 1 END, pc2.CategoryId)
            FROM dbo.ProductCategories pc2
        )
        UPDATE ranked SET IsPrimary = CASE WHEN rn = 1 THEN 1 ELSE 0 END;
    END
    ELSE
    BEGIN
        INSERT INTO dbo.ProductCategories (ProductId, CategoryId)
        SELECT pm.ProductId, c.CategoryId
        FROM #PC pc
        JOIN #ProductMap pm ON pm.ProductKey = pc.ProductKey
        JOIN dbo.Categories c ON c.Slug = pc.CategorySlug
        WHERE NOT EXISTS (
            SELECT 1 FROM dbo.ProductCategories x
            WHERE x.ProductId = pm.ProductId AND x.CategoryId = c.CategoryId
        );
    END

    /* ========= 6) PRODUCT-IMAGES ========= */
    IF COL_LENGTH('dbo.ProductImages','ProductId') IS NULL OR COL_LENGTH('dbo.ProductImages','ImageUrl') IS NULL
        THROW 50000, 'dbo.ProductImages must have ProductId and ImageUrl', 1;

    DROP TABLE IF EXISTS #PI;
    CREATE TABLE #PI
    (
        ProductKey nvarchar(200) NOT NULL,
        ImageUrl nvarchar(500) NOT NULL,
        IsPrimary bit NOT NULL,
        SortOrder int NOT NULL
    );

    INSERT INTO #PI(ProductKey, ImageUrl, IsPrimary, SortOrder) VALUES
    (N'SKU-TSHIRT-001', N'/assets/images/home-2/product-image-1.png', 1, 1),
    (N'SKU-TSHIRT-001', N'/assets/images/home-2/product-image-2.png', 0, 2),
    (N'SKU-SHOE-001',   N'/assets/images/home-2/product-image-3.png', 1, 1),
    (N'SKU-SHOE-001',   N'/assets/images/home-2/product-image-4.png', 0, 2),
    (N'SKU-JACKET-001', N'/assets/images/home-2/product-image-5.png', 1, 1),
    (N'SKU-JACKET-001', N'/assets/images/home-2/product-image-6.png', 0, 2),
    (N'SKU-BAG-001',    N'/assets/images/home-2/product-image-7.png', 1, 1),
    (N'SKU-HAT-001',    N'/assets/images/home-2/product-image-8.png', 1, 1);

    ;WITH x AS
    (
        SELECT ProductKey, ImageUrl, IsPrimary, SortOrder,
               rn = ROW_NUMBER() OVER (PARTITION BY ProductKey ORDER BY CASE WHEN IsPrimary=1 THEN 0 ELSE 1 END, SortOrder, ImageUrl)
        FROM #PI
    )
    UPDATE x SET IsPrimary = CASE WHEN rn = 1 THEN 1 ELSE 0 END;

    DECLARE @PI_HasIsPrimary bit = IIF(COL_LENGTH('dbo.ProductImages','IsPrimary') IS NOT NULL, 1, 0);
    DECLARE @PI_HasSortOrder bit = IIF(COL_LENGTH('dbo.ProductImages','SortOrder') IS NOT NULL, 1, 0);

    IF @PI_HasIsPrimary = 1 AND @PI_HasSortOrder = 1
    BEGIN
        INSERT INTO dbo.ProductImages (ProductId, ImageUrl, IsPrimary, SortOrder)
        SELECT pm.ProductId, i.ImageUrl, i.IsPrimary, i.SortOrder
        FROM #PI i
        JOIN #ProductMap pm ON pm.ProductKey = i.ProductKey
        WHERE NOT EXISTS (
            SELECT 1 FROM dbo.ProductImages x
            WHERE x.ProductId = pm.ProductId AND x.ImageUrl = i.ImageUrl
        );
    END
    ELSE IF @PI_HasIsPrimary = 1 AND @PI_HasSortOrder = 0
    BEGIN
        INSERT INTO dbo.ProductImages (ProductId, ImageUrl, IsPrimary)
        SELECT pm.ProductId, i.ImageUrl, i.IsPrimary
        FROM #PI i
        JOIN #ProductMap pm ON pm.ProductKey = i.ProductKey
        WHERE NOT EXISTS (
            SELECT 1 FROM dbo.ProductImages x
            WHERE x.ProductId = pm.ProductId AND x.ImageUrl = i.ImageUrl
        );
    END
    ELSE IF @PI_HasIsPrimary = 0 AND @PI_HasSortOrder = 1
    BEGIN
        INSERT INTO dbo.ProductImages (ProductId, ImageUrl, SortOrder)
        SELECT pm.ProductId, i.ImageUrl, i.SortOrder
        FROM #PI i
        JOIN #ProductMap pm ON pm.ProductKey = i.ProductKey
        WHERE NOT EXISTS (
            SELECT 1 FROM dbo.ProductImages x
            WHERE x.ProductId = pm.ProductId AND x.ImageUrl = i.ImageUrl
        );
    END
    ELSE
    BEGIN
        INSERT INTO dbo.ProductImages (ProductId, ImageUrl)
        SELECT pm.ProductId, i.ImageUrl
        FROM #PI i
        JOIN #ProductMap pm ON pm.ProductKey = i.ProductKey
        WHERE NOT EXISTS (
            SELECT 1 FROM dbo.ProductImages x
            WHERE x.ProductId = pm.ProductId AND x.ImageUrl = i.ImageUrl
        );
    END

    -- Normalize primary to satisfy filtered unique index (if exists)
    IF @PI_HasIsPrimary = 1
    BEGIN
        DECLARE @PiIdCol sysname = NULL;
        IF COL_LENGTH('dbo.ProductImages','ProductImageId') IS NOT NULL SET @PiIdCol = N'ProductImageId';
        ELSE IF COL_LENGTH('dbo.ProductImages','Id') IS NOT NULL SET @PiIdCol = N'Id';
        ELSE SET @PiIdCol = N'ImageUrl';

        DECLARE @SqlNormalizePI nvarchar(max) = N'
            ;WITH ranked AS (
                SELECT pi.*, rn = ROW_NUMBER() OVER (
                    PARTITION BY pi.ProductId
                    ORDER BY CASE WHEN pi.IsPrimary=1 THEN 0 ELSE 1 END,
                             ' + CASE WHEN @PI_HasSortOrder = 1 THEN N'ISNULL(pi.SortOrder, 2147483647),' ELSE N'' END + N'
                             pi.' + QUOTENAME(@PiIdCol) + N'
                )
                FROM dbo.ProductImages pi
            )
            UPDATE ranked SET IsPrimary = CASE WHEN rn = 1 THEN 1 ELSE 0 END;
        ';

        EXEC sp_executesql @SqlNormalizePI;
    END

    COMMIT;
    PRINT N'SEED OK: Brands + Products + ProductCategories + ProductImages';

END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK;
    DECLARE @Err nvarchar(max) = ERROR_MESSAGE();
    PRINT @Err;
    THROW;
END CATCH;
