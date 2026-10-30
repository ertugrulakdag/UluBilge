using Microsoft.EntityFrameworkCore;

namespace UluBilge.Api.MCP.OAuth;

public sealed class McpOAuthOptions
{
    public const string Policy = "McpOAuth";
    public const string Scope = "mcp:tools";

    public string ServerUrl { get; set; } = string.Empty;
    public string DatabasePath { get; set; } = "App_Data/mcp-oauth.db";
    public string? SigningCertificatePath { get; set; }
    public string? SigningCertificatePassword { get; set; }
    public string? EncryptionCertificatePath { get; set; }
    public string? EncryptionCertificatePassword { get; set; }
    public List<McpOAuthClient> Clients { get; set; } = [];
    public List<McpOAuthClient> DevelopmentClients { get; set; } = [];

    public Uri Issuer => new(ServerUrl.TrimEnd('/') + "/");
    public Uri Resource => new(Issuer, "mcp");
}

public sealed class McpOAuthClient
{
    public string ClientId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? ClientSecret { get; set; }
    public List<string> RedirectUris { get; set; } = [];
}

internal sealed class McpOAuthDbContext(DbContextOptions<McpOAuthDbContext> options) : DbContext(options);
