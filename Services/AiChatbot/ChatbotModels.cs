namespace DOAN_BANHANG.Services.AiChatbot;

public class AiChatResult
{
    public string Reply { get; set; } = string.Empty;
    public List<AiProductCard> Products { get; set; } = new();
    public List<AiQuickLink> QuickLinks { get; set; } = new();
    public string Mode { get; set; } = "local"; // "gemini" | "local"
}

public class AiProductCard
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public int MinPrice { get; set; }
    public int MaxPrice { get; set; }
    public double Rating { get; set; }
    public string? CategoryName { get; set; }
    public string? BrandName { get; set; }
    public bool IsMall { get; set; }
    public bool IsFlashSale { get; set; }
}

public class AiQuickLink
{
    public string Label { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
}

public class AiChatHistoryItem
{
    public string Sender { get; set; } = "user"; // "user" | "bot"
    public string Content { get; set; } = string.Empty;
}

public class AiToolExecutionOutput
{
    public string Text { get; set; } = string.Empty;
    public List<AiProductCard> Products { get; set; } = new();
    public List<AiQuickLink> QuickLinks { get; set; } = new();
}
