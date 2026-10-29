using System.Globalization;
using System.Security.Claims;
using UserRole = UluBilge.Core.Enums.RoleEnum;

namespace UluBilge.Api.Authentication
{
    public static class AuthenticationClaims
    {
        public const string RoleEnum = "RoleEnum";
        public const string SecurityStamp = "security_stamp";
        public const string SessionId = "sid";

        private static readonly UserRole KnownRoles = Enum.GetValues<UserRole>()
            .Aggregate(UserRole.None, (mask, role) => mask | role);

        public static bool IsSupportedRole(UserRole roles) => (roles & ~KnownRoles) == 0;

        public static bool TryGetRole(ClaimsPrincipal? principal, out UserRole roles)
        {
            roles = default;
            var value = SingleValue(principal, RoleEnum);
            if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var mask))
            {
                return false;
            }

            roles = (UserRole)mask;
            return IsSupportedRole(roles);
        }

        public static bool TryGetSession(ClaimsPrincipal? principal, out UserSession session)
        {
            session = default!;
            var name = SingleValue(principal, "name");
            if (!int.TryParse(SingleValue(principal, "sub"), NumberStyles.None, CultureInfo.InvariantCulture, out var userId)
                || userId <= 0 || string.IsNullOrWhiteSpace(name) || name.Length > 64
                || !TryGetRole(principal, out var roles)
                || !Guid.TryParse(SingleValue(principal, SecurityStamp), out var stamp) || stamp == Guid.Empty
                || !Guid.TryParse(SingleValue(principal, SessionId), out var familyId) || familyId == Guid.Empty)
            {
                return false;
            }

            session = new UserSession(new AuthenticatedUser(userId, name, roles), stamp, familyId);
            return true;
        }

        private static string? SingleValue(ClaimsPrincipal? principal, string claimType)
        {
            var claims = principal?.FindAll(claimType).ToArray();
            return claims is { Length: 1 } ? claims[0].Value : null;
        }
    }

    public sealed record UserSession(AuthenticatedUser User, Guid SecurityStamp, Guid FamilyId);
}
