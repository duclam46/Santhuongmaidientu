using System.Text.Json;
using DOAN_BANHANG.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DOAN_BANHANG.Controllers;

[ApiController]
[AllowAnonymous]
[Route("webhooks/ghn")]
public class GhnWebhookController : ControllerBase
{
    private readonly DataContext _db;

    public GhnWebhookController(DataContext db)
    {
        _db = db;
    }

    [HttpPost("status")]
    public async Task<IActionResult> Status([FromBody] JsonElement payload, CancellationToken cancellationToken)
    {
        // GHN callback payload format may vary across versions.
        // We parse defensively and accept both root-level and nested data fields.
        var orderCode = GetString(payload, "order_code")
            ?? GetStringInData(payload, "order_code")
            ?? GetString(payload, "OrderCode");

        if (string.IsNullOrWhiteSpace(orderCode))
        {
            return BadRequest(new { ok = false, message = "order_code is required." });
        }

        var order = await _db.Orders
            .FirstOrDefaultAsync(x => x.TrackingCode == orderCode, cancellationToken);

        if (order is null)
        {
            // Accept callback to avoid retries storm; just report unknown order.
            return Ok(new { ok = true, matched = false, order_code = orderCode });
        }

        var status = (GetString(payload, "status")
            ?? GetStringInData(payload, "status")
            ?? "Shipping").Trim();

        var statusName = GetString(payload, "status_name")
            ?? GetStringInData(payload, "status_name");

        var description = GetString(payload, "description")
            ?? GetStringInData(payload, "description")
            ?? GetString(payload, "message")
            ?? GetStringInData(payload, "message");

        var updatedRaw = GetString(payload, "updated_date")
            ?? GetStringInData(payload, "updated_date")
            ?? GetString(payload, "time")
            ?? GetStringInData(payload, "time");

        var noteParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(statusName)) noteParts.Add(statusName.Trim());
        if (!string.IsNullOrWhiteSpace(description)) noteParts.Add(description.Trim());
        var note = noteParts.Count > 0 ? string.Join(" - ", noteParts) : $"GHN callback: {status}";
        if (note.Length > 255) note = note[..255];

        var createdAtUtc = DateTime.UtcNow;
        if (DateTimeOffset.TryParse(updatedRaw, out var updated))
        {
            createdAtUtc = updated.UtcDateTime;
        }

        _db.OrderTrackingLogs.Add(new OrderTrackingLog
        {
            OrderId = order.Id,
            Status = status,
            Note = note,
            CreatedAt = createdAtUtc
        });

        order.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new { ok = true, matched = true, order_code = orderCode, status });
    }

    private static string? GetStringInData(JsonElement root, string key)
    {
        if (root.ValueKind != JsonValueKind.Object) return null;
        if (!root.TryGetProperty("data", out var data)) return null;
        return GetString(data, key);
    }

    private static string? GetString(JsonElement obj, string key)
    {
        if (obj.ValueKind != JsonValueKind.Object) return null;
        if (!obj.TryGetProperty(key, out var value)) return null;

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }
}
