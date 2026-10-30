using System.Globalization; // Sayisal claimleri kulturden bagimsiz bicimlendirir.
using System.Security.Claims; // OAuth tokenine yazilacak kullanici kimligini olusturur.
using System.Text; // Guvenli giris formunun HTML icerigini hazirlar.
using System.Text.Encodings.Web; // Formdaki dis girdileri HTML olarak kodlar.
using Microsoft.AspNetCore; // OpenIddict istegini HttpContext uzerinden almayi saglar.
using Microsoft.AspNetCore.Antiforgery; // Giris formunun sahte istek korumasini kullanir.
using Microsoft.AspNetCore.Authentication; // OpenIddict kod ve token dogrulama sonucunu alir.
using Microsoft.AspNetCore.Authorization; // Giris ve token degisim adreslerini anonim cagrilabilir yapar.
using Microsoft.AspNetCore.Mvc; // OAuth HTTP yanitlarini uretir.
using Microsoft.AspNetCore.RateLimiting; // Giris ve token degisimi icin hiz sinirlarini uygular.
using OpenIddict.Abstractions; // OAuth istek, scope ve claim yardimcilarini kullanir.
using OpenIddict.Server.AspNetCore; // OAuth yanitlarini OpenIddict isleyicisine iletir.
using UluBilge.Api.Authentication; // Mevcut kullanici ve oturum dogrulamasini kullanir.
using UluBilge.Api.Authentication.OAuth; // OAuth sunucusunun sabit ayarlarini kullanir.
using static OpenIddict.Abstractions.OpenIddictConstants; // Standart OAuth hata ve claim adlarini kullanir.

namespace UluBilge.Api.Controllers; // OAuth uc noktalarini API controller duzeninde tutar.

[AllowAnonymous] // OAuth protokolunun kendi kimlik dogrulamasi oncesinde adreslere ulasilmasini saglar.
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)] // Giris formu ve OAuth yanitlarinin onbellege alinmasini engeller.
public sealed class OAuthController(IUserAuthenticationService authentication, IOpenIddictApplicationManager applications, IAntiforgery antiforgery, OAuthServerSettings settings) : Controller // OAuth protokolunu mevcut kullanici sistemiyle baglar.
{ // OAuth controllerini baslatir.
    private static readonly HashSet<string> AuthorizationFields = ["client_id", "redirect_uri", "response_type", "response_mode", "scope", "state", "code_challenge", "code_challenge_method", "resource", "prompt", "nonce", "max_age"]; // Giris formunda korunabilecek OAuth alanlarini sinirlar.

    [HttpGet("~/oauth/authorize")] // OpenIddict'in dogruladigi yetkilendirme istegi icin giris formunu acar.
    public async Task<IActionResult> AuthorizeAsync(CancellationToken cancellationToken) // Istemciye veri erisim izni vermek icin kullanicidan giris ister.
    { // Giris formu akisini baslatir.
        var request = GetRequest(); // OpenIddict'in dogruladigi protokol istegini alir.
        if (!IsSupportedTarget(request) || !request.HasScope(OAuthServerSettings.Scope)) return OAuthError(Errors.InvalidScope, "Gecerli MCP kaynagi ve mcp:tools izni gereklidir."); // Baska kaynak veya izne kod verilmesini engeller.
        if (request.HasPromptValue(PromptValues.None)) return OAuthError(Errors.LoginRequired, "Kullanici girisi gereklidir."); // Etkilesimsiz istekte mevcut tarayici oturumu olmadigini bildirir.
        return await LoginFormAsync(request, null, cancellationToken); // Kayitli istemciyi gosteren guvenli giris formunu dondurur.
    } // Giris formu akisini tamamlar.

