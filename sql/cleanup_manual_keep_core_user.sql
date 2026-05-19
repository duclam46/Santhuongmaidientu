-- Generated to match the user's cleanup commands exactly
USE [DOAN_BANHANG];
GO

-- 1. Tắt ràng buộc khóa ngoại
EXEC sp_MSforeachtable 'ALTER TABLE ? NOCHECK CONSTRAINT ALL';

-- 2. Xóa dữ liệu các bảng (Trừ Categories, Colors, Sizes)
DELETE FROM [SystemNotificationReads];
DELETE FROM [SystemNotifications];
DELETE FROM [Messages];
DELETE FROM [Conversations];
DELETE FROM [BlogPostProducts];
DELETE FROM [BlogComments];
DELETE FROM [BlogPosts];
DELETE FROM [PartnerApplications];
DELETE FROM [InventoryTransactions];
DELETE FROM [OrderTrackingLogs];
DELETE FROM [OrderItems];
DELETE FROM [Orders];
DELETE FROM [Vouchers];
DELETE FROM [CartItems];
DELETE FROM [Carts];
DELETE FROM [WishlistItems];
DELETE FROM [SizeGuideDetails];
DELETE FROM [SizeGuides];
DELETE FROM [VariantImages];
DELETE FROM [ProductImages];
DELETE FROM [ProductVariants];
DELETE FROM [ProductReviews];
DELETE FROM [Products];
DELETE FROM [UserAddresses];
DELETE FROM [UserProfiles];

-- 3. Xóa các tài khoản không phải Admin
DELETE FROM [Accounts] WHERE [Role] <> 'Admin';

-- 4. Bật lại ràng buộc khóa ngoại
EXEC sp_MSforeachtable 'ALTER TABLE ? WITH CHECK CHECK CONSTRAINT ALL';

PRINT 'DA DON DEP DATABASE THANH CONG!';
