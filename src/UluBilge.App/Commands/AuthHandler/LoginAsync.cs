using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using UluBilge.App.Authentication;
using UluBilge.Core.Commands;
using UluBilge.Core.Entities;
using UluBilge.Core.Enums;
using UluBilge.Data.Abstract;

namespace UluBilge.App.Commands.AuthHandler
{
    public static class LoginAsync
    {
        public sealed class Command : ICommand<Result?>
        {
            [Required, StringLength(64)]
            public string UserName { get; init; } = string.Empty;

            [Required, StringLength(128)]
            public string Password { get; init; } = string.Empty;
        }

        public sealed class Result
        {
            public required string AccessToken { get; init; }
            public string TokenType { get; init; } = "Bearer";
            public int ExpiresIn { get; init; }
            public DateTimeOffset ExpiresAtUtc { get; init; }
        }

        public sealed class Handler(IUserRepository userRepository, JwtOptions settings, SigningCredentials signingCredentials) : IAsyncCommandHandler<Command, Result?>
        {
            private static readonly PasswordHasher<User> Passwords = new(Options.Create(new PasswordHasherOptions { IterationCount = 210_000 }));
            private static readonly User DummyUser = new();
            private static readonly string DummyHash = Passwords.HashPassword(DummyUser, Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
            private static readonly RoleEnum KnownRoles = Enum.GetValues<RoleEnum>().Aggregate(RoleEnum.None, (mask, role) => mask | role);

            public async Task<Result?> HandleAsync(Command command, CancellationToken ct = default)
            {
                // Transaction zaten CommandHandlerDecorator tarafindan acilir.
                var user = await userRepository.GetByNormalizedUserNameForUpdateAsync(command.UserName.Trim().ToUpperInvariant(), ct);
                var verification = VerifyPassword(user, command.Password);
                var now = DateTime.UtcNow;
                if (user is null || !user.IsActive || user.LockoutEndUtc > now || (user.RoleEnum & ~KnownRoles) != 0)
                {
                    return null;
                }

                if (verification == PasswordVerificationResult.Failed)
                {
                    var failedCount = user.LockoutEndUtc.HasValue ? 1 : Math.Min(user.AccessFailedCount, 4) + 1;
                    await userRepository.RecordFailedLoginAsync(user.Id, failedCount, failedCount >= 5 ? now.AddMinutes(15) : null, now, ct);
                    return null; // Hata sayacinin commit edilmesi icin exception firlatilmaz.
                }

                if (verification == PasswordVerificationResult.SuccessRehashNeeded)
                {
                    user.PasswordHash = Passwords.HashPassword(user, command.Password);
                }

                await userRepository.RecordSuccessfulLoginAsync(user, now, ct);
                return CreateToken(user, now);
            }

            private Result CreateToken(User user, DateTime now)
            {
                var expiresAt = now.AddMinutes(settings.AccessTokenLifetimeMinutes);
                var roles = Enum.GetValues<RoleEnum>()
                    .Where(role => role != RoleEnum.None && user.RoleEnum.HasFlag(role))
                    .Select(role => role.ToString()).ToArray();

                var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
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
                        [JwtOptions.RoleClaimType] = roles
                    }
                });

                return new Result
                {
                    AccessToken = token,
                    ExpiresIn = settings.AccessTokenLifetimeMinutes * 60,
                    ExpiresAtUtc = new DateTimeOffset(expiresAt, TimeSpan.Zero)
                };
            }

            private static PasswordVerificationResult VerifyPassword(User? user, string password)
            {
                try
                {
                    // Bilinmeyen kullanicida da hash kontrolu yapilarak zaman farki azaltilir.
                    return Passwords.VerifyHashedPassword(user ?? DummyUser, user?.PasswordHash ?? DummyHash, password);
                }
                catch (FormatException)
                {
                    return PasswordVerificationResult.Failed;
                }
            }
        }
    }
}
