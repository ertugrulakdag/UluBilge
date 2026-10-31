using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using UluBilge.Api.Controllers;
using UluBilge.Api.Extentions;
using UluBilge.App.Authentication;
using UluBilge.Core.Entities;

namespace UluBilge.Test;

// These tests own a uniquely named database; the application's configured database is never migrated or changed.
[TestFixture, NonParallelizable]
public sealed class McpOAuthRefreshTests
{
    private const string ServerUrl = "https://localhost:7187";
    private const string Resource = ServerUrl + "/mcp";
    private const string RedirectUri = "https://client.example.test/callback";
    private const string FullScope = "mcp:tools offline_access";
    private readonly string databaseName = "UluBilgeOAuthTests_" + Guid.NewGuid().ToString("N");
    private string repositoryRoot = null!;
    private string connectionString = null!;
    private string masterConnectionString = null!;
    private bool databaseCreated;
    private TestHost host = null!;
    private string clientId = null!;
    private string otherClientId = null!;

    [OneTimeSetUp]
    public async Task CreateDatabase()
    {
        repositoryRoot = FindRepositoryRoot();
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(repositoryRoot, "src", "UluBilge.Api", "appsettings.json"))
            .Build();
        var configuredConnection = Environment.GetEnvironmentVariable("ULUBILGE_TEST_CONNECTION")
            ?? configuration.GetConnectionString("WriteConnection")
            ?? throw new InvalidOperationException("ULUBILGE_TEST_CONNECTION veya WriteConnection gereklidir.");
        var settings = new SqlConnectionStringBuilder(configuredConnection) { Pooling = false };
        settings.InitialCatalog = "master";
        masterConnectionString = settings.ConnectionString;
        settings.InitialCatalog = databaseName;
        connectionString = settings.ConnectionString;

