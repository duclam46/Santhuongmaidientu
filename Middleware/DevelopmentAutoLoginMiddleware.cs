namespace DOAN_BANHANG.Middleware;

public sealed class DevelopmentAutoLoginMiddleware
{
    private readonly RequestDelegate _next;
    public DevelopmentAutoLoginMiddleware(RequestDelegate next) => _next = next;
    
    public async Task InvokeAsync(HttpContext context)
    {
        // Tính năng tự động đăng nhập đã bị loại bỏ hoàn toàn.
        await _next(context);
    }
}
