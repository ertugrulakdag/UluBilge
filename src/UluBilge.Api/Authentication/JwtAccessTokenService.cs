using System.Globalization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace UluBilge.Api.Authentication
{
    internal sealed class JwtAccessTokenService(IOptions<JwtOptions> options, JwtKeyMaterial keyMaterial, TimeProvider timeProvider) : IAccessTokenService
    {
        private readonly JsonWebTokenHandler _handler = new();

        public AccessTokenResponse Create(AuthenticatedUser user, Guid securityStamp, Guid familyId)
        {
            if (user.Id <= 0)
            {
                throw new ArgumentException("Erişim belirteci oluşturmak için kullanıcı kimliği zorunludur.");
            }

            if (string.IsNullOrWhiteSpace(user.UserName))
            {
                throw new ArgumentException("Erişim belirteci oluşturmak için kullanıcı adı zorunludur.");
            }

            if (!AuthenticationClaims.IsSupportedRole(user.RoleEnum) || securityStamp == Guid.Empty || familyId == Guid.Empty)
            {
                throw new ArgumentException("Erişim belirteci oluşturmak için geçerli rol ve oturum bilgileri zorunludur.");
            }

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
                    [JwtRegisteredClaimNames.Sub] = user.Id.ToString(CultureInfo.InvariantCulture),
                    [JwtRegisteredClaimNames.Name] = user.UserName,
                    [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString("N"),
                    [AuthenticationClaims.RoleEnum] = (long)user.RoleEnum,
                    [AuthenticationClaims.SecurityStamp] = securityStamp.ToString("D"),
                    [AuthenticationClaims.SessionId] = familyId.ToString("D")
                }
            });

            return new AccessTokenResponse(token, "Bearer", settings.AccessTokenLifetimeMinutes * 60, expiresAt);
        }
    }
}
