using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using Xunit;

namespace UluBilge.Api.Tests;

public sealed class AuthenticationBoundaryTests
{
    [Fact]
    public async Task Wrong_user_and_wrong_password_return_the_same_error()
    {
        await using var factory = new JwtApiFactory();
        using var client = factory.CreateHttpsClient();
        using var unknown = await client.PostAsJsonAsync("/v1/Auth/login", new { userName = "unknown", password = "admin" });
        using var wrong = await client.PostAsJsonAsync("/v1/Auth/login", new { userName = "admin", password = "incorrect" });
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal("Bearer", wrong.Headers.WwwAuthenticate.ToString());
        // Compare only the fixed fields; tracing identifiers can differ between requests.
        var unknownBody = await unknown.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>();
        var wrongBody = await wrong.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>();
        Assert.Equal(unknownBody?.Title, wrongBody?.Title);
        Assert.Equal(unknownBody?.Detail, wrongBody?.Detail);
    }

    [Fact]
    public async Task Sixth_login_attempt_is_limited_even_with_spoofed_forwarding_headers()
    {
        await using var factory = new JwtApiFactory();
        using var client = factory.CreateHttpsClient();
        for (var attempt = 0; attempt < 6; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/Auth/login")
            {
                Content = JsonContent.Create(new { userName = "admin", password = "wrong" })
            };
            request.Headers.Add("X-Forwarded-For", $"192.0.2.{attempt + 1}");
            using var response = await client.SendAsync(request);
            Assert.Equal(attempt < 5 ? HttpStatusCode.Unauthorized : HttpStatusCode.TooManyRequests, response.StatusCode);
            if (attempt == 5)
            {
                Assert.True(response.Headers.RetryAfter?.Delta > TimeSpan.Zero);
            }
        }
    }

    [Theory]
    [InlineData("", "admin")]
    [InlineData("admin", "")]
    public async Task Empty_login_fields_are_bad_requests(string userName, string password)
    {
        await using var factory = new JwtApiFactory();
        using var client = factory.CreateHttpsClient();
        using var response = await client.PostAsJsonAsync("/v1/Auth/login", new { userName, password });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("not-base64")]
    [InlineData("YWJjZA==")]
    [InlineData("")]
    public void Invalid_configured_key_prevents_startup_instead_of_falling_back(string signingKey)
    {
        using var factory = new JwtApiFactory(overrides: new Dictionary<string, string?>
        {
            ["Jwt:SigningKeyBase64"] = signingKey
        });
        Assert.ThrowsAny<OptionsValidationException>(() => factory.CreateHttpsClient());
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Test")]
    public void Mock_authentication_cannot_start_outside_development(string environment)
    {
        using var factory = new JwtApiFactory(environment);
        var error = Assert.ThrowsAny<InvalidOperationException>(() => factory.CreateHttpsClient());
        Assert.Contains("IUserAuthenticator", error.Message);
    }

    [Fact]
    public void Nondevelopment_authentication_also_requires_an_explicit_signing_key()
    {
        using var factory = new JwtApiFactory("Production", new Dictionary<string, string?>
        {
            ["Jwt:SigningKeyBase64"] = null
        }, registerProductionAuthenticator: true);
        Assert.ThrowsAny<OptionsValidationException>(() => factory.CreateHttpsClient());
    }

    [Fact]
    public async Task Production_requires_https_and_does_not_publish_docs()
    {
        await using var factory = new JwtApiFactory("Production", registerProductionAuthenticator: true);
        using var client = factory.CreateHttpsClient();
        // With no production docs endpoint, the default policy still protects unmatched requests.
        foreach (var path in new[] { "/docs", "/openapi/v1.json" })
        {
            using var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        using var insecure = await client.GetAsync("http://localhost/v1/Auth/login");
        Assert.Equal(HttpStatusCode.BadRequest, insecure.StatusCode);
        using var login = await client.PostAsJsonAsync("/v1/Auth/login", new { userName = "admin", password = "admin" });
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
    }
}
