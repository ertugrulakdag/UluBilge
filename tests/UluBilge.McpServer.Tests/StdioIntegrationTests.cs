using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace UluBilge.McpServer.Tests;

public sealed class StdioIntegrationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("2025-11-25")]
    public async Task RealStdioClientDiscoversToolAndDisplaysAuthenticatedApiResults(string? protocolVersion)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ct = timeout.Token;
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var api = builder.Build();
        var loginCount = 0;
        var regionCount = 0;
        api.MapPost("/v1/Auth/login", async (HttpRequest request) =>
        {
            var login = await request.ReadFromJsonAsync<LoginBody>(ct);
            if (login?.UserName != ApiTestContext.UserName || login.Password != ApiTestContext.Password)
                return Results.Unauthorized();
            Interlocked.Increment(ref loginCount);
            return Results.Json(new
            {
                accessToken = "integration-access-token",
                tokenType = "Bearer",
                expiresIn = 300,
                expiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(5),
                refreshToken = ApiTestContext.RefreshA,
                refreshTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(7)
            });
        });
        api.MapGet("/v1/Region/GetRegions", (HttpRequest request) =>
        {
            if (request.Headers.Authorization != "Bearer integration-access-token") return Results.Unauthorized();
            Interlocked.Increment(ref regionCount);
            return Results.Json(new
            {
                data = new
                {
                    data = new[] { new { regionId = 1, regionDescription = "Eastern" } },
                    total = 1,
                    pageNumber = int.Parse(request.Query["pageNumber"].ToString()),
                    pageSize = int.Parse(request.Query["pageSize"].ToString())
                }
            });
        });
        await api.StartAsync(ct);

        var errors = new ConcurrentQueue<string>();
        var workingDirectory = Directory.CreateTempSubdirectory("ulubilge-mcp-stdio-");
        try
        {
            var environment = StdioClientTransportOptions.GetDefaultEnvironmentVariables();
            environment["DOTNET_ENVIRONMENT"] = "Development";
            environment["UluBilgeApi__BaseUrl"] = api.Urls.Single();
            environment["UluBilgeApi__UserName"] = ApiTestContext.UserName;
            environment["UluBilgeApi__Password"] = ApiTestContext.Password;
            environment["Logging__LogLevel__Default"] = "Information";
            var transport = new StdioClientTransport(new StdioClientTransportOptions
            {
                Command = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
                Arguments = [Path.GetFullPath(typeof(StdioIntegrationTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                    .Single(attribute => attribute.Key == "McpServerAssemblyPath").Value!)],
                WorkingDirectory = workingDirectory.FullName,
                InheritEnvironmentVariables = false,
                EnvironmentVariables = environment,
                StandardErrorLines = line => errors.Enqueue(line),
                ShutdownTimeout = TimeSpan.FromSeconds(3)
            });
            await using (var client = await McpClient.CreateAsync(transport, new McpClientOptions
            {
                ProtocolVersion = protocolVersion,
                InitializationTimeout = TimeSpan.FromSeconds(15)
            }, cancellationToken: ct))
            {
                var tool = Assert.Single(await client.ListToolsAsync(cancellationToken: ct));
                Assert.Equal("get_regions", tool.Name);
                Assert.NotNull(tool.ProtocolTool.OutputSchema);
                Assert.True(tool.ProtocolTool.OutputSchema.Value.GetProperty("properties").TryGetProperty("items", out _));
                Assert.Equal(0, Volatile.Read(ref loginCount));

                var result = await client.CallToolAsync("get_regions", cancellationToken: ct);
                Assert.False(result.IsError == true);
                Assert.Contains("Eastern", Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
                Assert.Equal(1, result.StructuredContent!.Value.GetProperty("items")[0].GetProperty("regionId").GetInt32());

                var paging = new Dictionary<string, object?> { ["pageNumber"] = 2, ["pageSize"] = 10 };
                var next = await client.CallToolAsync("get_regions", paging, cancellationToken: ct);
                Assert.False(next.IsError == true);
                Assert.Equal(2, next.StructuredContent!.Value.GetProperty("pageNumber").GetInt32());
                Assert.Equal(10, next.StructuredContent.Value.GetProperty("pageSize").GetInt32());
                Assert.Equal(1, Volatile.Read(ref loginCount));
                Assert.Equal(2, Volatile.Read(ref regionCount));

                var invalid = await client.CallToolAsync("get_regions", new Dictionary<string, object?> { ["pageSize"] = 101 }, cancellationToken: ct);
                Assert.True(invalid.IsError);
                Assert.Equal(2, Volatile.Read(ref regionCount));
            }

            Assert.NotEmpty(errors);
            var logs = string.Join('\n', errors);
            Assert.DoesNotContain(ApiTestContext.Password, logs);
            Assert.DoesNotContain("integration-access-token", logs);
            Assert.DoesNotContain(ApiTestContext.RefreshA, logs);
        }
        finally
        {
            await api.StopAsync(CancellationToken.None);
            workingDirectory.Delete();
        }
    }

    private sealed record LoginBody(string UserName, string Password);
}
