namespace UluBilge.McpServer.Configuration;

public sealed class UluBilgeApiOptions
{
    public const string SectionName = "UluBilgeApi";

    public string BaseUrl { get; set; } = "https://localhost:7187/";
    public string UserName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 30;
}
