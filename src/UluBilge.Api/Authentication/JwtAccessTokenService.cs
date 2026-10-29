using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace UluBilge.Api.Authentication;

internal sealed class JwtAccessTokenService(
    IOptions<JwtOptions> options,
    JwtKeyMaterial keyMaterial,
    TimeProvider timeProvider) : IAccessTokenService
{
    private readonly JsonWebTokenHandler _handler = new();

    public AccessTokenResponse Create(AuthenticatedUser user)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(user.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(user.UserName);

        var settings = options.Value;
        var issuedAt = DateTimeOffset.FromUnixTimeSeconds(timeProvider.GetUtcNow().ToUnixTimeSeconds());
        var expiresAt = issuedAt.AddMinutes(settings.AccessTokenLifetimeMinutes);
        var token = _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = settings.Issuer,
            Audience = settings.Audience,
            IssuedAt = issuedAt.UtcDateTime,
            NotBefore = issuedAt.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            TokenType = "at+jwt",
            SigningCredentials = keyMaterial.SigningCredentials,
            Claims = new Dictionary<string, object>
            {
                [JwtRegisteredClaimNames.Sub] = user.Id,
                [JwtRegisteredClaimNames.Name] = user.UserName,
                [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString("N"),
                ["role"] = user.Roles.ToArray(),
                ["permission"] = user.Permissions.ToArray()
            }
        });

        return new AccessTokenResponse(token, "Bearer", settings.AccessTokenLifetimeMinutes * 60, expiresAt);
    }
}
