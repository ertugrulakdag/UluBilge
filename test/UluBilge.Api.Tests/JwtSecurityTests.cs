using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using UluBilge.Api.Authentication;
using Xunit;

namespace UluBilge.Api.Tests;

public sealed class JwtSecurityTests : IClassFixture<DevelopmentJwtApiFixture>
{
    private readonly JwtApiFactory _factory;

    public JwtSecurityTests(DevelopmentJwtApiFixture fixture) => _factory = fixture.Factory;

    [Fact]
    public async Task Mock_login_issues_short_lived_token_that_authenticates_current_user()
    {
        using var client = _factory.CreateHttpsClient();
        var response = await client.PostAsJsonAsync("/v1/Auth/login", new { userName = "admin", password = "admin" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var token = await response.Content.ReadFromJsonAsync<AccessTokenResponse>();
        Assert.NotNull(token);
        Assert.Equal("Bearer", token.TokenType);
        Assert.Equal(300, token.ExpiresIn);
        Assert.InRange(token.ExpiresAtUtc, DateTimeOffset.UtcNow.AddMinutes(4), DateTimeOffset.UtcNow.AddMinutes(6));

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        var me = await client.GetAsync("/v1/Auth/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.True(me.Headers.CacheControl?.NoStore);
        var user = await me.Content.ReadFromJsonAsync<AuthenticatedUser>();
        Assert.NotNull(user);
        Assert.Equal("development-admin", user.Id);
        Assert.Equal("admin", user.UserName);
        Assert.Contains("Admin", user.Roles);
        Assert.Contains("regions:read", user.Permissions);
        Assert.Contains("regions:write", user.Permissions);
    }

    [Theory]
    [InlineData("GET", "/v1/Region/GetRegions")]
    [InlineData("GET", "/v1/Region/GetRegion/1")]
    [InlineData("POST", "/v1/Region/Add")]
    [InlineData("PUT", "/v1/Region/Update")]
    [InlineData("DELETE", "/v1/Region/Delete/1")]
    [InlineData("GET", "/v1/Auth/me")]
    public async Task Anonymous_requests_are_rejected_before_handlers(string method, string path)
    {
        using var client = _factory.CreateHttpsClient();
        using var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Bearer", Assert.Single(response.Headers.WwwAuthenticate).Scheme);
    }

    [Theory]
    [InlineData("tampered")]
    [InlineData("wrong-key")]
    [InlineData("wrong-issuer")]
    [InlineData("wrong-audience")]
    [InlineData("expired")]
    [InlineData("future")]
    [InlineData("unsigned")]
    [InlineData("wrong-algorithm")]
    [InlineData("wrong-type")]
    [InlineData("missing-expiration")]
    [InlineData("missing-issued-at")]
    [InlineData("missing-not-before")]
    [InlineData("missing-subject")]
    [InlineData("missing-name")]
    [InlineData("missing-token-id")]
    [InlineData("duplicate-subject")]
    [InlineData("invalid-issued-at")]
    [InlineData("not-before-after-issued-at")]
    [InlineData("excessive-lifetime")]
    public async Task Invalid_tokens_receive_a_generic_challenge(string scenario)
    {
        using var client = _factory.CreateHttpsClient();
        var token = TestTokens.Create(scenario);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.GetAsync("/v1/Auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Bearer", response.Headers.WwwAuthenticate.ToString());
        Assert.True(response.Headers.CacheControl?.NoStore);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(token, body);
        Assert.DoesNotContain("IDX", body);
        Assert.DoesNotContain("error_description", body);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task A_valid_read_token_reaches_the_query_handler()
    {
        using var client = _factory.CreateHttpsClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create());
        using var response = await client.GetAsync("/v1/Region/GetRegions");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Security test region", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("POST", "/v1/Region/Add")]
    [InlineData("PUT", "/v1/Region/Update")]
    [InlineData("DELETE", "/v1/Region/Delete/1")]
    public async Task Read_permission_cannot_modify_data_even_with_an_admin_role(string method, string path)
    {
        using var client = _factory.CreateHttpsClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            TestTokens.Create(roles: ["Admin"]));
        using var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("regions:read-extra")]
    [InlineData("REGIONS:READ")]
    [InlineData("regions:write")]
    public async Task Read_permission_must_match_exactly(string permission)
    {
        using var client = _factory.CreateHttpsClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            TestTokens.Create(permissions: [permission]));
        using var response = await client.GetAsync("/v1/Region/GetRegions");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Query_and_cookie_tokens_are_not_accepted()
    {
        using var client = _factory.CreateHttpsClient();
        var token = TestTokens.Create();
        client.DefaultRequestHeaders.Add("Cookie", $"access_token={token}");
        using var response = await client.GetAsync($"/v1/Auth/me?access_token={token}");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Development_docs_are_anonymous_and_describe_the_actual_security_boundary()
    {
        using var client = _factory.CreateHttpsClient();
        using var docs = await client.GetAsync("/docs/");
        Assert.Equal(HttpStatusCode.OK, docs.StatusCode);
        using var document = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));
        var root = document.RootElement;
        var bearer = root.GetProperty("components").GetProperty("securitySchemes").GetProperty("Bearer");
        Assert.Equal("bearer", bearer.GetProperty("scheme").GetString());
        var paths = root.GetProperty("paths");
        var secured = paths.GetProperty("/v1/Region/GetRegions").GetProperty("get");
        Assert.Equal(1, secured.GetProperty("security").GetArrayLength());
        var login = paths.GetProperty("/v1/Auth/login").GetProperty("post");
        Assert.False(login.TryGetProperty("security", out var loginSecurity) && loginSecurity.GetArrayLength() > 0);
    }
}

public sealed class DevelopmentJwtApiFixture : IDisposable
{
    public JwtApiFactory Factory { get; } = new();

    public void Dispose() => Factory.Dispose();
}
