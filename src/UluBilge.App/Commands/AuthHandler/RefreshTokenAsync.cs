using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
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

            [JsonIgnore]
            public OAuthTokenContext? OAuth { get; init; }
        }

        public sealed class InvalidScopeException() : ValidationException("Izin verilen kapsam genisletilemez.") { }

        public sealed class Handler(IUserTokenRepository tokens, IUserRepository users, AuthTokenService tokenService, TimeProvider clock) : IAsyncCommandHandler<Command, LoginAsync.Result?>
        {
            private static readonly RoleEnum KnownRoles = Enum.GetValues<RoleEnum>().Aggregate(RoleEnum.None, (mask, role) => mask | role);

            public async Task<LoginAsync.Result?> HandleAsync(Command command, CancellationToken ct = default)
            {
                var token = await tokens.GetByTokenHashForUpdateAsync(AuthTokenService.HashRefreshToken(command.RefreshToken), ct);
                if (token is null)
                    return null;

                // REST, baska OAuth istemcisi veya baska kaynak tokeni tuketilemez/iptal edilemez.
                var oauth = command.OAuth;
                if (oauth is null)
                {
                    if (token.OAuthClientId is not null || token.OAuthResource is not null || token.OAuthScope is not null)
                        return null;
                }
                else if (token.OAuthClientId != oauth.ClientId || token.OAuthResource != oauth.Resource
                    || token.OAuthScope is null || !token.SecurityStamp.HasValue)
                {
                    return null;
                }

                var now = clock.GetUtcNow().UtcDateTime;
                if (token.ConsumedAtUtc.HasValue || token.RevokedAtUtc.HasValue)
                {
                    await tokens.RevokeFamilyAsync(token.FamilyId, now, ct);
                    return null; // Iptalin commit edilmesi icin exception firlatilmaz.
                }

                if (token.ExpiresAtUtc <= now)
                    return null;

                if (oauth is not null)
                {
                    var scopes = (oauth.Scope ?? token.OAuthScope!).Split(' ', StringSplitOptions.RemoveEmptyEntries).Distinct().ToArray();
                    if (scopes.Except(token.OAuthScope!.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Any())
                        throw new InvalidScopeException();
                    oauth = oauth with { Scope = string.Join(' ', scopes) };
                }

                var user = await users.GetByUserIdAsync(token.UserId, ct);
                if (user is null || !user.IsActive || user.LockoutEndUtc > now || (user.RoleEnum & ~KnownRoles) != 0
                    || token.SecurityStamp.HasValue && token.SecurityStamp != user.SecurityStamp
                    || oauth is not null && !user.RoleEnum.HasFlag(RoleEnum.Admin) && !user.RoleEnum.HasFlag(RoleEnum.Report))
                {
                    await tokens.RevokeFamilyAsync(token.FamilyId, now, ct);
                    return null;
                }

                // Yenileme ilk girisin son gecerlilik zamanini uzatmaz.
                var issued = await tokenService.IssueAsync(user, token.FamilyId, now, token.ExpiresAtUtc, ct, oauth);
                await tokens.ConsumeAsync(token.Id, issued.TokenId, now, ct);
                if (issued.TokenId is null)
                    await tokens.RevokeFamilyAsync(token.FamilyId, now, ct);
                return issued.Result;
            }
        }
    }
}
