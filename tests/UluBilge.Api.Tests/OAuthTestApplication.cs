using Microsoft.AspNetCore.Hosting; // Test sunucusunun ortam ayarlarini yapilandirir.
using Microsoft.AspNetCore.Mvc.Testing; // Gercek API'yi bellek ici HTTP sunucusunda calistirir.
using Microsoft.AspNetCore.TestHost; // Uretim servislerini kontrollu test servisleriyle degistirir.
using Microsoft.Data.Sqlite; // Test bitiminde SQLite dosya kilitlerini serbest birakir.
using Microsoft.EntityFrameworkCore; // Her test icin bagimsiz OAuth veritabani olusturur.
using Microsoft.Extensions.DependencyInjection; // Test servislerini kaydeder.
using Microsoft.Extensions.DependencyInjection.Extensions; // Mevcut servis kayitlarini test icin degistirir.
using Microsoft.Extensions.Logging; // Testlerin sistem olay gunlugune yazmadan calismasini saglar.
using UluBilge.Api.Authentication; // Gercek JWT uretimini ve oturum sozlesmesini kullanir.
using UluBilge.Api.Authentication.OAuth; // OAuth deposu ve ayarlarini teste baglar.
using UluBilge.App.Queries.ReportHandler; // MCP testine sabit rapor verisi saglar.
using UluBilge.Core.Enums; // Farkli kullanici rollerini test eder.
using UluBilge.Core.Models; // Rapor yanit modelini kullanir.
using UluBilge.Core.Queries; // Gercek MCP tooluna test sorgu isleyicisi saglar.

namespace UluBilge.Api.Tests; // OAuth entegrasyon testlerinin ad alanini tanimlar.

public sealed class OAuthTestApplication(string? existingDatabasePath = null) : WebApplicationFactory<Program> // Her testte gercek OAuth ve MCP altyapisini baslatir.
{ // Test uygulamasini baslatir.
    public string DatabasePath { get; } = existingDatabasePath ?? Path.Combine(Path.GetTempPath(), "UluBilge.OAuthTests." + Guid.NewGuid().ToString("N"), "oauth.db"); // Testlere ait benzersiz kalici dosya yolunu belirler.
    public FakeUserState UserState { get; } = new(); // Kullanici iptal ve rol durumlarini veritabanindan bagimsiz denetler.

    protected override void ConfigureWebHost(IWebHostBuilder builder) // Test sunucusunun bagimliliklarini hazirlar.
    { // Test yapilandirmasini baslatir.
        builder.UseEnvironment("Development"); // Yerel sertifika ve gelistirme ayarlariyla protokol testi yapar.
        builder.ConfigureLogging(logging => logging.ClearProviders()); // Test sirasinda sistem gunlugu ve hassas HTTP verisi yazilmasini engeller.
        builder.ConfigureTestServices(services => // Is veritabanina dokunmadan gercek OAuth protokolunu test eder.
        { // Test servislerini baslatir.
            services.RemoveAll<OAuthServerSettings>(); // Varsayilan OAuth dosya yolunu testten ayirir.
            services.AddSingleton(new OAuthServerSettings(new Uri("https://localhost:7187/"), DatabasePath, true)); // Sabit issuer ve teste ait depoyu kullanir.
            services.RemoveAll<DbContextOptions<OAuthDbContext>>(); // Is ortamina ait OAuth baglanti ayarlarini kaldirir.
            services.AddDbContext<OAuthDbContext>(options => options.UseSqlite($"Data Source={DatabasePath};Pooling=False").UseOpenIddict()); // Gercek OpenIddict kayitlarini testin SQLite dosyasinda tutar.
            services.RemoveAll<IUserAuthenticationService>(); // Gercek kullanici veritabanina yazilmasini engeller.
            services.AddSingleton(UserState); // Testin yonettigi kullanici durumunu paylasir.
            services.AddScoped<IUserAuthenticationService, FakeUserAuthentication>(); // Parola ve oturum sonucunu kontrollu sekilde saglar.
            services.RemoveAll<IAsyncQueryHandler<GetProductSalesAsync.Query, GetProductSalesAsync.Result>>(); // Rapor icin Northwind bagimliligini testten ayirir.
            services.AddScoped<IAsyncQueryHandler<GetProductSalesAsync.Query, GetProductSalesAsync.Result>, FakeReportHandler>(); // Gercek MCP toolunun sorgu sonucunu sabitler.
        }); // Test servislerini tamamlar.
    } // Test yapilandirmasini tamamlar.

    public HttpClient CreateOAuthClient() => CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost:7187/"), AllowAutoRedirect = false, HandleCookies = true }); // Tarayici cookiesini koruyan ve OAuth callbackini izlemeyen istemci olusturur.