        ValidateDatabaseName();
        await using (var master = new SqlConnection(masterConnectionString))
        {
            await master.OpenAsync();
            await master.ExecuteAsync($"CREATE DATABASE [{databaseName}];");
            databaseCreated = true;
        }

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        // The OAuth migration must also be safe to apply again.
        foreach (var migration in new[] { "20261029T1955_UsersAndUserTokens.sql", "20261030T1200_UserTokensOAuth.sql", "20261030T1200_UserTokensOAuth.sql",
            "20261031T0900_OAuthClients.sql", "20261031T0900_OAuthClients.sql" })
        {
            var script = await File.ReadAllTextAsync(Path.Combine(repositoryRoot, "src", "UluBilge.Migration", "Migrations", migration));
            foreach (var batch in Regex.Split(script, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
                if (!string.IsNullOrWhiteSpace(batch))
                    await connection.ExecuteAsync(batch);
        }
    }

    [SetUp]
    public async Task SetUp()
    {
        await ExecuteAsync("""
            UPDATE dbo.UserTokens SET ReplacedByTokenId = NULL;
            DELETE FROM dbo.UserTokens;
            DELETE FROM dbo.OAuthClientRedirectUris;
            DELETE FROM dbo.OAuthClients;
            UPDATE dbo.Users SET IsActive = 1, RoleEnum = 1, SecurityStamp = NEWID(),
                AccessFailedCount = 0, LockoutEndUtc = NULL;
            """);
        host = await CreateHostAsync();
        clientId = (await RegisterAsync(host.Client, "Integration test client", RedirectUri)).GetProperty("client_id").GetString()!;
        otherClientId = (await RegisterAsync(host.Client, "Other integration test client", RedirectUri)).GetProperty("client_id").GetString()!;
    }

    [TearDown]
    public async Task TearDown()
    {
        if (host is not null)
            await host.DisposeAsync();
    }

    [OneTimeTearDown]
    public async Task DropDatabase()
    {
        if (!databaseCreated)
            return;

        ValidateDatabaseName();
        await using var master = new SqlConnection(masterConnectionString);
        await master.OpenAsync();
        await master.ExecuteAsync($"ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{databaseName}];");
    }

    [Test]
    public async Task DynamicRegistration_PersistsClientAndRedirects_AndAuthorizesAfterHostRestart()
    {
        const string displayName = "Persisted <MCP> client";
        const string secondRedirect = "https://client.example.test/second-callback";
        var registeredAt = DateTime.UtcNow;
        var registration = await RegisterAsync(host.Client, displayName, RedirectUri, secondRedirect);
        var registeredId = registration.GetProperty("client_id").GetString()!;
        Assert.Multiple(() =>
        {
            Assert.That(registeredId, Is.Not.Empty.And.Not.EqualTo(clientId));
            Assert.That(registration.GetProperty("client_name").GetString(), Is.EqualTo(displayName));
            Assert.That(registration.GetProperty("redirect_uris").EnumerateArray().Select(uri => uri.GetString()),
                Is.EquivalentTo(new[] { RedirectUri, secondRedirect }));
            Assert.That(registration.GetProperty("token_endpoint_auth_method").GetString(), Is.EqualTo("none"));
        });

        await using (var connection = new SqlConnection(connectionString))
        {
            var stored = await connection.QuerySingleAsync<PersistedClient>(
                "SELECT ClientId, DisplayName, IsActive, CreatedAtUtc FROM dbo.OAuthClients WHERE ClientId = @ClientId;",
                new { ClientId = registeredId });
            var redirects = await connection.QueryAsync<string>(
                "SELECT RedirectUri FROM dbo.OAuthClientRedirectUris WHERE ClientId = @ClientId;", new { ClientId = registeredId });
            Assert.Multiple(() =>
            {
                Assert.That(stored.ClientId, Is.EqualTo(registeredId));
                Assert.That(stored.DisplayName, Is.EqualTo(displayName));
                Assert.That(stored.IsActive, Is.True);
                Assert.That(stored.CreatedAtUtc, Is.InRange(registeredAt.AddSeconds(-1), DateTime.UtcNow.AddSeconds(1)));
                Assert.That(redirects, Is.EquivalentTo(new[] { RedirectUri, secondRedirect }));
            });
        }

        await host.DisposeAsync();
        host = await CreateHostAsync();
        using var consent = await GetAuthorizationAsync(host.Client, registeredId, secondRedirect);
        Assert.That(consent.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(await consent.Content.ReadAsStringAsync(), Does.Contain(WebUtility.HtmlEncode(displayName)));
        var tokens = await AuthorizeAsync(host.Client, registeredClientId: registeredId, redirectUri: secondRedirect);
        using var refreshed = await RefreshAsync(host.Client, tokens.GetProperty("refresh_token").GetString(), clientId: registeredId);
        await ReadSuccessAsync(refreshed);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task UnknownOrInactiveClient_IsRejectedWithoutConsumingRefresh(bool inactive)
    {
        var initial = await AuthorizeAsync(host.Client);
        var rejectedId = inactive ? clientId : "unknown-client";
        if (inactive)
            await ExecuteAsync("UPDATE dbo.OAuthClients SET IsActive = 0 WHERE ClientId = @ClientId;", new { ClientId = clientId });

        using var authorize = await GetAuthorizationAsync(host.Client, rejectedId, RedirectUri);
        await AssertOAuthErrorAsync(authorize, "invalid_request");
        using var refresh = await RefreshAsync(host.Client, initial.GetProperty("refresh_token").GetString(), clientId: rejectedId);
        await AssertOAuthErrorAsync(refresh, "invalid_client");
        Assert.That((await ReadTokensAsync()).Single().ConsumedAtUtc, Is.Null);
    }

    [Test]
    public async Task Authorization_RequiresExactClientIdAndOwnedCaseSensitiveRedirect()
    {
        const string foreignRedirect = "https://other.example.test/callback";
        await RegisterAsync(host.Client, "Other callback owner", foreignRedirect);
        var caseChangedId = ChangeFirstLetterCase(clientId);
        using var wrongCaseId = await GetAuthorizationAsync(host.Client, caseChangedId, RedirectUri);
        await AssertOAuthErrorAsync(wrongCaseId, "invalid_request");
        using var paddedId = await GetAuthorizationAsync(host.Client, clientId + " ", RedirectUri);
        await AssertOAuthErrorAsync(paddedId, "invalid_request");
        using var foreignCallback = await GetAuthorizationAsync(host.Client, clientId, foreignRedirect);
        await AssertOAuthErrorAsync(foreignCallback, "invalid_request");
        using var wrongCaseCallback = await GetAuthorizationAsync(host.Client, clientId, "https://client.example.test/Callback");
        await AssertOAuthErrorAsync(wrongCaseCallback, "invalid_request");
    }

    [Test]
    public async Task ProductionRegistration_AllowsDesktopLoopbackPortChanges_AndRejectsRemoteHttp()
    {
        const string productionUrl = "https://oauth.example.test";
        const string productionResource = productionUrl + "/mcp";
        await using var production = await CreateHostAsync(Environments.Production, productionUrl);
        var registration = await RegisterAsync(production.Client, "Desktop client", "http://127.0.0.1:18590/oauth/callback");
        var registeredId = registration.GetProperty("client_id").GetString()!;
        var tokens = await AuthorizeAsync(production.Client, registeredClientId: registeredId,
            redirectUri: "http://127.0.0.1:18591/oauth/callback", resource: productionResource);
        Assert.That(tokens.GetProperty("access_token").GetString(), Is.Not.Empty);

        using var wrongPath = await GetAuthorizationAsync(production.Client, registeredId,
            "http://127.0.0.1:18591/other-callback", productionResource);
        await AssertOAuthErrorAsync(wrongPath, "invalid_request");
        using var wrongHost = await GetAuthorizationAsync(production.Client, registeredId,
            "http://127.0.0.2:18591/oauth/callback", productionResource);
        await AssertOAuthErrorAsync(wrongHost, "invalid_request");
        using var wrongQuery = await GetAuthorizationAsync(production.Client, registeredId,
            "http://127.0.0.1:18591/oauth/callback?unexpected=value", productionResource);
        await AssertOAuthErrorAsync(wrongQuery, "invalid_request");
        using var remoteHttp = await production.Client.PostAsJsonAsync("/oauth/register", new
        {
            client_name = "Unencrypted remote callback", redirect_uris = new[] { "http://client.example.test/callback" }
        });
        await AssertOAuthErrorAsync(remoteHttp, "invalid_redirect_uri");
    }

    [Test]
    public async Task ConcurrentRegistration_EnforcesSharedActiveClientLimit_WithoutPartialRecords()
    {
        await ExecuteAsync("""
            ;WITH SeededClients AS
            (
                SELECT 1 AS Number
                UNION ALL
                SELECT Number + 1 FROM SeededClients WHERE Number < 997
            )
            INSERT INTO dbo.OAuthClients (ClientId, DisplayName, IsActive)
            SELECT CONCAT(N'seeded-', Number), N'Seeded client', 1 FROM SeededClients
            OPTION (MAXRECURSION 0);

            INSERT INTO dbo.OAuthClientRedirectUris (ClientId, RedirectUri)
            SELECT ClientId, N'https://seeded.example.test/callback'
            FROM dbo.OAuthClients WHERE ClientId LIKE N'seeded-%';
            """);
        await using var secondHost = await CreateHostAsync();
        var responses = await Task.WhenAll(
            host.Client.PostAsJsonAsync("/oauth/register", new
            {
                client_name = "Concurrent registration A", redirect_uris = new[] { "https://concurrent.example.test/callback-a" }
            }),
            secondHost.Client.PostAsJsonAsync("/oauth/register", new
            {
                client_name = "Concurrent registration B", redirect_uris = new[] { "https://concurrent.example.test/callback-b" }
            }));
        try
        {
            Assert.Multiple(() =>
            {
                Assert.That(responses.Count(response => response.StatusCode == HttpStatusCode.Created), Is.EqualTo(1));
                Assert.That(responses.Count(response => response.StatusCode == HttpStatusCode.ServiceUnavailable), Is.EqualTo(1));
            });
            using var rejected = JsonDocument.Parse(await responses.Single(response => response.StatusCode == HttpStatusCode.ServiceUnavailable).Content.ReadAsStringAsync());
            Assert.That(rejected.RootElement.GetProperty("error").GetString(), Is.EqualTo("temporarily_unavailable"));
            Assert.That(await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.OAuthClients WHERE IsActive = 1;"), Is.EqualTo(1000));
            Assert.That(await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.OAuthClients WHERE DisplayName LIKE N'Concurrent registration %';"), Is.EqualTo(1));
            Assert.That(await ScalarAsync<int>("""
                SELECT COUNT(*) FROM dbo.OAuthClientRedirectUris
                WHERE RedirectUri IN (N'https://concurrent.example.test/callback-a', N'https://concurrent.example.test/callback-b');
                """), Is.EqualTo(1));
            Assert.That(await ScalarAsync<int>("""
                SELECT COUNT(*) FROM dbo.OAuthClients AS client
                WHERE NOT EXISTS (SELECT 1 FROM dbo.OAuthClientRedirectUris AS redirect WHERE redirect.ClientId = client.ClientId);
                """), Is.Zero);

            await ExecuteAsync("UPDATE dbo.OAuthClients SET IsActive = 0 WHERE ClientId = @ClientId;", new { ClientId = clientId });
            await RegisterAsync(secondHost.Client, "Registration after deactivation", "https://concurrent.example.test/replacement");
            var activeClients = await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.OAuthClients WHERE IsActive = 1;");
            var allClients = await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.OAuthClients;");
            Assert.Multiple(() =>
            {
                Assert.That(activeClients, Is.EqualTo(1000));
                Assert.That(allClients, Is.EqualTo(1001));
            });
        }
        finally
        {
            foreach (var response in responses)
                response.Dispose();
        }
    }

    [Test]
    public async Task AuthorizationCode_PersistsHashedBoundToken_AndRotatesAfterHostRestart()
    {
        var initial = await AuthorizeAsync(host.Client);
        var token = new JsonWebToken(initial.GetProperty("access_token").GetString()!);
        Assert.Multiple(() =>
        {
            Assert.That(token.Issuer, Is.EqualTo(ServerUrl + "/"));
            Assert.That(token.Audiences, Does.Contain(Resource));
            Assert.That(token.GetClaim("scope").Value, Is.EqualTo(FullScope));
        });
        await AssertMcpAccessAsync(host.Client, initial.GetProperty("access_token").GetString()!);

        var refresh = initial.GetProperty("refresh_token").GetString()!;
        var stored = (await ReadTokensAsync()).Single();
        Assert.Multiple(() =>
        {
            Assert.That(stored.TokenHash, Is.EqualTo(SHA256.HashData(Encoding.UTF8.GetBytes(refresh))));
            Assert.That(stored.OAuthClientId, Is.EqualTo(clientId));
            Assert.That(stored.OAuthResource, Is.EqualTo(Resource));
            Assert.That(stored.OAuthScope, Is.EqualTo(FullScope));
            Assert.That(stored.SecurityStamp, Is.Not.Null);
        });
        Assert.That(stored.SecurityStamp, Is.EqualTo(await ScalarAsync<Guid>("SELECT SecurityStamp FROM dbo.Users WHERE Id = 1;")));

        // A fresh service provider has no OAuth memory from the issuing process.
        await host.DisposeAsync();
        host = await CreateHostAsync();
        using var rotated = await RefreshAsync(host.Client, refresh);
        var replacement = await ReadSuccessAsync(rotated);
        await AssertMcpAccessAsync(host.Client, replacement.GetProperty("access_token").GetString()!);
        var replacementRefresh = replacement.GetProperty("refresh_token").GetString()!;
        Assert.That(replacementRefresh, Is.Not.EqualTo(refresh));
        var rows = await ReadTokensAsync();
        Assert.Multiple(() =>
        {
            Assert.That(rows, Has.Count.EqualTo(2));
            Assert.That(rows.Select(row => row.FamilyId).Distinct().Count(), Is.EqualTo(1));
            Assert.That(rows[0].ConsumedAtUtc, Is.Not.Null);
            Assert.That(rows[0].ReplacedByTokenId, Is.EqualTo(rows[1].Id));
            Assert.That(rows[1].ExpiresAtUtc, Is.EqualTo(stored.ExpiresAtUtc));
        });

        using var replay = await RefreshAsync(host.Client, refresh);
        await AssertOAuthErrorAsync(replay, "invalid_grant");
        using var revokedChild = await RefreshAsync(host.Client, replacementRefresh);
        await AssertOAuthErrorAsync(revokedChild, "invalid_grant");
        Assert.That((await ReadTokensAsync()).All(row => row.RevokedAtUtc.HasValue), Is.True);
    }

    [Test]
    public async Task ClientAndRestBindings_RejectCrossUseWithoutConsumingValidToken()
    {
        var oauth = await AuthorizeAsync(host.Client);
        var refresh = oauth.GetProperty("refresh_token").GetString()!;
        using var wrongClient = await RefreshAsync(host.Client, refresh, clientId: otherClientId);
        await AssertOAuthErrorAsync(wrongClient, "invalid_grant");
        using var oauthAtRest = await host.Client.PostAsJsonAsync("/v1/Auth/RefreshToken", new { refreshToken = refresh });
        Assert.That(oauthAtRest.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That((await ReadTokensAsync()).Single().ConsumedAtUtc, Is.Null);
        using var validOAuth = await RefreshAsync(host.Client, refresh);
        await ReadSuccessAsync(validOAuth);

        using var login = await host.Client.PostAsJsonAsync("/v1/Auth/Login", new { userName = "admin", password = "admin" });
        var rest = (await ReadSuccessAsync(login)).GetProperty("data");
        var restRefresh = rest.GetProperty("refreshToken").GetString()!;
        using var restAtOAuth = await RefreshAsync(host.Client, restRefresh);
        await AssertOAuthErrorAsync(restAtOAuth, "invalid_grant");
        using var validRest = await host.Client.PostAsJsonAsync("/v1/Auth/RefreshToken", new { refreshToken = restRefresh });
        Assert.That(validRest.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task ResourceBinding_ValidatesRequestAndStoredResource_WithoutConsumingToken()
    {
        const string otherResource = "https://other.example.test/mcp";
        var initial = await AuthorizeAsync(host.Client);
        var refresh = initial.GetProperty("refresh_token").GetString()!;
        using var wrongRequestResource = await RefreshAsync(host.Client, refresh, resource: otherResource);
        await AssertOAuthErrorAsync(wrongRequestResource, "invalid_target");
        var stored = (await ReadTokensAsync()).Single();
        Assert.Multiple(() =>
        {
            Assert.That(stored.ConsumedAtUtc, Is.Null);
            Assert.That(stored.RevokedAtUtc, Is.Null);
        });

        await ExecuteAsync("UPDATE dbo.UserTokens SET OAuthResource = @Resource WHERE Id = @Id;", new { Resource = otherResource, stored.Id });
        using var wrongStoredResource = await RefreshAsync(host.Client, refresh);
        await AssertOAuthErrorAsync(wrongStoredResource, "invalid_grant");
        stored = (await ReadTokensAsync()).Single();
        Assert.Multiple(() =>
        {
            Assert.That(stored.ConsumedAtUtc, Is.Null);
            Assert.That(stored.RevokedAtUtc, Is.Null);
            Assert.That(stored.ReplacedByTokenId, Is.Null);
        });

        await ExecuteAsync("UPDATE dbo.UserTokens SET OAuthResource = @Resource WHERE Id = @Id;", new { Resource, stored.Id });
        using var validResource = await RefreshAsync(host.Client, refresh);
        await ReadSuccessAsync(validResource);
    }

    [Test]
    public async Task ExpiredRefresh_UsesDatabaseExpiry_AndDoesNotIssueReplacement()
    {
        var initial = await AuthorizeAsync(host.Client);
        await ExecuteAsync("UPDATE dbo.UserTokens SET ExpiresAtUtc = DATEADD(minute, -1, SYSUTCDATETIME());");
        using var expired = await RefreshAsync(host.Client, initial.GetProperty("refresh_token").GetString()!);
        await AssertOAuthErrorAsync(expired, "invalid_grant");
        var rows = await ReadTokensAsync();
        Assert.That(rows, Has.Count.EqualTo(1));
        var stored = rows.Single();
        Assert.Multiple(() =>
        {
            Assert.That(stored.ExpiresAtUtc, Is.LessThan(DateTime.UtcNow));
            Assert.That(stored.ConsumedAtUtc, Is.Null);
            Assert.That(stored.ReplacedByTokenId, Is.Null);
        });
    }

    [Test]
    public async Task ScopeExpansion_IsRejectedWithoutConsumingToken()
    {
        var initial = await AuthorizeAsync(host.Client);
        var refresh = initial.GetProperty("refresh_token").GetString()!;
        using var expanded = await RefreshAsync(host.Client, refresh, scope: FullScope + " unsupported:scope");
        await AssertOAuthErrorAsync(expanded, "invalid_scope");
        var stored = (await ReadTokensAsync()).Single();
        Assert.Multiple(() =>
        {
            Assert.That(stored.ConsumedAtUtc, Is.Null);
            Assert.That(stored.RevokedAtUtc, Is.Null);
        });
        using var unchanged = await RefreshAsync(host.Client, refresh);
        await ReadSuccessAsync(unchanged);
    }

    [Test]
    public async Task RemovingOfflineScope_ReturnsOnlyAccessToken_AndRevokesRefreshFamily()
    {
        var initial = await AuthorizeAsync(host.Client);
        var refresh = initial.GetProperty("refresh_token").GetString()!;
        using var response = await RefreshAsync(host.Client, refresh, scope: "mcp:tools");
        var narrowed = await ReadSuccessAsync(response);
        Assert.Multiple(() =>
        {
            Assert.That(narrowed.GetProperty("scope").GetString(), Is.EqualTo("mcp:tools"));
            Assert.That(narrowed.TryGetProperty("refresh_token", out _), Is.False);
        });
        await AssertMcpAccessAsync(host.Client, narrowed.GetProperty("access_token").GetString()!);
        var rows = await ReadTokensAsync();
        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows.Single().RevokedAtUtc, Is.Not.Null);
        using var replay = await RefreshAsync(host.Client, refresh);
        await AssertOAuthErrorAsync(replay, "invalid_grant");
    }

    [Test]
    public async Task ConcurrentRefresh_UsesOneTokenOnce_AndReplayRevokesTheWinner()
    {
        var initial = await AuthorizeAsync(host.Client);
        var refresh = initial.GetProperty("refresh_token").GetString()!;
        await using var secondHost = await CreateHostAsync();
        var responses = await Task.WhenAll(RefreshAsync(host.Client, refresh), RefreshAsync(secondHost.Client, refresh));
        try
        {
            Assert.Multiple(() =>
            {
                Assert.That(responses.Count(response => response.StatusCode == HttpStatusCode.OK), Is.EqualTo(1));
                Assert.That(responses.Count(response => response.StatusCode == HttpStatusCode.BadRequest), Is.EqualTo(1));
            });
            await AssertOAuthErrorAsync(responses.Single(response => response.StatusCode == HttpStatusCode.BadRequest), "invalid_grant");
            var winner = await ReadSuccessAsync(responses.Single(response => response.IsSuccessStatusCode));
            using var revoked = await RefreshAsync(host.Client, winner.GetProperty("refresh_token").GetString()!);
            await AssertOAuthErrorAsync(revoked, "invalid_grant");
            Assert.That((await ReadTokensAsync()).All(row => row.RevokedAtUtc.HasValue), Is.True);
        }
        finally
        {
            foreach (var response in responses)
                response.Dispose();
        }
    }

    [TestCase("SecurityStamp = NEWID()")]
    [TestCase("IsActive = 0")]
    [TestCase("RoleEnum = 4")]
    [TestCase("LockoutEndUtc = DATEADD(minute, 5, SYSUTCDATETIME())")]
    public async Task UserSecurityChanges_InvalidateRefreshFamily(string update)
    {
        var initial = await AuthorizeAsync(host.Client);
        await ExecuteAsync("UPDATE dbo.Users SET " + update + " WHERE Id = 1;");
        using var response = await RefreshAsync(host.Client, initial.GetProperty("refresh_token").GetString()!);
        await AssertOAuthErrorAsync(response, "invalid_grant");
        Assert.That((await ReadTokensAsync()).All(row => row.RevokedAtUtc.HasValue), Is.True);
    }

    [Test]
    public async Task AuthorizationWithoutOfflineScope_DoesNotCreateRefreshTokenOrOrphanRestSession()
    {
        var tokens = await AuthorizeAsync(host.Client, "mcp:tools");
        Assert.That(tokens.TryGetProperty("refresh_token", out _), Is.False);
        Assert.That(await ReadTokensAsync(), Is.Empty);
        await AssertMcpAccessAsync(host.Client, tokens.GetProperty("access_token").GetString()!);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("malformed-token")]
    public async Task MissingOrMalformedRefresh_ReturnsOAuthInvalidGrant(string? refresh)
    {
        using var response = await RefreshAsync(host.Client, refresh);
        await AssertOAuthErrorAsync(response, "invalid_grant");
    }

    private async Task<TestHost> CreateHostAsync(string environmentName = "Development", string serverUrl = ServerUrl)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = environmentName,
            ContentRootPath = repositoryRoot,
            ApplicationName = typeof(AuthController).Assembly.FullName
        });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:ReadConnection"] = connectionString,
            ["ConnectionStrings:WriteConnection"] = connectionString,
            ["Jwt:Issuer"] = "UluBilgeOAuthTests",
            ["Jwt:Audience"] = "UluBilgeOAuthTests.Rest",
            ["Jwt:SigningKeyBase64"] = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(databaseName))),
            ["Jwt:AccessTokenLifetimeMinutes"] = "5",
            ["Jwt:RefreshTokenLifetimeDays"] = "7",
            ["OAuth:ServerUrl"] = serverUrl
        });
        builder.Services.AddControllers().AddApplicationPart(typeof(AuthController).Assembly);
        builder.Services.AddUluBilgeServices(builder.Configuration);
        builder.Services.AddApiAuthentication(builder.Configuration, builder.Environment);
        builder.Services.AddMcpOAuth(builder.Configuration, builder.Environment);
        builder.Services.AddMcpServer().WithHttpTransport();
        var application = builder.Build();
        application.UseRouting();
        application.UseRateLimiter();
        application.UseAuthentication();
        application.UseAuthorization();
        application.MapControllers();
        application.MapMcpOAuth();
        await application.StartAsync();
        var client = application.GetTestClient();
        client.BaseAddress = new Uri(serverUrl);
        return new TestHost(application, client);
    }

