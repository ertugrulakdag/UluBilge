using System.Security.Cryptography.X509Certificates; // Uretim ortamindaki imzalama ve sifreleme sertifikalarini yukler.
using System.Threading.RateLimiting; // OAuth isteklerine hiz siniri uygular.
using Microsoft.AspNetCore.Authentication.JwtBearer; // Mevcut API token semasini kullanir.
using Microsoft.AspNetCore.Http; // Cookie ve HTTP ayarlarini kullanir.
using Microsoft.EntityFrameworkCore; // OAuth icin SQLite veri saglayicisini kullanir.
using Microsoft.Data.Sqlite; // SQLite dosya yolunu guvenli baglanti dizesine cevirir.
using Microsoft.IdentityModel.JsonWebTokens; // Tokeni dogru dogrulayiciya yonlendirmek icin issuer bilgisini okur.
using ModelContextProtocol.AspNetCore.Authentication; // MCP kaynak metadata destegini kullanir.
using OpenIddict.Server; // OAuth sunucusu olaylarini yapilandirir.
using OpenIddict.Validation; // OAuth erisim tokeni dogrulamasini yapilandirir.
using OpenIddict.Validation.AspNetCore; // OAuth token semasini ASP.NET Core ile baglar.
using UluBilge.Api.Authentication.OAuth; // Uygulamanin OAuth ayar ve depo servislerini kullanir.
using static OpenIddict.Abstractions.OpenIddictConstants; // Standart OAuth sabitlerini kullanir.

namespace UluBilge.Api.Extentions; // Servis kayitlarini mevcut extension duzeninde tutar.

