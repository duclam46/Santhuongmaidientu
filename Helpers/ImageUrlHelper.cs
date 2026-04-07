namespace DOAN_BANHANG.Helpers;

public static class ImageUrlHelper
{
    /// <summary>
    /// Normalizes image urls stored in DB to a browser-friendly web path.
    /// Handles common legacy formats like missing leading '/', '~/...', Windows paths,
    /// and full physical paths containing '/wwwroot/uploads/...'.
    /// </summary>
    public static string? NormalizeStaticFileUrl(string? rawUrl)
    {
        if (string.IsNullOrWhiteSpace(rawUrl)) return null;

        var u = rawUrl.Trim();
        if (u.Length == 0) return null;

        if (u.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || u.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || u.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            return u;
        }

        u = u.Replace('\\', '/');

        if (u.StartsWith("~/", StringComparison.Ordinal))
        {
            u = "/" + u[2..];
        }

        // If a physical path was stored, try extracting the web-relative uploads segment.
        var uploadsIndex = u.IndexOf("/uploads/", StringComparison.OrdinalIgnoreCase);
        if (uploadsIndex >= 0)
        {
            u = u[uploadsIndex..];
        }

        if (u.StartsWith("/", StringComparison.Ordinal))
        {
            return u;
        }

        return "/" + u;
    }
}
