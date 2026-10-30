using System.Security.Cryptography; // Tahmin edilemeyen istemci kimlikleri uretir.
using System.Text.Json.Serialization; // OAuth kayit alanlarini standart JSON adlariyla esler.
using Microsoft.AspNetCore.Authorization; // Ilk istemci kaydinin tokensiz yapilabilmesini saglar.
using Microsoft.AspNetCore.Mvc; // Istemci kayit isteklerini ve yanitlarini yonetir.
using Microsoft.AspNetCore.RateLimiting; // Dinamik kayit isteklerine hiz siniri uygular.
using Microsoft.AspNetCore.WebUtilities; // Istemci kimligini URL guvenli bicimde kodlar.
using OpenIddict.Abstractions; // Kalici OAuth istemci yonetimini kullanir.
using UluBilge.Api.Authentication.OAuth; // OAuth izinlerini ve kayit ayarini kullanir.
using static OpenIddict.Abstractions.OpenIddictConstants; // OAuth istemci izin sabitlerini kullanir.

namespace UluBilge.Api.Controllers; // Dinamik istemci kaydini API katmaninda tutar.

[AllowAnonymous] // Henuz tokeni olmayan MCP istemcisinin kaydolabilmesini saglar.
public sealed class OAuthRegistrationController(IOpenIddictApplicationManager applications, OAuthServerSettings settings, TimeProvider timeProvider) : ControllerBase // Dinamik MCP istemci kaydini kalici OpenIddict deposuna baglar.
{ // Istemci kayit controllerini baslatir.
    [HttpPost("~/oauth/register")] // Standart OAuth dinamik kayit adresini tanimlar.
    [Consumes("application/json")] // Istemci metadata bilgisini JSON olarak kabul eder.
    [EnableRateLimiting("oauth-register")] // Gereksiz istemci kayitlarini hiz siniriyla azaltir.
    [RequestSizeLimit(8192)] // Kayit isteginin boyutunu sinirlar.
    public async Task<IActionResult> RegisterAsync([FromBody] RegistrationRequest? request, CancellationToken cancellationToken) // MCP istemcisinin callback adreslerini kalici olarak kaydeder.
    { // Dinamik istemci kaydini baslatir.
        Response.Headers.CacheControl = "no-store"; // Kayit yanitinin onbellege alinmasini engeller.
        if (!settings.AllowDynamicClientRegistration) return NotFound(); // Yapilandirmada kapali olan dinamik kayit adresini yayinlamaz.
        if (!ModelState.IsValid || request?.RedirectUris is not { Length: > 0 and <= 5 } || request.ClientName?.Length > 128 // Kayit modelini ve callback sayisini denetler.
            || request.TokenEndpointAuthMethod is not (null or "none" or "client_secret_post" or "client_secret_basic") // Yalnizca desteklenen public ve confidential kimlik yontemlerini kabul eder.
            || request.GrantTypes is { } grants && (!grants.Contains(GrantTypes.AuthorizationCode) || grants.Any(grant => grant is not (GrantTypes.AuthorizationCode or GrantTypes.RefreshToken))) // Desteklenmeyen OAuth akislarini kaydetmez.
            || request.ResponseTypes is { } responses && (responses.Length != 1 || responses[0] != ResponseTypes.Code)) // Yalnizca authorization code yanitini kabul eder.
        { // Gecersiz metadata durumunu ele alir.
            return BadRequest(new { error = "invalid_client_metadata", error_description = "Gecerli callback adresleri ve authorization_code/PKCE istemcisi gereklidir." }); // Standart kayit hatasini dondurur.
        } // Metadata denetimini tamamlar.
        var authMethod = request.TokenEndpointAuthMethod ?? ClientAuthenticationMethods.ClientSecretBasic; // Yontem belirtilmezse OAuth standardinin Basic varsayilanini kullanir.
        var clientSecret = authMethod == ClientAuthenticationMethods.None ? null : WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32)); // Secret isteyen istemci icin guclu ve rastgele bir anahtar uretir.
        var descriptor = new OpenIddictApplicationDescriptor // Sunucunun izin verecegi istemci kaydini hazirlar.
        { // Istemci alanlarini baslatir.
            ClientId = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32)), // Rastgele ve benzersiz istemci kimligi uretir.
            ClientType = clientSecret is null ? ClientTypes.Public : ClientTypes.Confidential, // Istemci turunu kayitta istenen kimlik yontemine gore belirler.
            ClientSecret = clientSecret, // Secret'i OpenIddict'in hashleyerek saklamasi icin yoneticiye verir.
            ConsentType = ConsentTypes.Explicit, // Kullaniciya giris formunda acik izin adimini uygular.
            DisplayName = string.IsNullOrWhiteSpace(request.ClientName) ? "MCP client" : request.ClientName.Trim() // Kullaniciya gosterilecek istemci adini saklar.
        }; // Istemci alanlarini tamamlar.
        foreach (var value in request.RedirectUris) // Her callback adresini kaydetmeden once denetler.
        { // Callback denetimini baslatir.
            if (string.IsNullOrWhiteSpace(value) || value.Length > 2048 || !Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Fragment.Length != 0 || uri.UserInfo.Length != 0 // Callback adresindeki hatali veya tehlikeli kisimlari reddeder.
                || (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback))) // HTTPS veya yerel masaustu callback adresi gerektirir.
            { // Gecersiz callback durumunu ele alir.
                return BadRequest(new { error = "invalid_redirect_uri", error_description = "Callback, fragment ve kullanici bilgisi icermeyen HTTPS veya loopback HTTP adresi olmalidir." }); // Gecersiz callback'i kaydetmeden hata dondurur.
            } // Callback adresi denetimini tamamlar.
            descriptor.RedirectUris.Add(uri); // Daha sonra tam eslesmeyle dogrulanacak callback adresini kaydeder.
        } // Callback listesinin denetimini tamamlar.
        descriptor.Permissions.UnionWith([Permissions.Endpoints.Authorization, Permissions.Endpoints.Token, Permissions.Endpoints.Revocation, Permissions.GrantTypes.AuthorizationCode, Permissions.GrantTypes.RefreshToken, Permissions.ResponseTypes.Code, Permissions.Prefixes.Scope + OAuthServerSettings.Scope]); // Istemci izinlerini yalnizca MCP kod, yenileme ve iptal akisiyla sinirlar.
        descriptor.Requirements.Add(Requirements.Features.ProofKeyForCodeExchange); // Istemci kaydinda da PKCE zorunlulugunu saklar.
        descriptor.AddResourcePermissions(settings.Resource.AbsoluteUri); // Istemcinin yalnizca bu MCP kaynagi icin token istemesine izin verir.
        await applications.CreateAsync(descriptor, cancellationToken); // Istemciyi ve callback adreslerini kalici OAuth veritabanina yazar.
        var response = new Dictionary<string, object?> // Standart dinamik kayit yanitini olusturur.
        { // Kayit yaniti alanlarini baslatir.
            ["client_id"] = descriptor.ClientId, // Yeni istemcinin kimligini dondurur.
            ["client_id_issued_at"] = timeProvider.GetUtcNow().ToUnixTimeSeconds(), // Kayit zamanini Unix saniyesi olarak bildirir.
            ["client_name"] = descriptor.DisplayName, // Kaydedilmis gorunen adi dondurur.
            ["redirect_uris"] = descriptor.RedirectUris.Select(uri => uri.AbsoluteUri).ToArray(), // Sunucunun kabul ettigi callback adreslerini dondurur.
            ["grant_types"] = new[] { GrantTypes.AuthorizationCode, GrantTypes.RefreshToken }, // Desteklenen token akislarini bildirir.
            ["response_types"] = new[] { ResponseTypes.Code }, // Authorization code yanit turunu bildirir.
            ["token_endpoint_auth_method"] = authMethod, // Kayitli istemcinin gercek kimlik dogrulama yontemini bildirir.
            ["scope"] = OAuthServerSettings.Scope + " " + Scopes.OfflineAccess // Istemcinin isteyebilecegi MCP ve yenileme izinlerini bildirir.
        }; // Kayit yaniti alanlarini tamamlar.
        if (clientSecret is not null) // Yalnizca confidential istemci icin secret yaniti hazirlar.
        { // Gizli anahtar yanitini baslatir.
            response["client_secret"] = clientSecret; // Secret'i yalnizca ilk kayit yanitinda istemciye iletir.
            response["client_secret_expires_at"] = 0; // Secret'in otomatik bitis tarihi olmadigini standart bicimde bildirir.
        } // Gizli anahtar yanitini tamamlar.
        return StatusCode(StatusCodes.Status201Created, response); // Dinamik istemci kaydinin tamamlandigini bildirir.
    } // Dinamik istemci kaydini tamamlar.

    public sealed class RegistrationRequest // Standart OAuth kayit JSON alanlarini tasir.
    { // Kayit istek modelini baslatir.
        [JsonPropertyName("client_name")] public string? ClientName { get; set; } // Istemcinin kullaniciya gosterilecek adini alir.
        [JsonPropertyName("redirect_uris")] public string[]? RedirectUris { get; set; } // Izin verilecek callback adreslerini alir.
        [JsonPropertyName("grant_types")] public string[]? GrantTypes { get; set; } // Istemcinin istedigi token akislarini alir.
        [JsonPropertyName("response_types")] public string[]? ResponseTypes { get; set; } // Istemcinin bekledigi authorization yanit turunu alir.
        [JsonPropertyName("token_endpoint_auth_method")] public string? TokenEndpointAuthMethod { get; set; } // Istemcinin token uc noktasi kimlik dogrulama yontemini alir.
    } // Kayit istek modelini tamamlar.
} // Istemci kayit controllerini tamamlar.
