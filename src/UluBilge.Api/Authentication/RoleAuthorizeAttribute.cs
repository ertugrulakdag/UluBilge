using Microsoft.AspNetCore.Authorization;
using UluBilge.Core.Enums;

namespace UluBilge.Api.Authentication
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
    public sealed class RoleAuthorizeAttribute(RoleEnum roles) : AuthorizeAttribute, IAuthorizationRequirementData
    {
        public IEnumerable<IAuthorizationRequirement> GetRequirements()
        {
            yield return new RoleRequirement(roles);
        }
    }

    internal sealed record RoleRequirement(RoleEnum Roles) : IAuthorizationRequirement;

    internal sealed class RoleAuthorizationHandler : AuthorizationHandler<RoleRequirement>
    {
        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, RoleRequirement requirement)
        {
            if (context.User.Identity?.IsAuthenticated == true
                && requirement.Roles != RoleEnum.None
                && AuthenticationClaims.IsSupportedRole(requirement.Roles)
                && AuthenticationClaims.TryGetRole(context.User, out var roles)
                && (roles & requirement.Roles) == requirement.Roles)
            {
                context.Succeed(requirement);
            }

            return Task.CompletedTask;
        }
    }
}
