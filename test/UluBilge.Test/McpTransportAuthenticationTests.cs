using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using ModelContextProtocol.Server;
using UluBilge.Api.Extentions;
using UluBilge.App.Extensions;

namespace UluBilge.Test;

[TestFixture]
public sealed class McpTransportAuthenticationTests
{
    private const string ServerUrl = "https://localhost:7187";
    private WebApplication app = null!;
    private HttpClient client = null!;
    private string token = null!;

    [SetUp]
    public async Task SetUp()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["OAuth:ServerUrl"] = ServerUrl,
            ["ConnectionStrings:ReadConnection"] = "Server=unused;Database=unused;Integrated Security=true",
            ["ConnectionStrings:WriteConnection"] = "Server=unused;Database=unused;Integrated Security=true",
            ["Jwt:Issuer"] = "UluBilge", ["Jwt:Audience"] = "UluBilge.Api", ["Jwt:AccessTokenLifetimeMinutes"] = "5",
            ["Jwt:SigningKeyBase64"] = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32))
        });
        builder.Services.AddUluBilgeServices(builder.Configuration);
        builder.Services.AddApiAuthentication(builder.Configuration, builder.Environment);
        builder.Services.AddMcpOAuth(builder.Configuration, builder.Environment);
        builder.Services.AddMcpServer().WithHttpTransport().WithTools<ProbeTool>();
        app = builder.Build();
        app.UseRouting();
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapMcpOAuth();
        await app.StartAsync();
        client = app.GetTestClient();
        client.BaseAddress = new Uri(ServerUrl);
        var now = DateTime.UtcNow;
        token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = ServerUrl + "/", Audience = ServerUrl + "/mcp",
            IssuedAt = now, NotBefore = now, Expires = now.AddMinutes(5),
            SigningCredentials = app.Services.GetRequiredService<SigningCredentials>(),
            Claims = new Dictionary<string, object> { ["sub"] = "test-user", ["role"] = "Report", ["scope"] = "mcp:tools" }
        });
    }

    [TearDown]
    public async Task TearDown()
    {
        client?.Dispose();
        if (app is not null) await app.DisposeAsync();
    }

    [TestCase("GET")]
    [TestCase("DELETE")]
    public async Task Stateless_methods_with_valid_mcp_token_return_405_without_starting_oauth_again(string method)
    {
        using var initialize = await PostAsync(new
        {
            jsonrpc = "2.0", id = 1, method = "initialize",
            @params = new { protocolVersion = "2025-11-25", capabilities = new { }, clientInfo = new { name = "transport-test", version = "1.0" } }
        });
        Assert.That(initialize.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        using var request = new HttpRequestMessage(new HttpMethod(method), "/mcp");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.ParseAdd("text/event-stream");
        using var response = await client.SendAsync(request);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.MethodNotAllowed));
        Assert.That(response.Headers.WwwAuthenticate, Is.Empty);
        Assert.That(response.Content.Headers.Allow, Does.Contain("POST"));

        using var tools = await PostAsync(new { jsonrpc = "2.0", id = 2, method = "tools/list" });
        Assert.That(tools.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [TestCase("GET")]
    [TestCase("DELETE")]
    public async Task Stateless_methods_still_require_a_valid_mcp_token(string method)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), "/mcp");
        using var response = await client.SendAsync(request);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(response.Headers.WwwAuthenticate.ToString(), Does.Contain("resource_metadata="));
    }

    [TestCase("GET")]
    [TestCase("DELETE")]
    public async Task Stateless_methods_reject_rest_api_tokens(string method)
    {
        var now = DateTime.UtcNow;
        var apiToken = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "UluBilge", Audience = "UluBilge.Api", IssuedAt = now, NotBefore = now, Expires = now.AddMinutes(5),
            SigningCredentials = app.Services.GetRequiredService<SigningCredentials>(),
            Claims = new Dictionary<string, object> { ["sub"] = "test-user", ["role"] = "Report" }
        });
        using var request = new HttpRequestMessage(new HttpMethod(method), "/mcp");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);
        using var response = await client.SendAsync(request);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    private async Task<HttpResponseMessage> PostAsync(object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp") { Content = JsonContent.Create(body) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        return await client.SendAsync(request);
    }

    [McpServerToolType]
    public sealed class ProbeTool
    {
        [McpServerTool]
        public static string Probe() => "ok";
    }
}