    [HttpPost("~/oauth/authorize")] // Giris ve kullanici iznini ayni dogrulanmis OAuth isteginde tamamlar.
    [EnableRateLimiting("auth-login")] // Mevcut parola denemesi hiz sinirlarini OAuth girisine de uygular.
    [RequestSizeLimit(8192)] // Giris formunun boyutunu sinirlar.
    public async Task<IActionResult> AuthorizeAsync([FromForm] string? userName, [FromForm] string? password, CancellationToken cancellationToken) // Mevcut parola dogrulamasi ile OAuth kodu uretir.
    { // OAuth giris islemini baslatir.
        var request = GetRequest(); // OpenIddict'in istemci, callback ve PKCE denetiminden gecen istegini alir.
        if (!await antiforgery.IsRequestValidAsync(HttpContext)) return BadRequest(new { error = Errors.InvalidRequest, error_description = "Giris formu dogrulanamadi; sayfayi yeniden acin." }); // MVC gorunum altyapisina gerek duymadan sahte giris isteklerini reddeder.
        if (!IsSupportedTarget(request) || !request.HasScope(OAuthServerSettings.Scope)) return OAuthError(Errors.InvalidScope, "Gecerli MCP kaynagi ve mcp:tools izni gereklidir."); // Formda kaynak veya scope degistirilmesini engeller.
        if (string.IsNullOrWhiteSpace(userName) || userName.Length > 64 || string.IsNullOrEmpty(password) || password.Length > 128) return await LoginFormAsync(request, "Kullanici adi veya parola gecersiz.", cancellationToken); // Hatali boyuttaki girisleri veritabanina iletmez.
        var session = await authentication.LoginForOAuthAsync(userName, password, cancellationToken); // Ayni parola, hesap kilitleme ve kalici oturum kurallariyla giris yapar.
        if (session is null) return await LoginFormAsync(request, "Kullanici adi veya parola gecersiz.", cancellationToken); // Hatali giriste kod vermeden ayni formu gosterir.
        var identity = new ClaimsIdentity(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme, Claims.Name, AuthenticationClaims.RoleEnum); // Dogrulanmis kullanicinin OAuth kimligini olusturur.
        identity.SetClaim(Claims.Subject, session.User.Id.ToString(CultureInfo.InvariantCulture)); // Tokenin sahibini mevcut kullanici numarasiyla belirtir.
        identity.SetClaim(Claims.Name, session.User.UserName); // Mevcut kullanici adini tokena ekler.
        identity.SetClaim(AuthenticationClaims.RoleEnum, ((long)session.User.RoleEnum).ToString(CultureInfo.InvariantCulture)); // Rol maskesini mevcut yetkilendirme denetimiyle uyumlu tutar.
        identity.SetClaim(AuthenticationClaims.SecurityStamp, session.SecurityStamp.ToString("D")); // Parola ve hesap degisikliklerinde oturumun iptal edilebilmesini saglar.
        identity.SetClaim(AuthenticationClaims.SessionId, session.FamilyId.ToString("D")); // OAuth tokenini veritabanindaki oturum ailesine baglar.
        var principal = new ClaimsPrincipal(identity); // Token uretimine verilecek dogrulanmis kimligi hazirlar.
        principal.SetScopes(request.GetScopes()); // Yalnizca kullanicinin formda izin verdigi scope degerlerini korur.
        principal.SetResources(settings.Resource.AbsoluteUri); // Tokenin hedefini bu MCP sunucusuyla sinirlar.
        foreach (var claim in principal.Claims) claim.SetDestinations(Destinations.AccessToken); // Gerekli kimlik claimlerinin erisim tokenina yazilmasini saglar.
        return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme); // Tek kullanimlik kodu OpenIddict'e urettirip kayitli callback adresine yonlendirir.
    } // OAuth giris islemini tamamlar.

    [HttpPost("~/oauth/token")] // OAuth kodu ve refresh token degisimi adresini tanimlar.
    [Consumes("application/x-www-form-urlencoded")] // OAuth standardinin form veri bicimini kabul eder.
    [EnableRateLimiting("oauth-token")] // Token isteklerine ayri hiz siniri uygular.
    [RequestSizeLimit(8192)] // Token isteginin boyutunu sinirlar.
    public async Task<IActionResult> TokenAsync(CancellationToken cancellationToken) // Kod veya yenileme tokeni karsiliginda standart OAuth token yaniti uretir.
    { // Token degisimini baslatir.
        var request = GetRequest(); // OpenIddict tarafindan ayrisirilmis token istegini alir.
        if (!request.IsAuthorizationCodeGrantType() && !request.IsRefreshTokenGrantType()) return OAuthError(Errors.UnsupportedGrantType, "Bu token akisina izin verilmiyor."); // Desteklenmeyen token akislarini reddeder.
        if (!IsSupportedTarget(request)) return OAuthError(Errors.InvalidTarget, "Token yalnizca bu MCP kaynagi icin kullanilabilir."); // Tokenin farkli bir kaynaga yonlendirilmesini engeller.
        var result = await HttpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme); // Kodun veya refresh tokenin OpenIddict dogrulama sonucunu alir.
        if (result.Principal is not { } principal || !AuthenticationClaims.TryGetSession(principal, out var session) // Dogrulanmis tokenin mevcut oturum claimlerini denetler.
            || !await authentication.IsSessionValidAsync(session, cancellationToken)) return OAuthError(Errors.InvalidGrant, "Oturum artik gecerli degil; yeniden giris yapin."); // Kapanmis veya degismis kullanici oturumuna token verilmesini engeller.
        if (!string.IsNullOrEmpty(request.Scope)) principal.SetScopes(request.GetScopes()); // Yenilemede talep edilen daha dar izinleri uygular.
        if (!principal.HasScope(OAuthServerSettings.Scope)) return OAuthError(Errors.InvalidScope, "mcp:tools izni gereklidir."); // MCP izni olmayan bir token uretilmesini engeller.
        principal.SetResources(settings.Resource.AbsoluteUri); // Token yenilenirken de ayni MCP audience bilgisini korur.
        return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme); // Token omru, kodun tek kullanimi ve refresh rotation islemlerini OpenIddict'e birakir.
    } // Token degisimini tamamlar.

    private OpenIddictRequest GetRequest() => HttpContext.GetOpenIddictServerRequest() ?? throw new InvalidOperationException("OAuth istegi bulunamadi."); // Yalnizca protokol isleyicisinden gecen istekleri kullanir.

    private bool IsSupportedTarget(OpenIddictRequest request) => request.GetResources().All(resource => resource == settings.Resource.AbsoluteUri); // Resource verilmisse bu MCP sunucusuyla tam eslesmesini ister.

    private ForbidResult OAuthError(string error, string description) => Forbid(new AuthenticationProperties(new Dictionary<string, string?> // Hatalari standart OAuth yanitina donusturur.
    { // OAuth hata alanlarini baslatir.
        [OpenIddictServerAspNetCoreConstants.Properties.Error] = error, // Protokol hata kodunu belirtir.
        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = description // Istemciye guvenli bir hata aciklamasi verir.
    }), OpenIddictServerAspNetCoreDefaults.AuthenticationScheme); // Hata yanitini OpenIddict ile uretir.

    private async Task<ContentResult> LoginFormAsync(OpenIddictRequest request, string? error, CancellationToken cancellationToken) // Giris ve istemci izni formunu HTML kodlamasiyla hazirlar.
    { // Form uretimini baslatir.
        var application = await applications.FindByClientIdAsync(request.ClientId!, cancellationToken) ?? throw new InvalidOperationException("OAuth istemcisi bulunamadi."); // Onceden dogrulanmis istemcinin kaydini alir.
        var clientName = await applications.GetDisplayNameAsync(application, cancellationToken) ?? request.ClientId!; // Kullaniciya hangi istemciye izin verdigini gosterir.
        var tokens = antiforgery.GetAndStoreTokens(HttpContext); // Forma ve cookieye eslestirilecek sahte istek korumasini olusturur.
        var html = new StringBuilder("<!doctype html><html lang=\"tr\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><title>UluBilge MCP Giris</title></head><body><main><h1>UluBilge MCP</h1>"); // Giris sayfasinin temel yapisini olusturur.
        html.Append("<p><strong>").Append(HtmlEncoder.Default.Encode(clientName)).Append("</strong> uygulamasinin urun satis raporlariniza erismesine izin vermek icin giris yapin.</p>"); // Istemci adini HTML enjeksiyonu olmadan gosterir.
        if (error is not null) html.Append("<p role=\"alert\">").Append(HtmlEncoder.Default.Encode(error)).Append("</p>"); // Hatali giris mesajini guvenli bicimde gosterir.
        html.Append("<form method=\"post\" action=\"").Append(HtmlEncoder.Default.Encode(Url.Content("~/oauth/authorize"))).Append("\">"); // Formu yalnizca yerel OAuth giris adresine gonderir.
        var fields = Request.HasFormContentType ? (IEnumerable<KeyValuePair<string, Microsoft.Extensions.Primitives.StringValues>>)Request.Form : Request.Query; // Ilk veya tekrar gosterilen formun protokol alanlarini alir.
        foreach (var field in fields.Where(field => AuthorizationFields.Contains(field.Key))) // Yalnizca izin verilen OAuth alanlarini forma tasir.
        { // Gizli protokol alanlarini uretir.
            foreach (var value in field.Value) AppendHidden(html, field.Key, value ?? string.Empty); // Cok degerli alanlari kaybetmeden ve HTML kodlayarak korur.
        } // Gizli protokol alanlarini tamamlar.
        AppendHidden(html, tokens.FormFieldName, tokens.RequestToken!); // Sahte istek koruma degerini forma ekler.
        html.Append("<p><label>Kullanici adi <input name=\"userName\" autocomplete=\"username\" maxlength=\"64\" required></label></p><p><label>Parola <input name=\"password\" type=\"password\" autocomplete=\"current-password\" maxlength=\"128\" required></label></p><button type=\"submit\">Giris yap ve izin ver</button></form></main></body></html>"); // Kullanici kimligi ve acik izin icin form alanlarini ekler.
        Response.Headers.CacheControl = "no-store"; // Giris iceriginin onbellege alinmasini engeller.
        Response.Headers.Pragma = "no-cache"; // Eski istemciler icin onbellek engelini belirtir.
        Response.Headers["Content-Security-Policy"] = "default-src 'none'; form-action 'self'; frame-ancestors 'none'; base-uri 'none'"; // Formun baska adrese gonderilmesini ve sayfanin cerceveye alinmasini engeller.
        Response.Headers["X-Content-Type-Options"] = "nosniff"; // Tarayicinin icerik turunu degistirerek yorumlamasini engeller.
        return Content(html.ToString(), "text/html; charset=utf-8"); // Giris formunu tarayiciya dondurur.
    } // Form uretimini tamamlar.

    private static void AppendHidden(StringBuilder html, string name, string value) => html.Append("<input type=\"hidden\" name=\"").Append(HtmlEncoder.Default.Encode(name)).Append("\" value=\"").Append(HtmlEncoder.Default.Encode(value)).Append("\">"); // Gizli alan adini ve degerini HTML enjeksiyonuna karsi kodlar.
} // OAuth controllerini tamamlar.
