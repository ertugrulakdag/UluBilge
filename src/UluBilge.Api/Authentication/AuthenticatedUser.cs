using UluBilge.Core.Enums;

namespace UluBilge.Api.Authentication
{
    public sealed record AuthenticatedUser(int Id, string UserName, RoleEnum RoleEnum);
}
