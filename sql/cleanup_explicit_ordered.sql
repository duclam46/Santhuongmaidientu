USE [DOAN_BANHANG];
GO

SET NOCOUNT ON;

BEGIN TRAN;

BEGIN TRY
    -- Tạm tắt toàn bộ ràng buộc FK để tránh lỗi khi xóa
    EXEC sp_MSforeachtable 'ALTER TABLE ? NOCHECK CONSTRAINT ALL';

    -- 1) Messaging
    DELETE FROM [SystemNotificationReads];
    DELETE FROM [Messages];
    DELETE FROM [Conversations];
    DELETE FROM [SystemNotifications];

    -- 2) Product / variant images and related items
    DELETE FROM [VariantImages];
    DELETE FROM [ProductImages];

    -- 3) Reviews, size guides, and details
    DELETE FROM [ProductReviews];
    DELETE FROM [SizeGuideDetails];
    DELETE FROM [SizeGuides];

    -- 4) Wishlists, carts
    DELETE FROM [WishlistItems];
    DELETE FROM [CartItems];
    DELETE FROM [Carts];

    -- 5) Inventory / orders
    DELETE FROM [InventoryTransactions];
    DELETE FROM [OrderTrackingLogs];
    DELETE FROM [OrderItems];
    DELETE FROM [Orders];

    -- 6) Blog content
    DELETE FROM [BlogPostProducts];
    DELETE FROM [BlogComments];
    DELETE FROM [BlogPosts];

    -- 7) Partner applications
    DELETE FROM [PartnerApplications];

    -- 8) Warehouse network
    DELETE FROM [WarehouseRoutes];
    DELETE FROM [Warehouses];

    -- 9) Product variants and products
    DELETE FROM [ProductVariants];
    DELETE FROM [Products];

    -- 10) Product related (images already cleared)

    -- 11) User data
    DELETE FROM [UserAddresses];
    DELETE FROM [UserProfiles];

    -- 12) Accounts: keep Admin accounts
    DELETE FROM [Accounts] WHERE [Role] <> 'Admin';

    -- 13) Vouchers
    DELETE FROM [Vouchers];

    -- 14) Miscellaneous
    DELETE FROM [SizeGuides];
    DELETE FROM [SizeGuideDetails];
    DELETE FROM [BlogPostProducts];
    DELETE FROM [Messages];
    DELETE FROM [SystemNotificationReads];

    -- Note: Keep these tables untouched: Colors, Sizes, Categories, __EFMigrationsHistory

    -- Bật lại ràng buộc và kiểm tra
    EXEC sp_MSforeachtable 'ALTER TABLE ? WITH CHECK CHECK CONSTRAINT ALL';

    COMMIT TRAN;
    PRINT 'Explicit ordered cleanup finished. Kept tables: Colors, Sizes, Categories, __EFMigrationsHistory, and Admin accounts.';
END TRY
BEGIN CATCH
    ROLLBACK TRAN;
    EXEC sp_MSforeachtable 'ALTER TABLE ? WITH CHECK CHECK CONSTRAINT ALL';
    DECLARE @ErrMsg NVARCHAR(4000) = ERROR_MESSAGE();
    RAISERROR('Cleanup failed: %s', 16, 1, @ErrMsg);
END CATCH;
GO
