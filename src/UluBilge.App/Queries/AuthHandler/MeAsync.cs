using System.Security.Claims;
using UluBilge.App.Authentication;
using UluBilge.Core.Entities;
using UluBilge.Core.Enums;
using UluBilge.Core.Queries;
using UluBilge.Data.Abstract;

namespace UluBilge.App.Queries.AuthHandler
{
    public static class MeAsync
    {
        public sealed class Query : IQuery<Result?>
        {
            public ClaimsPrincipal User { get; init; } = new();
        }

        public sealed record Result(int Id, string UserName, RoleEnum RoleEnum, bool IsActive);

        public sealed class Handler(IUserRepository userRepository) : IAsyncQueryHandler<Query, Result?>
        {
            public async Task<Result?> HandleAsync(Query query, CancellationToken ct = default)
            {
                ct.ThrowIfCancellationRequested();
                var principal = query.User;

                if (principal.Identity?.IsAuthenticated != true
                    || !int.TryParse(principal.FindFirst("sub")?.Value, out var id)
                    || id <= 0 || string.IsNullOrWhiteSpace(principal.Identity.Name))
                {
                    return null;
                }
                User? user = await userRepository.GetByUserIdAsync(id, ct);

                var roles = principal.FindAll(JwtOptions.RoleClaimType).Aggregate(RoleEnum.None, (mask, claim) => Enum.TryParse<RoleEnum>(claim.Value, out var role) ? mask | role : mask);
                return new Result(id, principal.Identity.Name, roles, user?.IsActive ?? false);
            }
        }
    }
}