    private async Task<JsonElement> AuthorizeAsync(HttpClient client, string scope = FullScope, string? registeredClientId = null,
        string redirectUri = RedirectUri, string resource = Resource)
    {
        var authorizationClientId = registeredClientId ?? clientId;
        var verifier = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        var parameters = new Dictionary<string, string?>
        {
            ["client_id"] = authorizationClientId, ["redirect_uri"] = redirectUri, ["response_type"] = "code",
            ["resource"] = resource, ["scope"] = scope, ["state"] = Guid.NewGuid().ToString("N"),
            ["code_challenge"] = Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))),
            ["code_challenge_method"] = "S256"
        };
        using var form = await client.GetAsync(QueryHelpers.AddQueryString("/oauth/authorize", parameters));
        Assert.That(form.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var html = await form.Content.ReadAsStringAsync();
        var fields = Regex.Matches(html, "<input type=\"hidden\" name=\"([^\"]+)\" value=\"([^\"]*)\" />")
            .ToDictionary(match => WebUtility.HtmlDecode(match.Groups[1].Value), match => WebUtility.HtmlDecode(match.Groups[2].Value));
        Assert.That(fields.ContainsKey("__RequestVerificationToken"), Is.True);
        fields["userName"] = "admin";
        fields["password"] = "admin";
        fields["decision"] = "allow";
        using var request = new HttpRequestMessage(HttpMethod.Post, "/oauth/login") { Content = new FormUrlEncodedContent(fields) };
        request.Headers.Add("Cookie", string.Join("; ", form.Headers.GetValues("Set-Cookie").Select(cookie => cookie.Split(';')[0])));
        using var consent = await client.SendAsync(request);
        Assert.That(consent.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
        var redirect = consent.Headers.Location!;
        var query = QueryHelpers.ParseQuery(redirect.Query);
        Assert.That(query.ContainsKey("code"), Is.True, "OAuth consent must return an authorization code.");
        Assert.That(query["state"].ToString(), Is.EqualTo(parameters["state"]));
        using var exchanged = await client.PostAsync("/oauth/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code", ["client_id"] = authorizationClientId, ["resource"] = resource,
            ["code"] = query["code"].ToString(), ["redirect_uri"] = redirectUri, ["code_verifier"] = verifier
        }));
        return await ReadSuccessAsync(exchanged);
    }

    private static async Task<JsonElement> RegisterAsync(HttpClient client, string displayName, params string[] redirects)
    {
        using var response = await client.PostAsJsonAsync("/oauth/register", new
        {
            client_name = displayName, redirect_uris = redirects,
            grant_types = new[] { "authorization_code", "refresh_token" }, response_types = new[] { "code" }, token_endpoint_auth_method = "none"
        });
        var text = await response.Content.ReadAsStringAsync();
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created), text);
        using var body = JsonDocument.Parse(text);
        return body.RootElement.Clone();
    }

    private static Task<HttpResponseMessage> GetAuthorizationAsync(HttpClient client, string registeredClientId, string redirectUri, string resource = Resource)
        => client.GetAsync(QueryHelpers.AddQueryString("/oauth/authorize", new Dictionary<string, string?>
        {
            ["client_id"] = registeredClientId, ["redirect_uri"] = redirectUri, ["response_type"] = "code",
            ["resource"] = resource, ["scope"] = FullScope,
            ["code_challenge"] = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32)), ["code_challenge_method"] = "S256"
        }));

    private static string ChangeFirstLetterCase(string value)
    {
        var index = value.IndexOfAny("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ".ToCharArray());
        Assert.That(index, Is.GreaterThanOrEqualTo(0));
        var changed = value.ToCharArray();
        changed[index] = char.IsUpper(changed[index]) ? char.ToLowerInvariant(changed[index]) : char.ToUpperInvariant(changed[index]);
        return new string(changed);
    }

    private Task<HttpResponseMessage> RefreshAsync(HttpClient client, string? refresh, string? scope = null, string? clientId = null, string resource = Resource)
    {
        var fields = new Dictionary<string, string> { ["grant_type"] = "refresh_token", ["client_id"] = clientId ?? this.clientId, ["resource"] = resource };
        if (refresh is not null) fields["refresh_token"] = refresh;
        if (scope is not null) fields["scope"] = scope;
        return client.PostAsync("/oauth/token", new FormUrlEncodedContent(fields));
    }

    private static async Task AssertMcpAccessAsync(HttpClient client, string accessToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = JsonContent.Create(new
            {
                jsonrpc = "2.0", id = 1, method = "initialize",
                @params = new { protocolVersion = "2025-06-18", capabilities = new { }, clientInfo = new { name = "OAuth integration tests", version = "1.0" } }
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        using var response = await client.SendAsync(request);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(await response.Content.ReadAsStringAsync(), Does.Contain("protocolVersion"));
    }

    private static async Task<JsonElement> ReadSuccessAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), text);
        using var body = JsonDocument.Parse(text);
        return body.RootElement.Clone();
    }

    private static async Task AssertOAuthErrorAsync(HttpResponseMessage response, string error)
    {
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.That(body.RootElement.GetProperty("error").GetString(), Is.EqualTo(error));
    }

    private async Task ExecuteAsync(string sql, object? parameters = null)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        // OAuthClientRedirectUris has an indexed persisted computed column.
        await connection.ExecuteAsync("""
            SET ANSI_NULLS ON;
            SET ANSI_PADDING ON;
            SET ANSI_WARNINGS ON;
            SET ARITHABORT ON;
            SET CONCAT_NULL_YIELDS_NULL ON;
            SET QUOTED_IDENTIFIER ON;
            SET NUMERIC_ROUNDABORT OFF;
            """ + sql, parameters);
    }

    private async Task<T> ScalarAsync<T>(string sql, object? parameters = null)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        return await connection.QuerySingleAsync<T>(sql, parameters);
    }

    private async Task<List<UserToken>> ReadTokensAsync()
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        return (await connection.QueryAsync<UserToken>("SELECT * FROM dbo.UserTokens ORDER BY Id;")).AsList();
    }

    private void ValidateDatabaseName()
    {
        if (!Regex.IsMatch(databaseName, @"\AUluBilgeOAuthTests_[0-9a-f]{32}\z", RegexOptions.CultureInvariant))
            throw new InvalidOperationException("Yalnizca bu testin olusturdugu gecici veritabani degistirilebilir.");
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "src", "UluBilge.Api", "appsettings.json")))
                return directory.FullName;
        throw new DirectoryNotFoundException("UluBilge kaynak dizini bulunamadi.");
    }

    private sealed class TestHost(WebApplication application, HttpClient client) : IAsyncDisposable
    {
        private bool disposed;
        public HttpClient Client { get; } = client;

        public async ValueTask DisposeAsync()
        {
            if (disposed) return;
            disposed = true;
            Client.Dispose();
            await application.DisposeAsync();
        }
    }

    private sealed class PersistedClient
    {
        public string ClientId { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public bool IsActive { get; set; }
        public DateTime CreatedAtUtc { get; set; }
    }
}
