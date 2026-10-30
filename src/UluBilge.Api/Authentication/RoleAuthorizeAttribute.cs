using Microsoft.AspNetCore.Authorization; // Standart rol yetkilendirmesini kullanir.
using UluBilge.Core.Enums; // Rol enum degerlerini kullanir.

namespace UluBilge.Api.Authentication; // Rol etiketini kimlik dogrulama altinda tutar.

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)] // Controller ve metotlarda rol etiketi kullanilmasini saglar.
public sealed class RoleAuthorizeAttribute : AuthorizeAttribute // Ozel handler yerine yerlesik rol denetimini kullanir.
#if DEBUG // Kullanicinin istedigi Debug davranisini korur.
    , IAllowAnonymous // Debug derlemesinde giris ve rol sarti aramaz.
#endif // Debug istisnasini tamamlar.
{ // Rol etiketini baslatir.
    public RoleAuthorizeAttribute(params RoleEnum[] roles) // Bir veya daha fazla rol kabul eder.
    { // Rol adlarini hazirlar.
        Roles = roles.Length == 0 ? nameof(RoleEnum.None) : string.Join(",", roles); // Rollerden herhangi birini kabul eder; bos liste erisim vermez.
    } // Rol adlarini hazirlamayi tamamlar.
} // Rol etiketini tamamlar.