public static class OAuthServiceCollectionExtensions // OAuth sunucusu ve MCP kimlik dogrulamasini kaydeder.
{ // OAuth kayit sinifini baslatir.
    public static IServiceCollection AddUluBilgeOAuth(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment) // Mevcut JWT sistemine OAuth destegi ekler.
    { // OAuth kayitlarini baslatir.
        var issuerText = configuration["OAuth:Issuer"] ?? (environment.IsDevelopment() ? "https://localhost:7187/" : null); // Gelistirmede yerel adresi, diger ortamlarda yapilandirilmis adresi kullanir.
        if (!Uri.TryCreate(issuerText, UriKind.Absolute, out var issuer) || issuer.Query.Length != 0 || issuer.Fragment.Length != 0 || issuer.UserInfo.Length != 0 // Issuer adresinin sabit ve gecerli oldugunu denetler.
            || issuer.AbsolutePath != "/" || (issuer.Scheme != Uri.UriSchemeHttps && !(environment.IsDevelopment() && issuer.Scheme == Uri.UriSchemeHttp && issuer.IsLoopback))) // Uretimde HTTPS ve kok adres kullanilmasini zorunlu tutar.
        { // Gecersiz OAuth adresini ele alir.
            throw new InvalidOperationException("OAuth:Issuer, uygulamanin herkese acik HTTPS kok adresi olmalidir."); // Hatali kesif adresiyle uygulamanin baslamasini engeller.
        } // OAuth adresi denetimini tamamlar.
        var databasePath = Path.GetFullPath(configuration["OAuth:DatabasePath"] ?? "App_Data/oauth.db", environment.ContentRootPath); // OAuth dosyasinin tam yolunu belirler.
        var settings = new OAuthServerSettings(issuer, databasePath, configuration.GetValue("OAuth:AllowDynamicClientRegistration", true)); // Dogrulanmis ayarlari olusturur.
        services.AddSingleton(settings); // Ayni OAuth ayarlarini tum servislere verir.
        var connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString(); // Dosya yolundaki ozel karakterleri baglanti dizesi icin guvenli bicimde kodlar.
        services.AddDbContext<OAuthDbContext>(options => options.UseSqlite(connectionString).UseOpenIddict()); // OpenIddict kayitlarini is verilerinden ayri kalici depoya baglar.
        services.AddHostedService<OAuthDatabaseInitializer>(); // Sunucu acilirken OAuth tablolarini hazirlar.
        services.AddAntiforgery(options => // Tarayici giris formunu sahte isteklerden korur.
        { // Form korumasi ayarlarini baslatir.
            options.Cookie.Name = "UluBilge.OAuth.Antiforgery"; // OAuth formuna ait koruma cookiesini adlandirir.
            options.Cookie.HttpOnly = true; // Koruma cookiesinin betiklerden okunmasini engeller.
            options.Cookie.SecurePolicy = environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always; // Uretimde cookie aktarimini HTTPS ile sinirlar.
            options.Cookie.SameSite = SameSiteMode.Strict; // Cookiesinin siteler arasi form gonderiminde kullanilmasini engeller.
        }); // Form korumasi ayarlarini tamamlar.
        services.AddOpenIddict().AddCore(options => options.UseEntityFrameworkCore().UseDbContext<OAuthDbContext>()) // Kalici istemci, authorization code ve token yonetimini etkinlestirir.
            .AddServer(options => // OAuth authorization sunucusunu yapilandirir.
            { // Sunucu ayarlarini baslatir.
                options.SetIssuer(issuer); // Metadata ve tokenlarda ayni guvenilir issuer adresini kullanir.
                options.SetConfigurationEndpointUris("/.well-known/oauth-authorization-server", "/.well-known/openid-configuration"); // OAuth ve OpenID kesif adreslerini yayinlar.
                options.SetAuthorizationEndpointUris("/oauth/authorize"); // Tarayicidan kullanici izni alinacak adresi belirler.
                options.SetTokenEndpointUris("/oauth/token"); // Authorization code ve refresh token degisim adresini belirler.
                options.SetRevocationEndpointUris("/oauth/revoke"); // OAuth token iptali icin standart adresi acar.
                options.AllowAuthorizationCodeFlow().AllowRefreshTokenFlow(); // Yalnizca kodla giris ve token yenileme akislarini acar.
                options.Configure(server => // Masaustu ve sunucu tabanli MCP istemcilerinin kimlik yontemlerini yayinlar.
                { // Istemci kimlik yontemi ayarlarini baslatir.
                    server.ClientAuthenticationMethods.Clear(); // Bu uygulamada yonetilmeyen ozel anahtar yontemlerini kaldirir.
                    server.ClientAuthenticationMethods.UnionWith([ClientAuthenticationMethods.None, ClientAuthenticationMethods.ClientSecretPost, ClientAuthenticationMethods.ClientSecretBasic]); // Secretsiz PKCE ile guvenli client secret yontemlerini destekler.
                }); // Istemci kimlik yontemi ayarlarini tamamlar.
                options.RequireProofKeyForCodeExchange(); // Authorization code icin PKCE dogrulamasini zorunlu tutar.
                options.Configure(server => server.CodeChallengeMethods.Remove(CodeChallengeMethods.Plain)); // PKCE icin yalnizca S256 yontemini kabul eder.
                options.RegisterScopes(OAuthServerSettings.Scope, Scopes.OfflineAccess); // MCP erisim ve yenileme izinlerini yayinlar.
                options.RegisterResources(settings.Resource.AbsoluteUri); // Istemcinin resource parametresinde yalnizca bu MCP kaynagini isteyebilmesini saglar.
                options.SetAuthorizationCodeLifetime(TimeSpan.FromMinutes(5)); // Tek kullanimlik kodun omrunu bes dakika ile sinirlar.
                options.SetAccessTokenLifetime(TimeSpan.FromMinutes(configuration.GetValue("Jwt:AccessTokenLifetimeMinutes", 5))); // Erisim tokeni omrunu mevcut JWT ayariyla esler.
                options.SetRefreshTokenLifetime(TimeSpan.FromDays(configuration.GetValue("Jwt:RefreshTokenLifetimeDays", 7))); // Yenileme tokeni omrunu mevcut oturum ayariyla esler.
                options.SetRefreshTokenReuseLeeway(TimeSpan.Zero); // Kullanilmis refresh tokenin tekrar kullanilmasina tolerans tanimlamaz.
                options.DisableAccessTokenEncryption(); // Erisim tokenini standart imzali JWT olarak uretir.
                if (environment.IsDevelopment()) // Yerel gelistirme anahtarlarini ayarlar.
                { // Gelistirme sertifikasi kaydini baslatir.
                    options.AddDevelopmentSigningCertificate().AddDevelopmentEncryptionCertificate(); // Yeniden baslatmalarda korunan yerel sertifikalari kullanir.
                } // Gelistirme sertifikasi kaydini tamamlar.
                else // Uretim ortaminin sertifikalarini ayarlar.
                { // Uretim sertifikasi kaydini baslatir.
                    options.AddSigningCertificate(LoadCertificate(configuration, "Signing")); // Uretim tokenlarini yapilandirilmis imzalama sertifikasi ile imzalar.
                    options.AddEncryptionCertificate(LoadCertificate(configuration, "Encryption")); // Kod ve refresh tokenlarini ayri sifreleme sertifikasi ile korur.
                } // Uretim sertifikasi kaydini tamamlar.
                var host = options.UseAspNetCore().EnableAuthorizationEndpointPassthrough().EnableTokenEndpointPassthrough(); // Kullanici girisini uygulamaya, protokol dogrulamasini OpenIddict'e birakir.
                if (environment.IsDevelopment()) host.DisableTransportSecurityRequirement(); // Yalnizca yerel gelistirmede HTTP ile protokol testi yapilmasina izin verir.
                options.AddEventHandler<OpenIddictServerEvents.HandleConfigurationRequestContext>(handler => handler.UseInlineHandler(context => // Dinamik kayit adresini kesif belgesine ekler.
                { // Kesif belgesi ek alanlarini baslatir.
                    if (settings.AllowDynamicClientRegistration) context.Metadata["registration_endpoint"] = new Uri(issuer, "oauth/register").AbsoluteUri; // Dinamik kayit aciksa istemcilere adresini bildirir.
                    return default; // Kesif olayinin tamamlandigini bildirir.
                })); // Kesif belgesi ek alanlarini tamamlar.
            }) // OAuth sunucusu ayarlarini tamamlar.
            .AddValidation(options => // MCP'ye gelen OAuth tokenlarini dogrular.
            { // Token dogrulama ayarlarini baslatir.
                options.UseLocalServer(); // Yerel OAuth sunucusunun issuer ve anahtarlarini guvenilir kaynaktan alir.
                options.AddAudiences(settings.Resource.AbsoluteUri); // Tokenin yalnizca bu MCP kaynagi icin verilmis olmasini zorunlu tutar.
                options.EnableTokenEntryValidation(); // Iptal edilmis tokenlarin hemen reddedilmesi icin kalici kaydi denetler.
                options.UseAspNetCore(); // Dogrulamayi ASP.NET Core kimlik sistemine baglar.
                options.AddEventHandler<OpenIddictValidationEvents.ProcessAuthenticationContext>(handler => handler.UseScopedHandler<OAuthSessionValidationHandler>().SetOrder(int.MaxValue - 100_000)); // Imza dogrulamasindan sonra mevcut kullanici oturumunu da denetler.
            }); // OAuth token dogrulama ayarlarini tamamlar.
        services.AddOptions<OpenIddictServerOptions>().ValidateOnStart(); // Eksik sertifika ve protokol ayarlarini ilk istegi beklemeden uygulama baslangicinda denetler.
        services.AddAuthentication().AddPolicyScheme(OAuthServerSettings.BearerScheme, "MCP bearer", options => // OAuth ve mevcut API tokenlarini uygun dogrulayiciya yonlendirir.
        { // Token semasi secimini baslatir.
            options.ForwardDefaultSelector = context => // Tokenin guvenilmeyen issuer bilgisini yalnizca dogrulayici secimi icin okur.
            { // Yonlendirme secimini baslatir.
                var authorization = context.Request.Headers.Authorization.ToString(); // Bearer basligini alir.
                if (authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) // Bearer token varsa semasini belirler.
                { // Token turu secimini baslatir.
                    var token = authorization[7..].Trim(); // Basliktan token degerini ayirir.
                    if (token.Length <= 16_384 && new JsonWebTokenHandler().CanReadToken(token)) // Cok buyuk veya bozuk tokenlarin ayrisirilmasini engeller.
                    { // Okunabilir tokeni ele alir.
                        try // Bicin olarak JWT'ye benzeyen hatali icerigi guvenli bicimde ele alir.
                        { // Issuer okumasini baslatir.
                            if (new JsonWebToken(token).Issuer == issuer.AbsoluteUri) return OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme; // OAuth tokenini OpenIddict imza ve oturum denetimine gonderir.
                        } // Issuer okumasini tamamlar.
                        catch (Exception exception) when (exception is ArgumentException or Microsoft.IdentityModel.Tokens.SecurityTokenException) // Bozuk JWT'nin sunucu hatasina donusmesini engeller.
                        { // Hatali tokenin varsayilan dogrulayiciya birakilmasini saglar.
                            return JwtBearerDefaults.AuthenticationScheme; // Gecersiz tokenin standart 401 akisiyla reddedilmesini saglar.
                        } // Hatali token durumunu tamamlar.
                    } // Okunabilir token denetimini tamamlar.
                } // Token turu secimini tamamlar.
                return JwtBearerDefaults.AuthenticationScheme; // Diger tokenlari mevcut JWT dogrulayicisina gonderir.
            }; // Token dogrulayicisi secimini tamamlar.
        }).AddMcp(options => // MCP kaynak kesfini ve 401 basligini etkinlestirir.
        { // MCP kesif ayarlarini baslatir.
            options.ForwardAuthenticate = OAuthServerSettings.BearerScheme; // Gelen tokeni secilen dogrulayiciya iletir.
            options.ResourceMetadataUri = new Uri(issuer, ".well-known/oauth-protected-resource/mcp"); // Kaynak metadata adresini guvenilir yapilandirmadan olusturur.
            options.ResourceMetadata = new() // MCP kaynagini OAuth istemcilerine tanitir.
            { // Kaynak metadata alanlarini baslatir.
                Resource = settings.Resource.AbsoluteUri, // Tokenin hedef kaynak adresini bildirir.
                ResourceName = "UluBilge MCP", // Istemcide gorunecek kaynak adini belirler.
                AuthorizationServers = [issuer.AbsoluteUri], // Token verebilen OAuth sunucusunu bildirir.
                ScopesSupported = [OAuthServerSettings.Scope] // Kaynak icin gereken en dar izni bildirir.
            }; // Kaynak metadata alanlarini tamamlar.
        }); // MCP kesif ayarlarini tamamlar.
        services.AddRateLimiter(options => // OAuth isteklerinin kaynak tuketimini sinirlar.
        { // OAuth hiz sinirlarini baslatir.
            options.AddPolicy("oauth-register", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromHours(1), QueueLimit = 0 })); // Ayni IP icin saatte en fazla on dinamik istemci kaydina izin verir.
            options.AddPolicy("oauth-token", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions { PermitLimit = 60, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 })); // Token degisim isteklerini IP basina dakikada altmis ile sinirlar.
        }); // OAuth hiz sinirlarini tamamlar.
        return services; // Zincirlenebilir servis koleksiyonunu dondurur.
    } // OAuth kayitlarini tamamlar.

    private static X509Certificate2 LoadCertificate(IConfiguration configuration, string purpose) // Uretim sertifikasini gizli bilgi ayarlarindan yukler.
    { // Sertifika yuklemeyi baslatir.
        var path = configuration[$"OAuth:{purpose}CertificatePath"] ?? throw new InvalidOperationException($"OAuth:{purpose}CertificatePath zorunludur."); // Gerekli sertifika yolunu alir.
        return X509CertificateLoader.LoadPkcs12FromFile(path, configuration[$"OAuth:{purpose}CertificatePassword"], X509KeyStorageFlags.EphemeralKeySet); // Ozel anahtari disari aktarmadan PFX sertifikasini yukler.
    } // Sertifika yuklemeyi tamamlar.
} // OAuth servis kayit sinifini tamamlar.
