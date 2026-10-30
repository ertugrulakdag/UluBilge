using System.ComponentModel.DataAnnotations;
using UluBilge.App.Authentication;
using UluBilge.Core.Commands;
using UluBilge.Core.Enums;
using UluBilge.Data.Abstract;

namespace UluBilge.App.Commands.AuthHandler
{
    public static class RefreshTokenAsync
    {
        public sealed class Command : ICommand<LoginAsync.Result?>
        {
            [Required, StringLength(43, MinimumLength = 43), RegularExpression("^[A-Za-z0-9_-]{43}$")]
            public string RefreshToken { get; init; } = string.Empty;
        }

        public sealed class Handler(IUserTokenRepository tokens, IUserRepository users, AuthTokenService tokenService) : IAsyncCommandHandler<Command, LoginAsync.Result?>
        {
            private static readonly RoleEnum KnownRoles = Enum.GetValues<RoleEnum>().Aggregate(RoleEnum.None, (mask, role) => mask | role);

            public async Task<LoginAsync.Result?> HandleAsync(Command command, CancellationToken ct = default)
            {
                var token = await tokens.GetByTokenHashForUpdateAsync(AuthTokenService.HashRefreshToken(command.RefreshToken), ct);
                if (token is null)
                    return null;

                var now = DateTime.UtcNow;
                if (token.ConsumedAtUtc.HasValue || token.RevokedAtUtc.HasValue)
                {
                    await tokens.RevokeFamilyAsync(token.FamilyId, now, ct);
                    return null; // Iptalin commit edilmesi icin exception firlatilmaz.
                }

                if (token.ExpiresAtUtc <= now)
                    return null;

                var user = await users.GetByUserIdAsync(token.UserId, ct);
                if (user is null || !user.IsActive || user.LockoutEndUtc > now || (user.RoleEnum & ~KnownRoles) != 0)
                {
                    await tokens.RevokeFamilyAsync(token.FamilyId, now, ct);
                    return null;
                }

                // Yenileme ilk girisin son gecerlilik zamanini uzatmaz.
                var issued = await tokenService.IssueAsync(user, token.FamilyId, now, token.ExpiresAtUtc, ct);
                await tokens.ConsumeAsync(token.Id, issued.TokenId, now, ct);
                return issued.Result;
            }
        }
    }
}
