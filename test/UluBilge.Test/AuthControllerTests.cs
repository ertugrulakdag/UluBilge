using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using UluBilge.Api.Models;
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
    public async Task Login_ValidCredentials_ReturnsToken()
    {
        var result = await api.LoginAsync();

        Assert.That(result.AccessToken, Is.Not.Empty);
        Assert.That(result.TokenType, Is.EqualTo("Bearer"));
        Assert.That(result.ExpiresIn, Is.GreaterThan(0));
        Assert.That(result.ExpiresAtUtc, Is.GreaterThan(DateTimeOffset.UtcNow));
    }

    [Test]
    public async Task Login_InvalidCredentials_ReturnsUnauthorized()
    {
        // Gerçek kullanıcının başarısız giriş sayacını değiştirmemek için bulunmayan bir ad kullanılır.
        using var response = await api.Client.PostAsJsonAsync("/v1/Auth/login", new
        {
            userName = "unknown-" + Guid.NewGuid().ToString("N"),
            password = "wrong-password"
        });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task Login_EmptyCredentials_ReturnsBadRequest()
    {
        using var response = await api.Client.PostAsJsonAsync("/v1/Auth/login", new { userName = "", password = "" });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task Me_ValidToken_ReturnsLoggedInUser()
    {
        var login = await api.LoginAsync();
        var token = new JsonWebToken(login.AccessToken);
        var roles = token.Claims.Where(claim => claim.Type == "role")
            .Aggregate(RoleEnum.None, (mask, claim) => mask | Enum.Parse<RoleEnum>(claim.Value));

        using var response = await api.Client.GetAsync("/v1/Auth/me");
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

        using var response = await api.Client.GetAsync("/v1/Auth/me");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }
}
