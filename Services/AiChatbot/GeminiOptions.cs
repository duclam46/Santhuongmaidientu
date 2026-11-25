namespace DOAN_BANHANG.Services.AiChatbot;

public class GeminiOptions
{
    public const string SectionName = "Gemini";

    public bool Enabled { get; set; } = true;

    public string ApiKey { get; set; } = string.Empty;

    public string Model { get; set; } = "gemini-2.5-flash";

    public string FallbackModel { get; set; } = "gemini-2.5-flash-lite";

    public int TimeoutSeconds { get; set; } = 25;

    public int MaxHistoryMessages { get; set; } = 8;
}
