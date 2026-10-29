using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using UluBilge.Api.Authentication;
using UluBilge.App.Queries.RegionHandler;
using UluBilge.Core.Entities;
using UluBilge.Core.Queries;

namespace UluBilge.Api.Tests;

public sealed class JwtApiFactory(
    string environment = "Development",
    IReadOnlyDictionary<string, string?>? overrides = null,
    bool registerProductionAuthenticator = false) : WebApplicationFactory<Program>
{
    // Public, deterministic test data; never used by the running API.
    internal static readonly byte[] SigningKey = Enumerable.Range(1, 64).Select(value => (byte)value).ToArray();
    internal const string Issuer = "UluBilge.SecurityTests";
    internal const string Audience = "UluBilge.SecurityTests.Api";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            var settings = new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = Issuer,
                ["Jwt:Audience"] = Audience,
                ["Jwt:SigningKeyBase64"] = Convert.ToBase64String(SigningKey),
                ["Jwt:AccessTokenLifetimeMinutes"] = "5"
            };

            if (overrides is not null)
            {
                foreach (var (key, value) in overrides)
                {
                    settings[key] = value;
                }
            }

            configuration.AddInMemoryCollection(settings);
        });

        builder.ConfigureTestServices(services =>
        {
            // Exercise real JWT authentication/authorization, with no SQL dependency.
            services.RemoveAll<IAsyncQueryHandler<GetRegionsAsync.Query, GetRegionsAsync.Result>>();
            services.AddScoped<IAsyncQueryHandler<GetRegionsAsync.Query, GetRegionsAsync.Result>, TestRegionsHandler>();

            if (registerProductionAuthenticator)
            {
                services.AddSingleton<IUserAuthenticator, RejectingProductionAuthenticator>();
            }
        });
    }

    internal HttpClient CreateHttpsClient() => CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
        HandleCookies = false
    });

    private sealed class TestRegionsHandler : IAsyncQueryHandler<GetRegionsAsync.Query, GetRegionsAsync.Result>
    {
        public Task<GetRegionsAsync.Result> HandleAsync(GetRegionsAsync.Query query, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(new GetRegionsAsync.Result
            {
                Data = [new Region { RegionId = 1, RegionDescription = "Security test region" }],
                Total = 1,
                PageNumber = query.PageNumber,
                PageSize = query.PageSize
            });
        }
    }

    private sealed class RejectingProductionAuthenticator : IUserAuthenticator
    {
        public Task<AuthenticatedUser?> AuthenticateAsync(string userName, string password, CancellationToken cancellationToken = default)
            => Task.FromResult<AuthenticatedUser?>(null);
    }
}

internal static class TestTokens
{
    internal static string Create(
        string? scenario = null,
        string[]? permissions = null,
        string[]? roles = null)
    {
        var now = DateTimeOffset.UtcNow;
        var issuedAt = scenario == "expired" ? now.AddMinutes(-10) : now;
        if (scenario == "future")
        {
            issuedAt = now.AddMinutes(2);
        }

        var key = scenario == "wrong-key"
            ? Enumerable.Range(65, 64).Select(value => (byte)value).ToArray()
            : JwtApiFactory.SigningKey;
        var algorithm = scenario == "wrong-algorithm" ? SecurityAlgorithms.HmacSha512 : SecurityAlgorithms.HmacSha256;
        var credentials = scenario == "unsigned" ? null : new SigningCredentials(new SymmetricSecurityKey(key), algorithm);
        var header = new JwtHeader(credentials)
        {
            ["typ"] = scenario == "wrong-type" ? "JWT" : "at+jwt"
        };
        var payload = new JwtPayload
        {
            ["iss"] = scenario == "wrong-issuer" ? "untrusted-issuer" : JwtApiFactory.Issuer,
            ["aud"] = scenario == "wrong-audience" ? "other-api" : JwtApiFactory.Audience,
            ["sub"] = "test-user",
            ["name"] = "test.user",
            ["jti"] = Guid.NewGuid().ToString("N"),
            ["iat"] = issuedAt.ToUnixTimeSeconds(),
            ["nbf"] = issuedAt.ToUnixTimeSeconds(),
            ["exp"] = issuedAt.AddMinutes(scenario == "excessive-lifetime" ? 30 : 5).ToUnixTimeSeconds(),
            ["role"] = roles ?? ["Reader"],
            ["permission"] = permissions ?? ["regions:read"]
        };

        switch (scenario)
        {
            case "missing-expiration": payload.Remove("exp"); break;
            case "missing-issued-at": payload.Remove("iat"); break;
            case "missing-not-before": payload.Remove("nbf"); break;
            case "missing-subject": payload.Remove("sub"); break;
            case "missing-name": payload.Remove("name"); break;
            case "missing-token-id": payload.Remove("jti"); break;
            case "duplicate-subject": payload["sub"] = new[] { "test-user", "other-user" }; break;
            case "invalid-issued-at": payload["iat"] = "not-a-timestamp"; break;
            case "not-before-after-issued-at": payload["nbf"] = now.AddSeconds(1).ToUnixTimeSeconds(); break;
        }

        var token = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(header, payload));
        if (scenario == "tampered")
        {
            var signatureStart = token.LastIndexOf('.') + 1;
            token = token[..signatureStart] + (token[signatureStart] == 'A' ? 'B' : 'A') + token[(signatureStart + 1)..];
        }

        return token;
    }
}
