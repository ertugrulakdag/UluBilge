using Microsoft.AspNetCore.Authorization;
using UluBilge.Core.Enums;
/*
 * AuthorizeAttribute: ASP.NET Core’un yetkilendirme sistemine katılmasını sağlar.
 * IAuthorizationRequirementData: Gerekli rol bilgisini GetRequirements() üzerinden yetkilendirme sistemine verir.
 * roles.Aggregate(...): Verilen rolleri bit düzeyinde | işlemiyle birleştirir. Örneğin Admin = 1, Report = 2 ise sonuç 3 olur. Mevcut RoleAuthorizationHandler, kullanıcının bu rollerden en az birine sahip olup olmadığını kontrol eder.
 * IAllowAnonymous, ASP.NET Core’a “bu uç noktaya giriş yapmadan erişilebilir” bilgisini verir.
 * IAllowAnonymous, kimlik doğrulama middleware’ini kapatmaz. Gönderilen token işlenebilir; ancak kimlik veya rol sonucu bu uç noktaya erişimi engellemez.
 */
namespace UluBilge.Api.Authentication
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
    public sealed class RoleAuthorizeAttribute(params RoleEnum[] roles) : AuthorizeAttribute, IAuthorizationRequirementData
#if DEBUG
        , IAllowAnonymous
#endif
    {
        public IEnumerable<IAuthorizationRequirement> GetRequirements()
        {
            yield return new RoleRequirement(roles.Aggregate(RoleEnum.None, (mask, role) => mask | role));
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
                && (roles & requirement.Roles) != RoleEnum.None)
            {
                context.Succeed(requirement);
            }

            return Task.CompletedTask;
        }
    }
}