    public void DeleteDatabase() // Yalnizca bu teste ait gecici depoyu temizler.
    { // Gecici depo temizligini baslatir.
        var directory = Path.GetFullPath(Path.GetDirectoryName(DatabasePath)!); // Silinecek dizinin tam yolunu hesaplar.
        var temporaryRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar; // Izin verilen gecici kok dizinini belirler.
        if (!directory.StartsWith(temporaryRoot, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(directory).StartsWith("UluBilge.OAuthTests.", StringComparison.Ordinal)) throw new InvalidOperationException("Beklenmeyen test dizini."); // Test disindaki bir dizinin silinmesini engeller.
        SqliteConnection.ClearAllPools(); // SQLite havuzlarinin dosyayi tutmasini engeller.
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); // Sadece dogrulanmis test dizinini kaldirir.
    } // Gecici depo temizligini tamamlar.

    private sealed class FakeReportHandler : IAsyncQueryHandler<GetProductSalesAsync.Query, GetProductSalesAsync.Result> // Gercek MCP akisi icin is veritabanindan bagimsiz rapor saglar.
    { // Test sorgu isleyicisini baslatir.
        public Task<GetProductSalesAsync.Result> HandleAsync(GetProductSalesAsync.Query query, CancellationToken ct = default) => Task.FromResult(new GetProductSalesAsync.Result { Data = [new ProductSalesReportItem { ProductId = 1, ProductName = "Chai", TotalQuantity = 4, OrderCount = 2, NetSales = 72m }] }); // MCP cagrisi icin bilinen ve degismeyen bir sonuc dondurur.
    } // Test sorgu isleyicisini tamamlar.
} // Test uygulamasini tamamlar.

public sealed class FakeUserState // Guvenlik testinin yonettigi kullanici durumunu tutar.
{ // Test kullanicisi durumunu baslatir.
    public bool IsValid { get; set; } = true; // Oturum iptalini test etmek icin gecerliligi degistirir.
    public int LoginCount { get; set; } // Sahte isteklerin parola servisine ulasip ulasmadigini olcer.
    public RoleEnum Role { get; set; } = RoleEnum.Admin; // Farkli rol durumlarinda MCP yetkilendirmesini test eder.
    public UserSession Session => new(new AuthenticatedUser(1, "admin", Role), Guid.Parse("11111111-1111-1111-1111-111111111111"), Guid.Parse("22222222-2222-2222-2222-222222222222")); // Yeniden baslatma testlerinde de ayni oturum kimligini kullanir.
} // Test kullanicisi durumunu tamamlar.

internal sealed class FakeUserAuthentication(FakeUserState state, IAccessTokenService accessTokens, TimeProvider timeProvider) : IUserAuthenticationService // Veritabanina dokunmadan mevcut JWT servisinin kullanilmasini saglar.
{ // Test kimlik dogrulamasini baslatir.
    public Task<UserSession?> LoginForOAuthAsync(string userName, string password, CancellationToken cancellationToken = default) // OAuth girisinde test kullanicisini dogrular.
    { // Test parola denetimini baslatir.
        state.LoginCount++; // Parola servisine yapilan cagrilari kaydeder.
        return Task.FromResult<UserSession?>(userName == "admin" && password == "test-password" ? state.Session : null); // Yalnizca testte tanimlanan kimlik bilgilerini kabul eder.
    } // Test parola denetimini tamamlar.
    public async Task<TokenResponse?> LoginAsync(string userName, string password, CancellationToken cancellationToken = default) // Eski JSON girisinin gercek JWT uretimini test eder.
    { // Eski token uretimini baslatir.
        var session = await LoginForOAuthAsync(userName, password, cancellationToken); // Ayni test kullanicisini dogrular.
        if (session is null) return null; // Hatali parola icin token vermez.
        var token = accessTokens.Create(session.User, session.SecurityStamp, session.FamilyId); // Uretim JWT servisiyle gercek imzali token uretir.
        return new TokenResponse(token.AccessToken, token.TokenType, token.ExpiresIn, token.ExpiresAtUtc, "test-refresh-token", timeProvider.GetUtcNow().AddDays(7)); // Test istemcisine mevcut token yanit bicimini verir.
    } // Eski token uretimini tamamlar.
    public Task<TokenResponse?> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default) => Task.FromResult<TokenResponse?>(null); // Eski yenileme adresini OAuth testinde devre disi birakir.
    public Task LogoutAsync(UserSession session, CancellationToken cancellationToken = default) { state.IsValid = false; return Task.CompletedTask; } // Test kullanicisinin oturumunu iptal eder.
    public Task<bool> IsSessionValidAsync(UserSession session, CancellationToken cancellationToken = default) => Task.FromResult(state.IsValid && session == state.Session); // Degismis rol veya iptal edilmis oturumu reddeder.
} // Test kimlik dogrulamasini tamamlar.
