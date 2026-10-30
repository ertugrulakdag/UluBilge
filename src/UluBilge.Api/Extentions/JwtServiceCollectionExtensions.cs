using System.Security.Cryptography; // Gelistirmede rastgele imzalama anahtari uretir.
using System.Threading.RateLimiting; // Giris denemelerini IP bazinda sinirlar.
using Microsoft.AspNetCore.Authentication.JwtBearer; // Yerlesik JWT dogrulayicisini kullanir.
using Microsoft.AspNetCore.Authorization; // Varsayilan giris zorunlulugunu ayarlar.
using Microsoft.AspNetCore.RateLimiting; // Giris hiz siniri politikasini kaydeder.
using Microsoft.IdentityModel.Tokens; // JWT imza ve dogrulama ayarlarini kullanir.
using UluBilge.Api.Authentication; // Sade kimlik dogrulama servisini kaydeder.

namespace UluBilge.Api.Extentions; // Servis kaydini mevcut extension duzeninde tutar.

public static class JwtServiceCollectionExtensions // JWT ve temel korumalari tek yerde yapilandirir.
{ // JWT servis kaydini baslatir.
    public static IServiceCollection AddApiAuthentication(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment) // Giris ve Bearer token dogrulamasini kaydeder.
    { // Kimlik dogrulama ayarlarini baslatir.
        var settings = configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions(); // Mevcut JWT yapilandirmasini okur.
        if (string.IsNullOrWhiteSpace(settings.Issuer) || string.IsNullOrWhiteSpace(settings.Audience) || settings.AccessTokenLifetimeMinutes is < 1 or > 15) // Gerekli alanlari ve kisa token omrunu denetler.
            throw new InvalidOperationException("Jwt:Issuer, Jwt:Audience ve 1-15 dakika araliginda token suresi gereklidir."); // Hatali ayarlarla baslamayi engeller.
        if (settings.SigningKeyBase64 is null && environment.IsDevelopment()) settings.SigningKeyBase64 = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)); // Yalnizca gelistirmede gecici guclu anahtar kullanir.
        byte[] keyBytes; // Dogrulanacak imzalama anahtarini tutar.
        try { keyBytes = Convert.FromBase64String(settings.SigningKeyBase64 ?? string.Empty); } // Base64 anahtar ayarini okur.
        catch (FormatException) { throw new InvalidOperationException("Jwt:SigningKeyBase64 gecerli Base64 olmalidir."); } // Hatali anahtari degerini gostermeden reddeder.
        if (keyBytes.Length < 32) throw new InvalidOperationException("Jwt:SigningKeyBase64 en az 32 rastgele bayt icermelidir."); // Eksik veya zayif anahtarla baslamayi engeller.
        var key = new SymmetricSecurityKey(keyBytes); // Token uretimi ve dogrulamasinin ortak anahtarini hazirlar.
        services.AddSingleton(settings); // JWT ayarlarini servise verir.
        services.AddSingleton(new SigningCredentials(key, SecurityAlgorithms.HmacSha256)); // Token uretimi icin HS256 imzasini kaydeder.
        services.AddScoped<AuthService>(); // Tek giris ve token servisini kaydeder.
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options => // JWT dogrulamasini yerlesik middleware'e birakir.
        { // JWT dogrulama ayarlarini baslatir.
            options.MapInboundClaims = false; // sub, name ve role alanlarinin adlarini korur.
            options.IncludeErrorDetails = false; // Token ayrintilarini hata yanitinda paylasmaz.
            options.TokenValidationParameters = new TokenValidationParameters // Gerekli guvenlik kontrollerini ayarlar.
            { // Token dogrulama alanlarini baslatir.
                ValidateIssuer = true, ValidIssuer = settings.Issuer, // Tokeni beklenen sunucunun vermis olmasini ister.
                ValidateAudience = true, ValidAudience = settings.Audience, // Tokenin bu API icin verilmis olmasini ister.
                ValidateIssuerSigningKey = true, IssuerSigningKey = key, // Gizli anahtarla token imzasini dogrular.
                ValidateLifetime = true, RequireExpirationTime = true, // Suresi dolan veya bitis zamani olmayan tokeni reddeder.
                RequireSignedTokens = true, ValidAlgorithms = [SecurityAlgorithms.HmacSha256], // Imzasiz ve beklenmeyen algoritmali tokenlari reddeder.
                ClockSkew = TimeSpan.FromSeconds(15), // Saat farki toleransini kisa tutar.
                NameClaimType = "name", RoleClaimType = AuthService.RoleClaimType // Kullanici adini ve standart rol denetimini esler.
            }; // Token dogrulama alanlarini tamamlar.
        }); // JWT dogrulama ayarlarini tamamlar.
        services.AddAuthorization(options => options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build()); // Acikca anonim olmayan adreslerde giris ister.
        services.AddRateLimiter(options => // Giris denemelerine temel hiz siniri uygular.
        { // Giris hiz sinirini baslatir.
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests; // Sinir asildiginda 429 yaniti verir.
            options.AddPolicy("auth-login", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 })); // IP basina dakikada en fazla bes giris denemesine izin verir.
        }); // Giris hiz sinirini tamamlar.
        return services; // Servis kayit zincirini surdurur.
    } // Kimlik dogrulama ayarlarini tamamlar.
} // JWT servis kaydini tamamlar.
