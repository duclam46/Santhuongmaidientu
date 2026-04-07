namespace DOAN_BANHANG.Helpers;

public static class VietnamTimeHelper
{
    private static readonly Lazy<TimeZoneInfo> _vnTz = new(() =>
    {
        // Windows
        try { return TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time"); }
        catch { }

        // Linux/macOS (if ever hosted there)
        try { return TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh"); }
        catch { }

        return TimeZoneInfo.Local;
    });

    public static TimeZoneInfo VietNamTimeZone => _vnTz.Value;

    // HTML <input type="datetime-local"> posts a DateTime without timezone (Kind=Unspecified).
    // In this project we treat user input as Vietnam local time and store UTC in DB.
    public static DateTime? VietNamLocalInputToUtc(DateTime? value)
    {
        if (!value.HasValue) return null;

        var dt = value.Value;
        if (dt.Kind == DateTimeKind.Utc) return dt;
        if (dt.Kind == DateTimeKind.Local) return dt.ToUniversalTime();

        // Unspecified -> assume Vietnam local
        dt = DateTime.SpecifyKind(dt, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(dt, VietNamTimeZone);
    }

    public static DateTime? UtcToVietNamLocal(DateTime? utc)
    {
        if (!utc.HasValue) return null;
        var dt = DateTime.SpecifyKind(utc.Value, DateTimeKind.Utc);
        return TimeZoneInfo.ConvertTimeFromUtc(dt, VietNamTimeZone);
    }
}
