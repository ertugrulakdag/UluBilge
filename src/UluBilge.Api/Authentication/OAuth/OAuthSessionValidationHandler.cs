using OpenIddict.Abstractions; // OAuth scope ve claim yardimcilarini kullanir.
using OpenIddict.Validation; // Dogrulanmis OAuth token olaylarini kullanir.

namespace UluBilge.Api.Authentication.OAuth; // OAuth oturum denetimini ayri tutar.

public sealed class OAuthSessionValidationHandler(IUserAuthenticationService authentication) : IOpenIddictValidationHandler<OpenIddictValidationEvents.ProcessAuthenticationContext> // OAuth tokenini mevcut kullanici oturumuna baglar.
{ // Oturum denetleyicisini baslatir.
    public async ValueTask HandleAsync(OpenIddictValidationEvents.ProcessAuthenticationContext context) // Imzasi dogrulanmis erisim tokeninin oturumunu denetler.
    { // Token oturum denetimini baslatir.
        if (context.AccessTokenPrincipal is not { } principal) return; // Token yoksa varsayilan OpenIddict sonucunu korur.
        if (!principal.HasScope(OAuthServerSettings.Scope) || !AuthenticationClaims.TryGetSession(principal, out var session) // Gerekli MCP iznini ve oturum claimlerini denetler.
            || !await authentication.IsSessionValidAsync(session, context.CancellationToken)) // Hesap ve oturumun veritabaninda halen gecerli oldugunu dogrular.
        { // Gecersiz oturum durumunu ele alir.
            context.Reject(OpenIddictConstants.Errors.InvalidToken, "Oturum veya MCP erisim izni artik gecerli degil."); // Gecersiz tokenin kullanilmasini engeller.
        } // Gecersiz oturum denetimini tamamlar.
    } // Token oturum denetimini tamamlar.
} // Oturum denetleyicisini tamamlar.
