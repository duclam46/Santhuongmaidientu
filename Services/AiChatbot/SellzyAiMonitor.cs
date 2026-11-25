using System.Collections.Concurrent;

namespace DOAN_BANHANG.Services.AiChatbot;

public class AiChatTraceEntry
{
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public int AccountId { get; set; }
    public string PromptPreview { get; set; } = string.Empty;
    public string Route { get; set; } = string.Empty; // guardrail-blocked | cache-hit | router-local | router-lite | router-flash | fallback
    public string ModelUsed { get; set; } = string.Empty;
    public List<string> ToolsCalled { get; set; } = new();
    public long DurationMs { get; set; }
    public bool GuardrailTriggered { get; set; }
    public string? GuardrailReason { get; set; }
    public bool Success { get; set; } = true;
}

/// <summary>
/// Bộ giám sát (Monitoring & Trace Telemetry) thời gian thực cho Trợ lý AI Sellzy.
/// Lưu vòng đệm 100 lượt xử lý gần nhất trên RAM để Admin kiểm tra Route, Model, Tool, Latency và Guardrails.
/// </summary>
public class SellzyAiMonitor
{
    private const int MaxEntries = 100;
    private readonly ConcurrentQueue<AiChatTraceEntry> _traces = new();

    public void Record(AiChatTraceEntry entry)
    {
        _traces.Enqueue(entry);
        while (_traces.Count > MaxEntries && _traces.TryDequeue(out _)) { }
    }

    public object GetDashboardSnapshot(int recentCount = 30)
    {
        var all = _traces.ToArray();
        var total = all.Length;
        var cacheHits = all.Count(x => x.Route == "cache-hit");
        var guardrailBlocks = all.Count(x => x.GuardrailTriggered);
        var avgMs = total > 0 ? Math.Round(all.Average(x => x.DurationMs), 1) : 0;

        var routeBreakdown = all
            .GroupBy(x => x.Route)
            .ToDictionary(g => g.Key, g => g.Count());

        var recent = all
            .Reverse()
            .Take(Math.Clamp(recentCount, 1, 100))
            .Select(x => new
            {
                timeLocal = x.TimestampUtc.AddHours(7).ToString("HH:mm:ss dd/MM"),
                x.AccountId,
                x.PromptPreview,
                x.Route,
                x.ModelUsed,
                x.ToolsCalled,
                x.DurationMs,
                x.GuardrailTriggered,
                x.GuardrailReason,
                x.Success
            })
            .ToList();

        return new
        {
            totalTrackedRequests = total,
            cacheHitRatePercent = total > 0 ? Math.Round(cacheHits * 100.0 / total, 1) : 0,
            guardrailBlockedCount = guardrailBlocks,
            avgLatencyMs = avgMs,
            routeBreakdown,
            recentTraces = recent
        };
    }
}
