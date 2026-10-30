using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using UluBilge.Api.Models;
using UluBilge.App.Commands.AuthHandler;
using UluBilge.App.Queries.AuthHandler;
using UluBilge.Core.Enums;

namespace UluBilge.Test;

[TestFixture]
public sealed class AuthControllerTests
{
    private ApiTestClient api = null!;

    [SetUp]
    public void SetUp() => api = new ApiTestClient();

    [TearDown]
    public void TearDown() => api.Dispose();

    [Test]
    public async Task Login_Refresh_RotatesTokens_AndRejectsReplayAndConcurrentReuse()
    {
        var result = await api.LoginAsync();

        Assert.That(result.AccessToken, Is.Not.Empty);
        Assert.That(result.TokenType, Is.EqualTo("Bearer"));
        Assert.That(result.ExpiresIn, Is.GreaterThan(0));
        Assert.That(result.ExpiresAtUtc, Is.GreaterThan(DateTimeOffset.UtcNow));
        Assert.That(result.RefreshToken, Has.Length.EqualTo(43));
        Assert.That(result.RefreshTokenExpiresAtUtc, Is.GreaterThan(result.ExpiresAtUtc));

        // Yenileme access token olmadan ve gecersiz bir access token ile de calisir.
        api.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "expired-or-invalid-access-token");
        using var refreshResponse = await api.Client.PostAsJsonAsync("/v1/Auth/RefreshToken", new { result.RefreshToken });
        Assert.That(refreshResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var refreshed = (await refreshResponse.Content.ReadFromJsonAsync<BaseResponse<LoginAsync.Result>>())!.Data;
        Assert.That(refreshed.RefreshToken, Is.Not.EqualTo(result.RefreshToken));
        Assert.That(refreshed.AccessToken, Is.Not.EqualTo(result.AccessToken));
        Assert.That(refreshed.RefreshTokenExpiresAtUtc, Is.EqualTo(result.RefreshTokenExpiresAtUtc).Within(TimeSpan.FromMilliseconds(1)));

        api.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", refreshed.AccessToken);
        using var me = await api.Client.GetAsync("/v1/Auth/Me");
        Assert.That(me.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        api.Client.DefaultRequestHeaders.Authorization = null;
        using var secondRefreshResponse = await api.Client.PostAsJsonAsync("/v1/Auth/RefreshToken", new { refreshed.RefreshToken });
        Assert.That(secondRefreshResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var second = (await secondRefreshResponse.Content.ReadFromJsonAsync<BaseResponse<LoginAsync.Result>>())!.Data;

        var concurrent = await Task.WhenAll(
            api.Client.PostAsJsonAsync("/v1/Auth/RefreshToken", new { second.RefreshToken }),
            api.Client.PostAsJsonAsync("/v1/Auth/RefreshToken", new { second.RefreshToken }));
        try
        {
            Assert.That(concurrent.Count(response => response.StatusCode == HttpStatusCode.OK), Is.EqualTo(1));
            Assert.That(concurrent.Count(response => response.StatusCode == HttpStatusCode.Unauthorized), Is.EqualTo(1));
            var winner = (await concurrent.Single(response => response.IsSuccessStatusCode).Content.ReadFromJsonAsync<BaseResponse<LoginAsync.Result>>())!.Data;
            using var revoked = await api.Client.PostAsJsonAsync("/v1/Auth/RefreshToken", new { winner.RefreshToken });
            Assert.That(revoked.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        }
        finally
        {
            foreach (var response in concurrent) response.Dispose();
        }

        using var replay = await api.Client.PostAsJsonAsync("/v1/Auth/RefreshToken", new { result.RefreshToken });
        Assert.That(replay.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task Login_InvalidCredentials_ReturnsUnauthorized()
    {
        // Gerçek kullanıcının başarısız giriş sayacını değiştirmemek için bulunmayan bir ad kullanılır.
        using var response = await api.Client.PostAsJsonAsync("/v1/Auth/Login", new
        {
            userName = "unknown-" + Guid.NewGuid().ToString("N"),
            password = "wrong-password"
        });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task Login_EmptyCredentials_ReturnsBadRequest()
    {
        using var response = await api.Client.PostAsJsonAsync("/v1/Auth/Login", new { userName = "", password = "" });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task Me_ValidToken_ReturnsLoggedInUser()
    {
        var login = await api.LoginAsync();
        var token = new JsonWebToken(login.AccessToken);
        var roles = token.Claims.Where(claim => claim.Type == "role")
            .Aggregate(RoleEnum.None, (mask, claim) => mask | Enum.Parse<RoleEnum>(claim.Value));

        using var response = await api.Client.GetAsync("/v1/Auth/Me");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var body = await response.Content.ReadFromJsonAsync<BaseResponse<MeAsync.Result>>();

        Assert.That(body, Is.Not.Null);
        Assert.That(body!.Data.Id.ToString(CultureInfo.InvariantCulture), Is.EqualTo(token.Subject));
        Assert.That(body.Data.UserName, Is.EqualTo(token.GetClaim("name").Value));
        Assert.That(body.Data.RoleEnum, Is.EqualTo(roles));
        Assert.That(body.Data.IsActive, Is.True);
    }

    [TestCase(null)]
    [TestCase("invalid-token")]
    public async Task Me_MissingOrInvalidToken_ReturnsUnauthorized(string? token)
    {
        if (token is not null)
            api.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await api.Client.GetAsync("/v1/Auth/Me");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task Refresh_UnknownToken_ReturnsUnauthorized()
    {
        var refreshToken = Microsoft.IdentityModel.Tokens.Base64UrlEncoder.Encode(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        using var response = await api.Client.PostAsJsonAsync("/v1/Auth/RefreshToken", new { refreshToken });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("invalid-token")]
    [TestCase("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA+")]
    public async Task Refresh_MissingOrMalformedToken_ReturnsBadRequest(string? refreshToken)
    {
        using var response = await api.Client.PostAsJsonAsync("/v1/Auth/RefreshToken", new { refreshToken });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }
}
