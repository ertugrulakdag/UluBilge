using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using UluBilge.Core.Entities;
using UluBilge.Data.Abstract;

namespace UluBilge.Api.Authentication
{
    public sealed class DatabaseUserAuthenticationService(
        IUnitOfWork unitOfWork,
        IUserRepository users,
        IUserTokenRepository tokens,
        UserPasswordService passwords,
        IAccessTokenService accessTokens,
        IOptions<JwtOptions> options,
        TimeProvider timeProvider) : IUserAuthenticationService
    {
        private const int MaximumFailedAttempts = 5;
        private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

        public async Task<TokenResponse?> LoginAsync(string userName, string password, CancellationToken cancellationToken = default) => // Mevcut JSON giris uc noktasini korur.
            (await LoginCoreAsync(userName, password, cancellationToken))?.Tokens; // Ortak giris sonucundan API tokenlarini dondurur.

        public async Task<UserSession?> LoginForOAuthAsync(string userName, string password, CancellationToken cancellationToken = default) => // OAuth icin ayni kullanici dogrulama akisini kullanir.
            (await LoginCoreAsync(userName, password, cancellationToken))?.Session; // Parola dogrulandiginda kalici oturum kimligini dondurur.

        private Task<AuthenticatedLogin?> LoginCoreAsync(string userName, string password, CancellationToken cancellationToken) => // Iki giris turunun parola, kilitleme ve oturum mantigini ortak tutar.
            unitOfWork.ExecuteCommandAsync<AuthenticatedLogin?>(async ct => // Kullanici kilidini ve oturum kaydini ayni islemde yonetir.
            {
                var user = await users.GetByNormalizedUserNameForUpdateAsync(userName.Trim().ToUpperInvariant(), ct);
                var passwordResult = passwords.Verify(user, password);
                var now = timeProvider.GetUtcNow().UtcDateTime;
                if (user is null || !user.IsActive || user.LockoutEndUtc > now
                    || !AuthenticationClaims.IsSupportedRole(user.RoleEnum) || user.SecurityStamp == Guid.Empty)
                {
                    return null;
                }

                if (passwordResult == PasswordVerificationResult.Failed)
                {
                    var failedCount = user.LockoutEndUtc.HasValue ? 1 : Math.Min(user.AccessFailedCount, MaximumFailedAttempts - 1) + 1;
                    var lockoutEnd = failedCount >= MaximumFailedAttempts ? now.Add(LockoutDuration) : (DateTime?)null;
                    await users.RecordFailedLoginAsync(user.Id, failedCount, lockoutEnd, now, ct);
                    return null;
                }

                if (passwordResult == PasswordVerificationResult.SuccessRehashNeeded)
                {
                    user.PasswordHash = passwords.Hash(user, password);
                }

                await users.RecordSuccessfulLoginAsync(user, now, ct);
                var familyId = Guid.NewGuid(); // Yeni girise ait ortak oturum ailesini olusturur.
                var issuedTokens = await IssueTokensAsync(user, familyId, now, now.AddDays(options.Value.RefreshTokenLifetimeDays), ct); // Mevcut kalici oturum ve yenileme kaydini acar.
                var session = new UserSession(new AuthenticatedUser(user.Id, user.UserName, user.RoleEnum), user.SecurityStamp, familyId); // OAuth claimleri icin dogrulanmis kullanici ve oturum bilgisini hazirlar.
                return issuedTokens is null ? null : new AuthenticatedLogin(session, issuedTokens); // Yalnizca basarili giriste oturum ve tokenlari dondurur.
            }, cancellationToken);

        public Task<TokenResponse?> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default)
        {
            if (!TryHashRefreshToken(refreshToken, out var hash))
            {
                return Task.FromResult<TokenResponse?>(null);
            }

            return unitOfWork.ExecuteCommandAsync<TokenResponse?>(async ct =>
            {
                var token = await tokens.GetByHashAsync(hash, ct);
                if (token is null)
                {
                    return null;
                }

                // Bütün oturum yazma işlemleri önce kullanıcı satırını kilitler. Aynı kullanıcıya ait yenileme ve çıkış işlemleri sırayla çalışır.
                var user = await users.GetByIdForUpdateAsync(token.UserId, ct);
                token = await tokens.GetByHashAsync(hash, ct);
                if (user is null || token is null)
                {
                    return null;
                }

                var now = timeProvider.GetUtcNow().UtcDateTime;
                if (token.ConsumedAtUtc.HasValue || token.RevokedAtUtc.HasValue || !user.IsActive
                    || !AuthenticationClaims.IsSupportedRole(user.RoleEnum) || user.SecurityStamp == Guid.Empty)
                {
                    // Tekrar kullanım tespitinde iptal kayıtları null sonuçla birlikte commit edilmelidir.
                    await tokens.RevokeFamilyAsync(user.Id, token.FamilyId, now, ct);
                    return null;
                }

                if (token.ExpiresAtUtc <= now)
                {
                    return null;
                }

                // Yenileme zincirinin mutlak bitiş tarihi uzatılmaz.
                return await IssueTokensAsync(user, token.FamilyId, now, token.ExpiresAtUtc, ct, token.Id);
            }, cancellationToken);
        }

