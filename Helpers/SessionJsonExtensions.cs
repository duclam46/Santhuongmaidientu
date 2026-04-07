using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace DOAN_BANHANG.Helpers;

public static class SessionJsonExtensions
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static void SetJson<T>(this ISession session, string key, T value)
    {
        session.SetString(key, JsonSerializer.Serialize(value, Options));
    }

    public static T? GetJson<T>(this ISession session, string key)
    {
        var raw = session.GetString(key);
        if (string.IsNullOrWhiteSpace(raw)) return default;

        return JsonSerializer.Deserialize<T>(raw, Options);
    }
}
