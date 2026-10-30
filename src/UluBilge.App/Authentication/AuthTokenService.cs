using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using UluBilge.App.Commands.AuthHandler;
using UluBilge.Core.Entities;
using UluBilge.Core.Enums;
using UluBilge.Data.Abstract;

namespace UluBilge.App.Authentication
{
    public sealed class AuthTokenService(IUserTokenRepository tokens, JwtOptions settings, SigningCredentials signingCredentials)
    {
        public async Task<(LoginAsync.Result Result, long TokenId)> IssueAsync(User user, Guid familyId, DateTime now, DateTime refreshExpiresAtUtc, CancellationToken ct = default)
        {
            var expiresAt = now.AddMinutes(settings.AccessTokenLifetimeMinutes);
            var roles = Enum.GetValues<RoleEnum>()
                .Where(role => role != RoleEnum.None && user.RoleEnum.HasFlag(role))
                .Select(role => role.ToString()).ToArray();

            var accessToken = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
            {
                Issuer = settings.Issuer,
                Audience = settings.Audience,
                IssuedAt = now,
                NotBefore = now,
                Expires = expiresAt,
                SigningCredentials = signingCredentials,
                Claims = new Dictionary<string, object>
                {
                    ["sub"] = user.Id.ToString(CultureInfo.InvariantCulture),
                    ["name"] = user.UserName,
                    ["jti"] = Guid.NewGuid().ToString("N"),
                    [JwtOptions.RoleClaimType] = roles
                }
            });

            var refreshToken = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
            var tokenId = await tokens.InsertAsync(new UserToken
            {
                UserId = user.Id,
                TokenHash = HashRefreshToken(refreshToken),
                FamilyId = familyId,
                CreatedAtUtc = now,
                ExpiresAtUtc = refreshExpiresAtUtc
            }, ct);

            return (new LoginAsync.Result
            {
                AccessToken = accessToken,
                ExpiresIn = settings.AccessTokenLifetimeMinutes * 60,
                ExpiresAtUtc = new DateTimeOffset(expiresAt, TimeSpan.Zero),
                RefreshToken = refreshToken,
                RefreshTokenExpiresAtUtc = new DateTimeOffset(DateTime.SpecifyKind(refreshExpiresAtUtc, DateTimeKind.Utc))
            }, tokenId);
        }

        public static byte[] HashRefreshToken(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));
    }
}