        public async Task LogoutAsync(UserSession session, CancellationToken cancellationToken = default)
        {
            await unitOfWork.ExecuteCommandAsync(async ct =>
            {
                await users.GetByIdForUpdateAsync(session.User.Id, ct);
                await tokens.RevokeFamilyAsync(session.User.Id, session.FamilyId, timeProvider.GetUtcNow().UtcDateTime, ct);
                return true;
            }, cancellationToken);
        }

        public Task<bool> IsSessionValidAsync(UserSession session, CancellationToken cancellationToken = default) =>
            // Güvenlik kontrolleri replica gecikmesinden etkilenmemek için yazma bağlantısında yapılır.
            unitOfWork.ExecuteCommandAsync(ct => tokens.IsSessionValidAsync(session.User.Id, session.SecurityStamp,
                session.User.RoleEnum, session.FamilyId, timeProvider.GetUtcNow().UtcDateTime, ct), cancellationToken);

        private async Task<TokenResponse?> IssueTokensAsync(User user, Guid familyId, DateTime now, DateTime expiresAt, CancellationToken cancellationToken, long? previousTokenId = null)
        {
            var refreshToken = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
            var token = new UserToken
            {
                UserId = user.Id,
                FamilyId = familyId,
                TokenHash = SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken)),
                CreatedAtUtc = now,
                ExpiresAtUtc = expiresAt
            };
            var tokenId = await tokens.InsertAsync(token, cancellationToken);
            if (previousTokenId.HasValue && !await tokens.TryConsumeAsync(previousTokenId.Value, tokenId, now, cancellationToken))
            {
                await tokens.RevokeFamilyAsync(user.Id, familyId, now, cancellationToken);
                return null;
            }

            var accessToken = accessTokens.Create(new AuthenticatedUser(user.Id, user.UserName, user.RoleEnum), user.SecurityStamp, familyId);
            return new TokenResponse(accessToken.AccessToken, accessToken.TokenType, accessToken.ExpiresIn, accessToken.ExpiresAtUtc,
                refreshToken, new DateTimeOffset(DateTime.SpecifyKind(expiresAt, DateTimeKind.Utc)));
        }

        private sealed record AuthenticatedLogin(UserSession Session, TokenResponse Tokens); // Ortak giris sonucunu servis icinde tasir.

        private static bool TryHashRefreshToken(string refreshToken, out byte[] hash)
        {
            hash = [];
            if (refreshToken is not { Length: 43 }
                || refreshToken.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
            {
                return false;
            }

            hash = SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken));
            return true;
        }
    }
}
