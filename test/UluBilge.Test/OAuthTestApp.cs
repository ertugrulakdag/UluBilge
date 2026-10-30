using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UluBilge.Api.Controllers;
using UluBilge.Api.Extentions;
using UluBilge.Api.MCP.OAuth;
using UluBilge.Api.MCP.Tools;
using UluBilge.App.Extensions;
using UluBilge.Core.Entities;
using UluBilge.Core.Enums;
using UluBilge.Data.Abstract;

namespace UluBilge.Test;

internal sealed class OAuthTestApp(bool production = false, string? localRedirectUri = null) : IAsyncDisposable
{
    public const string ServerUrl = "https://localhost:7187";
    public const string Resource = ServerUrl + "/mcp";
    public const string ClientId = "ulubilge-chatgpt";
    public const string RedirectUri = "https://chatgpt.com/connector_platform_oauth_redirect";
    public const string LocalClientId = "ulubilge-claude-local";
    public const string LocalRedirectUri = "http://127.0.0.1:3334/oauth/callback";
    public const string Password = "test-password-123";
    private readonly string directory = Path.Combine(Path.GetTempPath(), "UluBilge.OAuthTests." + Guid.NewGuid().ToString("N"));
    private WebApplication app = null!;
    private string Address => production ? "https://mcp.example" : ServerUrl;
    public HttpClient Client { get; private set; } = null!;
    public FakeUsers Users { get; } = new();
    public FakeUnitOfWork Work { get; } = new();
    public TestClock Clock { get; } = new();

    public async Task StartAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = production ? "Production" : "Development",
            ApplicationName = typeof(AuthController).Assembly.GetName().Name
        });
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = "UluBilge", ["Jwt:Audience"] = "UluBilge.Api",
            ["Jwt:SigningKeyBase64"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            ["Jwt:AccessTokenLifetimeMinutes"] = "5",
            ["ConnectionStrings:ReadConnection"] = "Server=unused;Database=unused;Integrated Security=true",
            ["ConnectionStrings:WriteConnection"] = "Server=unused;Database=unused;Integrated Security=true",
            ["OAuth:ServerUrl"] = Address, ["OAuth:DatabasePath"] = Path.Combine(directory, "oauth.db"),
            ["OAuth:Clients:0:ClientId"] = ClientId, ["OAuth:Clients:0:DisplayName"] = "ChatGPT",
            ["OAuth:Clients:0:RedirectUris:0"] = RedirectUri,
            ["OAuth:Clients:1:ClientId"] = "ulubilge-claude", ["OAuth:Clients:1:DisplayName"] = "Claude",
            ["OAuth:Clients:1:RedirectUris:0"] = "https://claude.ai/api/mcp/auth_callback",
            ["OAuth:DevelopmentClients:0:ClientId"] = LocalClientId,
            ["OAuth:DevelopmentClients:0:DisplayName"] = "Claude Desktop (local tests)",
            ["OAuth:DevelopmentClients:0:RedirectUris:0"] = localRedirectUri ?? LocalRedirectUri
        });
        if (production)
        {
            Directory.CreateDirectory(directory);
            var signing = Path.Combine(directory, "signing.pfx");
            var encryption = Path.Combine(directory, "encryption.pfx");
            CreateCertificate(signing, X509KeyUsageFlags.DigitalSignature);
            CreateCertificate(encryption, X509KeyUsageFlags.KeyEncipherment);
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OAuth:SigningCertificatePath"] = signing, ["OAuth:SigningCertificatePassword"] = "test-only-passphrase",
                ["OAuth:EncryptionCertificatePath"] = encryption, ["OAuth:EncryptionCertificatePassword"] = "test-only-passphrase"
            });
        }
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        builder.Services.AddSingleton<TimeProvider>(Clock);
        builder.Services.AddControllers().AddApplicationPart(typeof(AuthController).Assembly);
        builder.Services.AddProblemDetails();
        builder.Services.AddUluBilgeServices(builder.Configuration);
        builder.Services.AddApiAuthentication(builder.Configuration, builder.Environment);
        builder.Services.AddMcpOAuth(builder.Configuration, builder.Environment);
        builder.Services.AddMcpServer().WithHttpTransport().WithToolsFromAssembly(typeof(ProductSalesReportTool).Assembly);
        builder.Services.RemoveAll<IUnitOfWork>();
        builder.Services.RemoveAll<IUserRepository>();
        builder.Services.AddSingleton<IUnitOfWork>(Work);
        builder.Services.AddSingleton<IUserRepository>(Users);
        app = builder.Build();
        app.UseRouting();
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        await app.MapMcpOAuthAsync();
        app.MapMcp("/mcp").RequireAuthorization(McpOAuthOptions.Policy);
        app.MapGet("/test/api", () => "API").RequireAuthorization();
        await app.StartAsync();
        Client = app.GetTestClient();
        Client.BaseAddress = new Uri(Address);
    }

    public async Task<AuthorizationForm> FormAsync(string? clientId = null, string? redirectUri = null,
        string? resource = null, string scopes = "mcp:tools offline_access", string state = "test-state", string method = "S256")
    {
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var parameters = new Dictionary<string, string?>
        {
            ["response_type"] = "code", ["client_id"] = clientId ?? ClientId,
            ["redirect_uri"] = redirectUri ?? RedirectUri, ["resource"] = resource ?? Address + "/mcp",
            ["scope"] = scopes, ["state"] = state,
            ["code_challenge"] = method == "S256" ? Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))) : verifier,
            ["code_challenge_method"] = method
        };
        var response = await Client.GetAsync(QueryHelpers.AddQueryString("/oauth/authorize", parameters));
        var html = await response.Content.ReadAsStringAsync();
        var fields = Regex.Matches(html, "<input type=\"hidden\" name=\"([^\"]+)\" value=\"([^\"]*)\" />")
            .ToDictionary(match => WebUtility.HtmlDecode(match.Groups[1].Value), match => WebUtility.HtmlDecode(match.Groups[2].Value));
        var cookie = response.Headers.TryGetValues("Set-Cookie", out var cookies)
            ? string.Join("; ", cookies.Select(value => value.Split(';')[0])) : string.Empty;
        return new(response, html, fields, cookie, verifier);
    }

    public async Task<HttpResponseMessage> LoginAsync(AuthorizationForm form, string password = Password, bool csrf = true, string decision = "allow")
    {
        var fields = new Dictionary<string, string>(form.Fields)
        {
            ["userName"] = "reporter", ["password"] = password, ["decision"] = decision
        };
        if (!csrf) fields.Remove("__RequestVerificationToken");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/oauth/authorize") { Content = new FormUrlEncodedContent(fields) };
        if (!string.IsNullOrEmpty(form.Cookie)) request.Headers.Add("Cookie", form.Cookie);
        return await Client.SendAsync(request);
    }

    public async Task<(string Code, string Verifier)> CodeAsync()
    {
        var form = await FormAsync();
        if (!form.Response.IsSuccessStatusCode) throw new InvalidOperationException("Authorization form failed: " + form.Html);
        var response = await LoginAsync(form);
        if (response.StatusCode != HttpStatusCode.Redirect)
            throw new InvalidOperationException("Authorization failed: " + await response.Content.ReadAsStringAsync());
        return (QueryHelpers.ParseQuery(response.Headers.Location!.Query)["code"].ToString(), form.Verifier);
    }

    public Task<HttpResponseMessage> ExchangeAsync(string code, string verifier, string? clientId = null, string? redirectUri = null, string? resource = null) =>
        Client.PostAsync("/oauth/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code", ["client_id"] = clientId ?? ClientId,
            ["code"] = code, ["code_verifier"] = verifier, ["redirect_uri"] = redirectUri ?? RedirectUri,
            ["resource"] = resource ?? Address + "/mcp"
        }));

    public Task<HttpResponseMessage> RefreshAsync(string refreshToken, string? clientId = null) =>
        Client.PostAsync("/oauth/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token", ["client_id"] = clientId ?? ClientId,
            ["refresh_token"] = refreshToken, ["resource"] = Address + "/mcp"
        }));

    public async Task<HttpResponseMessage> McpAsync(string? token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = JsonContent.Create(new { jsonrpc = "2.0", id = 1, method = "initialize", @params = new
            {
                protocolVersion = "2025-11-25", clientInfo = new { name = "OAuthTests", version = "1.0" }, capabilities = new { }
            } })
        };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await Client.SendAsync(request);
    }

    public static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    public async Task RestartAsync() { Client.Dispose(); await app.DisposeAsync(); await StartAsync(); }
    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static void CreateCertificate(string path, X509KeyUsageFlags usage)
    {
        if (File.Exists(path)) return;
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=OAuth test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509KeyUsageExtension(usage, critical: true));
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
        File.WriteAllBytes(path, certificate.Export(X509ContentType.Pfx, "test-only-passphrase"));
    }

    public async ValueTask DisposeAsync()
    {
        Client?.Dispose();
        if (app is not null) await app.DisposeAsync();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        var target = Path.GetFullPath(directory);
        var temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (Directory.Exists(target) && target.StartsWith(temp, StringComparison.OrdinalIgnoreCase)
            && Path.GetFileName(target).StartsWith("UluBilge.OAuthTests.", StringComparison.Ordinal)) Directory.Delete(target, recursive: true);
    }
}

