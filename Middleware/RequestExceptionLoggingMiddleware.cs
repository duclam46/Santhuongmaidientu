using System.Text;

namespace DOAN_BANHANG.Middleware;

public sealed class RequestExceptionLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestExceptionLoggingMiddleware> _logger;

    public RequestExceptionLoggingMiddleware(RequestDelegate next, ILogger<RequestExceptionLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task Invoke(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Unhandled exception while processing request");
            sb.AppendLine($"Method: {context.Request.Method}");
            sb.AppendLine($"Path: {context.Request.Path}{context.Request.QueryString}");

            var endpoint = context.GetEndpoint();
            if (endpoint != null)
            {
                sb.AppendLine($"Endpoint: {endpoint.DisplayName}");
            }

            if (context.Request.RouteValues.Count > 0)
            {
                sb.Append("RouteValues: ");
                sb.AppendLine(string.Join(", ", context.Request.RouteValues.Select(kvp => $"{kvp.Key}={kvp.Value}")));
            }

            _logger.LogError(ex, sb.ToString());
            throw;
        }
    }
}
