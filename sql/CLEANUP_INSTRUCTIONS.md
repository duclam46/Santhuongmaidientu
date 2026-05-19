Hướng dẫn chạy script xóa dữ liệu (giữ lại `Colors`, `Sizes`, `Categories`)

1) Kiểm tra kết nối tới database `DOAN_BANHANG` trước khi chạy. Sao lưu database nếu cần.

2) Chạy bằng `sqlcmd` (Windows):

```powershell
sqlcmd -S <SERVER_NAME> -d DOAN_BANHANG -i "sql\cleanup_keep_core.sql" -E
```
- Thay `<SERVER_NAME>` bằng `localhost\SQLEXPRESS` hoặc tên server của bạn.
- `-E` dùng xác thực Windows; đổi sang `-U <user> -P <pass>` nếu dùng SQL auth.

3) Hoặc mở file [sql/cleanup_keep_core.sql](sql/cleanup_keep_core.sql) trong SSMS và Execute.

Lưu ý an toàn:
- Script sẽ tạm thời tắt ràng buộc FK trước khi xóa và bật lại sau khi xóa.
- Script bỏ qua bảng `Colors`, `Sizes`, `Categories` và `__EFMigrationsHistory`.
- Luôn sao lưu trước khi thực hiện trên môi trường production.