internal sealed record AuthorizationForm(HttpResponseMessage Response, string Html, Dictionary<string, string> Fields, string Cookie, string Verifier);
internal sealed class TestClock : TimeProvider
{
    private TimeSpan offset;
    public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow + offset;
    public void Advance(TimeSpan value) => offset += value;
}
internal sealed class FakeUnitOfWork : IUnitOfWork
{
    public int Commands, Queries;
    public DbConnection Connection => throw new InvalidOperationException("Tests must not access Northwind.");
    public DbTransaction? Transaction => null;
    public Task<T> ExecuteQueryAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct = default) { Queries++; return action(ct); }
    public Task<T> ExecuteCommandAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct = default) { Commands++; return action(ct); }
}
internal sealed class FakeUsers : IUserRepository
{
    private static readonly PasswordHasher<User> Hasher = new(Options.Create(new PasswordHasherOptions { IterationCount = 210_000 }));
    private static readonly string Hash = Hasher.HashPassword(new User(), OAuthTestApp.Password);
    public User? User { get; set; } = new() { Id = 7, UserName = "reporter", NormalizedUserName = "REPORTER", IsActive = true, RoleEnum = RoleEnum.Report, PasswordHash = Hash };
    public Task<User?> GetByNormalizedUserNameForUpdateAsync(string name, CancellationToken ct = default) => Task.FromResult(name == User?.NormalizedUserName ? User : null);
    public Task<User?> GetByUserIdAsync(int id, CancellationToken ct = default) => Task.FromResult(User?.Id == id ? User : null);
    public Task RecordFailedLoginAsync(int userId, int count, DateTime? lockoutEndUtc, DateTime now, CancellationToken ct = default)
    { User!.AccessFailedCount = count; User.LockoutEndUtc = lockoutEndUtc; return Task.CompletedTask; }
    public Task RecordSuccessfulLoginAsync(User user, DateTime now, CancellationToken ct = default)
    { user.AccessFailedCount = 0; user.LockoutEndUtc = null; user.LastLoginAtUtc = now; return Task.CompletedTask; }
}
