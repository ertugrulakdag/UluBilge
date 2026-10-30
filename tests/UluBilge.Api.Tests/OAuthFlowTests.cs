using System.Net; // HTTP durum kodlarini ve HTML kod cozumunu kullanir.
using System.Net.Http.Headers; // MCP isteklerine Bearer basligi ekler.
using System.Net.Http.Json; // Standart OAuth JSON istek ve yanitlarini kullanir.
using System.Security.Cryptography; // PKCE verifier ve challenge degerlerini uretir.
using System.Text; // PKCE icin ASCII baytlarini uretir.
using System.Text.Json; // OAuth ve MCP yanitlarini inceler.
using System.Text.RegularExpressions; // Tarayici formundaki korumali alanlari okur.
using Microsoft.AspNetCore.WebUtilities; // OAuth sorgu ve callback parametrelerini yonetir.
using ModelContextProtocol.Authentication; // Gercek MCP istemcisinin otomatik OAuth akisiyla entegrasyonu test eder.
using ModelContextProtocol.Client; // Gercek MCP istemcisi ve HTTP tasimasini kullanir.
using UluBilge.Core.Enums; // OAuth tokenlarinda Report ve diger rollerin davranisini test eder.
using Xunit; // Entegrasyon testlerini ve beklentilerini tanimlar.

namespace UluBilge.Api.Tests; // OAuth akis testlerini ayri ad alaninda tutar.

public sealed class OAuthFlowTests // Gercek HTTP ve kalici OpenIddict deposu uzerinden OAuth davranisini test eder.
{ // OAuth test sinifini baslatir.
    [Fact] // Kesif ve dinamik istemci kaydini birlikte dogrular.
    public async Task DiscoveryAndDynamicRegistrationExposeWorkingEndpoints() // Tokensiz istemcinin OAuth sunucusunu kesfedebildigini test eder.
    { // Kesif testini baslatir.
        using var app = new OAuthTestApplication(); // Teste ait API ve OAuth deposunu hazirlar.
        try // Test verilerini her durumda temizler.
        { // Kesif beklentilerini baslatir.
            using var client = app.CreateOAuthClient(); // Tarayici benzeri HTTPS istemcisi olusturur.
            var resource = await client.GetFromJsonAsync<JsonElement>("/.well-known/oauth-protected-resource/mcp"); // MCP kaynak kesif belgesini tokensiz ister.
            Assert.Equal("https://localhost:7187/mcp", resource.GetProperty("resource").GetString()); // Kaynak adresinin dogru oldugunu denetler.
            var metadata = await client.GetFromJsonAsync<JsonElement>("/.well-known/oauth-authorization-server"); // OAuth sunucusu kesif belgesini alir.
            Assert.Equal("https://localhost:7187/oauth/token", metadata.GetProperty("token_endpoint").GetString()); // Token adresinin gercek degisim adresiyle eslestigini denetler.
            Assert.Equal("https://localhost:7187/oauth/register", metadata.GetProperty("registration_endpoint").GetString()); // Dinamik kaydin kesifte bildirildigini denetler.
            Assert.Contains(metadata.GetProperty("code_challenge_methods_supported").EnumerateArray(), item => item.GetString() == "S256"); // Guclu PKCE yonteminin bildirildigini denetler.
            Assert.DoesNotContain(metadata.GetProperty("code_challenge_methods_supported").EnumerateArray(), item => item.GetString() == "plain"); // Guvensiz PKCE yonteminin kapali oldugunu denetler.
            Assert.Contains(metadata.GetProperty("token_endpoint_auth_methods_supported").EnumerateArray(), item => item.GetString() == "none"); // Public istemcilerin secretsiz PKCE akisinin kesifte bildirildigini denetler.
            var registration = await RegisterAsync(client); // Istemciyi gercek dinamik kayit adresinden kaydeder.
            Assert.False(string.IsNullOrEmpty(registration)); // Istemciye benzersiz client_id verildigini denetler.
        } // Kesif beklentilerini tamamlar.
        finally { app.Dispose(); app.DeleteDatabase(); } // Test sunucusunu kapatip yalnizca teste ait dosyalari siler.
    } // Kesif testini tamamlar.

#if !DEBUG // Otomatik OAuth yonlendirmesini kimlik dogrulama zorunlu olan Release derlemesinde test eder.
    [Fact] // Gercek MCP SDK istemcisinin otomatik OAuth akisini dogrular.
    public async Task McpSdkAutomaticallyDiscoversRegistersAndAuthenticates() // Kesif, dinamik kayit, tarayici girisi ve tool cagrisini tek istemci akisinda test eder.
    { // Otomatik MCP OAuth testini baslatir.
        using var app = new OAuthTestApplication(); // Bagimsiz OAuth ve MCP sunucusunu olusturur.
        try // Test deposunun temizligini garanti eder.
        { // Otomatik istemci beklentilerini baslatir.
            using var http = app.CreateOAuthClient(); // OAuth metadata ve form isteklerini gercek HTTP hattina baglar.
            var prompted = false; // Istemcinin kullanici girisini baslatip baslatmadigini izler.
            var options = new HttpClientTransportOptions // Gercek SDK tasima ve OAuth seceneklerini hazirlar.
            { // SDK tasima ayarlarini baslatir.
                Endpoint = new Uri("https://localhost:7187/mcp"), // Gercek MCP kaynagina baglanir.
                TransportMode = HttpTransportMode.StreamableHttp, // HTTP uzerinden standart MCP tasimasini kullanir.
                OAuth = new ClientOAuthOptions // Client ID vermeden otomatik kayit akisini baslatir.
                { // OAuth istemci ayarlarini baslatir.
                    RedirectUri = new Uri("https://client.example/callback"), // Kayit sirasinda SDK'nin bildirecegi callback adresini belirler.
                    AuthorizationCallbackHandler = async (context, cancellationToken) => // Gercek tarayici yerine testte ayni giris formunu doldurur.
                    { // Tarayici girisi taklidini baslatir.
                        prompted = true; // SDK'nin kullanicidan giris istedigini kaydeder.
                        using var page = await http.GetAsync(context.AuthorizationUri, cancellationToken); // SDK'nin urettigi PKCE ve state bilgileriyle giris formunu acar.
                        Assert.Equal(HttpStatusCode.OK, page.StatusCode); // SDK isteginin sunucu tarafindan kabul edildigini denetler.
                        var html = await page.Content.ReadAsStringAsync(cancellationToken); // Gecerli giris formunu alir.
                        var fields = Regex.Matches(html, "<input type=\"hidden\" name=\"([^\"]+)\" value=\"([^\"]*)\">").ToDictionary(match => WebUtility.HtmlDecode(match.Groups[1].Value), match => WebUtility.HtmlDecode(match.Groups[2].Value)); // SDK'nin protokol ve antiforgery degerlerini tarayici gibi korur.
                        fields["userName"] = "admin"; // Test kullanicisinin adini girer.
                        fields["password"] = "test-password"; // Yalnizca teste ait parolayi girer.
                        using var login = await http.PostAsync("/oauth/authorize", new FormUrlEncodedContent(fields), cancellationToken); // Kullanicinin giris ve izin formunu gonderir.
                        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode); // Basarili giriste callback yaniti alindigini denetler.
                        var callback = QueryHelpers.ParseQuery(login.Headers.Location!.Query); // Gercek OAuth callback alanlarini alir.
                        return new AuthorizationResult { Code = callback["code"].ToString(), State = callback["state"].ToString(), Iss = callback["iss"].ToString() }; // State ve issuer denetimini SDK'ye birakir.
                    } // Tarayici girisi taklidini tamamlar.
                } // OAuth istemci ayarlarini tamamlar.
            }; // SDK tasima ayarlarini tamamlar.
            await using var transport = new HttpClientTransport(options, http, ownsHttpClient: false); // OAuth HTTP isteklerini test sunucusuna yonlendirir.
            await using var client = await McpClient.CreateAsync(transport); // SDK'nin otomatik OAuth ve MCP baglantisini baslatir.
            var result = await client.CallToolAsync("get_product_sales", new Dictionary<string, object?>()); // SDK'nin aldigi tokenla gercek MCP raporunu cagirir.
            Assert.True(prompted); // Kesif ve dinamik kayittan sonra giris adiminin calistigini denetler.
            Assert.NotEqual(true, result.IsError); // Hata bayragi belirtilmemis olsa da basarili MCP sonucunu kabul eder.
            Assert.Contains("Chai", JsonSerializer.Serialize(result.StructuredContent)); // SDK'nin yapisal rapor verisini alabildigini denetler.
        } // Otomatik istemci beklentilerini tamamlar.
        finally { app.Dispose(); app.DeleteDatabase(); } // Test sunucusunu ve deposunu temizler.
    } // Otomatik MCP OAuth testini tamamlar.
#endif // Release derlemesine ozel otomatik OAuth testini tamamlar.

    [Theory] // Birden fazla gecersiz callback bicimini test eder.
    [InlineData("http://untrusted.example/callback")] // Yerel olmayan HTTP callbackini kapsar.
    [InlineData("https://client.example/callback#fragment")] // Fragment iceren callbacki kapsar.
    [InlineData("https://user:password@client.example/callback")] // Kullanici bilgisi iceren callbacki kapsar.
    public async Task RegistrationRejectsUnsafeRedirects(string redirectUri) // Gecersiz callbacklerin kalici kayda alinmadigini test eder.
    { // Callback kayit testini baslatir.
        using var app = new OAuthTestApplication(); // Bagimsiz OAuth sunucusu olusturur.
        try // Test deposunun temizligini garanti eder.
        { // Callback beklentilerini baslatir.
            using var client = app.CreateOAuthClient(); // Kayit istemcisini olusturur.
            using var response = await client.PostAsJsonAsync("/oauth/register", new { client_name = "test", redirect_uris = new[] { redirectUri }, token_endpoint_auth_method = "none" }); // Hatali callback ile kayit ister.
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); // Hatali callbackin reddedildigini denetler.
        } // Callback beklentilerini tamamlar.
        finally { app.Dispose(); app.DeleteDatabase(); } // Teste ait OAuth deposunu temizler.
    } // Callback kayit testini tamamlar.

    [Fact] // Authorization code akisinin kimlik ve veri aktarimini dogrular.
    public async Task AuthorizationCodePkceReturnsUsableMcpTokensAndRejectsCodeReuse() // PKCE, token JSON'u ve MCP rapor cagrisi arasindaki butun akisi test eder.
    { // Uctan uca OAuth testini baslatir.
        using var app = new OAuthTestApplication(); // Gercek OAuth altyapisini baslatir.
        try // Test temizligini garanti eder.
        { // OAuth akis beklentilerini baslatir.
            using var client = app.CreateOAuthClient(); // Form cookiesini koruyan istemci olusturur.
            var clientId = await RegisterAsync(client); // MCP istemcisini kaydeder.
            var code = await AuthorizeAsync(client, clientId); // Kullanici girisi ve S256 ile kod alir.
            var tokens = await ExchangeAsync(client, clientId, code.Code, code.Verifier); // Kodu gercek OAuth tokenlarina donusturur.
            Assert.Equal("Bearer", tokens.GetProperty("token_type").GetString(), ignoreCase: true); // Token turunun standart Bearer oldugunu denetler.
            Assert.InRange(tokens.GetProperty("expires_in").GetInt32(), 1, 900); // Token omrunun mevcut JWT sinirini asmadigini denetler.
            Assert.True(tokens.TryGetProperty("refresh_token", out _)); // Offline erisim izninde yenileme tokeni verildigini denetler.
            using var report = await McpAsync(client, tokens.GetProperty("access_token").GetString()); // OAuth tokeniyle gercek MCP toolunu cagirir.
            Assert.Equal(HttpStatusCode.OK, report.StatusCode); // Imza, audience, scope, oturum ve rol kontrollerinin basarili oldugunu denetler.
            Assert.Contains("Chai", await report.Content.ReadAsStringAsync()); // MCP sonucunun gercek tool tarafindan uretildigini denetler.
            using var replay = await TokenRequestAsync(client, new() { ["grant_type"] = "authorization_code", ["client_id"] = clientId, ["code"] = code.Code, ["code_verifier"] = code.Verifier, ["redirect_uri"] = "https://client.example/callback" }); // Ayni authorization code'u ikinci kez kullanmayi dener.
            Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode); // Authorization code'un tek kullanimlik oldugunu denetler.
        } // OAuth akis beklentilerini tamamlar.
        finally { app.Dispose(); app.DeleteDatabase(); } // Teste ait sunucu ve dosyalari temizler.
    } // Uctan uca OAuth testini tamamlar.

    [Fact] // Callback, PKCE ve client_id baglantisini dogrular.
    public async Task CodeExchangeRejectsWrongVerifierClientAndRedirect() // Kodun baska istemci veya callback icin kullanilmasini engelledigini test eder.
    { // Kod baglama testini baslatir.
        using var app = new OAuthTestApplication(); // Bagimsiz OAuth sunucusu baslatir.
        try // Test temizligini garanti eder.
        { // Kod baglama beklentilerini baslatir.
            using var client = app.CreateOAuthClient(); // OAuth test istemcisini olusturur.
            var clientId = await RegisterAsync(client); // Asil istemciyi kaydeder.
            var otherClientId = await RegisterAsync(client); // Baska bir istemciyi kaydeder.
            var code = await AuthorizeAsync(client, clientId); // Asil istemci icin kod alir.
            foreach (var values in new[] { (clientId, new string('x', 43), "https://client.example/callback"), (otherClientId, code.Verifier, "https://client.example/callback"), (clientId, code.Verifier, "https://client.example/other") }) // Farkli kod baglama hatalarini sirayla dener.
            { // Hatali kod degisimlerini baslatir.
                using var response = await TokenRequestAsync(client, new() { ["grant_type"] = "authorization_code", ["client_id"] = values.Item1, ["code"] = code.Code, ["code_verifier"] = values.Item2, ["redirect_uri"] = values.Item3 }); // Baglama bilgilerinden birini degistirerek token ister.
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); // Hatali baglama bilgileriyle token verilmedigini denetler.
            } // Hatali kod degisimlerini tamamlar.
        } // Kod baglama beklentilerini tamamlar.
        finally { app.Dispose(); app.DeleteDatabase(); } // Test dosyalarini temizler.
    } // Kod baglama testini tamamlar.

    [Fact] // Tarayici formu ve parola korumasini dogrular.
    public async Task AuthorizationRequiresPkceRegisteredRedirectAntiforgeryAndValidPassword() // Eksik protokol veya giris bilgisiyle kod verilmedigini test eder.
    { // Yetkilendirme giris testini baslatir.
        using var app = new OAuthTestApplication(); // Bagimsiz OAuth sunucusu baslatir.
        try // Test temizligini garanti eder.
        { // Yetkilendirme beklentilerini baslatir.
            using var client = app.CreateOAuthClient(); // Tarayici benzeri istemci olusturur.
            var clientId = await RegisterAsync(client); // Gecerli istemciyi kaydeder.
            var query = AuthorizationQuery(clientId, new string('a', 43)); // Gecerli istegin protokol alanlarini olusturur.
            query.Remove("code_challenge"); // PKCE bilgisini istekten cikarir.
            using var noPkce = await client.GetAsync(QueryHelpers.AddQueryString("/oauth/authorize", query)); // Eksik PKCE ile giris formu ister.
            Assert.Equal(HttpStatusCode.BadRequest, noPkce.StatusCode); // PKCE olmadan yetkilendirme yapilmadigini denetler.
            query = AuthorizationQuery(clientId, new string('a', 43)); // Gecerli protokol alanlarini yeniden kurar.
            query["redirect_uri"] = "https://attacker.example/callback"; // Kayit disi callback adresi dener.
            using var badRedirect = await client.GetAsync(QueryHelpers.AddQueryString("/oauth/authorize", query)); // Kayit disi adrese yetkilendirme ister.
            Assert.Equal(HttpStatusCode.BadRequest, badRedirect.StatusCode); // Sunucunun guvenilmeyen callbacke yonlendirmedigini denetler.
            var form = await LoginFieldsAsync(client, clientId, new string('a', 43)); // Gecerli form ve koruma alanlarini alir.
            form.Remove("__RequestVerificationToken"); // Sahte istek koruma alanini cikarir.
            using var noAntiforgery = await client.PostAsync("/oauth/authorize", new FormUrlEncodedContent(form)); // Korumasiz giris formu gonderir.
            Assert.Equal(HttpStatusCode.BadRequest, noAntiforgery.StatusCode); // Sahte giris isteginin reddedildigini denetler.
            Assert.Equal(0, app.UserState.LoginCount); // Sahte istegin parola servisine ulasmadigini denetler.
            form = await LoginFieldsAsync(client, clientId, new string('a', 43)); // Yeni ve gecerli form alir.
            form["password"] = "wrong-password"; // Yanlis parola gonderir.
            using var badPassword = await client.PostAsync("/oauth/authorize", new FormUrlEncodedContent(form)); // Hatali kullanici girisi yapar.
            Assert.Equal(HttpStatusCode.OK, badPassword.StatusCode); // Hata durumunda giris formunun tekrar gosterildigini denetler.
            Assert.Null(badPassword.Headers.Location); // Hatali giriste authorization code ile callbacke yonlendirme yapilmadigini denetler.
        } // Yetkilendirme beklentilerini tamamlar.
        finally { app.Dispose(); app.DeleteDatabase(); } // Test sunucusu ve dosyalarini temizler.
    } // Yetkilendirme giris testini tamamlar.

    [Fact] // Refresh rotation ve tekrar kullanim korumasini dogrular.
    public async Task RefreshRotatesAndCannotBeReplayed() // Kullanici oturumu halen gecerliyken yenileme tokeninin tekrar kullanilamadigini test eder.
    { // Yenileme testini baslatir.
        using var app = new OAuthTestApplication(); // Bagimsiz OAuth sunucusu baslatir.
        try // Test temizligini garanti eder.
        { // Yenileme beklentilerini baslatir.
            using var client = app.CreateOAuthClient(); // OAuth test istemcisini olusturur.
            var clientId = await RegisterAsync(client); // Yenileme izni olan istemciyi kaydeder.
            var code = await AuthorizeAsync(client, clientId); // Kullanici izniyle kod alir.
            var original = await ExchangeAsync(client, clientId, code.Code, code.Verifier); // Ilk token ciftini alir.
            var originalRefresh = original.GetProperty("refresh_token").GetString()!; // Ilk yenileme tokenini test icinde tutar.
            using var refreshed = await TokenRequestAsync(client, new() { ["grant_type"] = "refresh_token", ["client_id"] = clientId, ["refresh_token"] = originalRefresh }); // Yenileme tokenini kullanir.
            Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode); // Gecerli yenilemenin basarili oldugunu denetler.
            var rotated = await refreshed.Content.ReadFromJsonAsync<JsonElement>(); // Yeni token ciftini okur.
            Assert.NotEqual(originalRefresh, rotated.GetProperty("refresh_token").GetString()); // Yenilemede yeni refresh token verildigini denetler.
            using var replay = await TokenRequestAsync(client, new() { ["grant_type"] = "refresh_token", ["client_id"] = clientId, ["refresh_token"] = originalRefresh }); // Kullanilmis ilk refresh tokeni tekrar gonderir.
            Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode); // Refresh tokenin ikinci kez kullanilamadigini denetler.
        } // Yenileme beklentilerini tamamlar.
        finally { app.Dispose(); app.DeleteDatabase(); } // Teste ait dosyalari temizler.
    } // Yenileme testini tamamlar.

    [Fact] // Erisim tokeni ve refresh token icin ortak oturum iptalini dogrular.
    public async Task RevokedSessionPreventsOAuthRefreshAndMcpAccess() // Henuz suresi dolmamis OAuth tokeninin iptal edilmis oturumla kullanilamadigini test eder.
    { // Oturum iptali testini baslatir.
        using var app = new OAuthTestApplication(); // Bagimsiz OAuth sunucusu olusturur.
        try // Test dosyalarinin temizligini garanti eder.
        { // Oturum iptali beklentilerini baslatir.
            using var client = app.CreateOAuthClient(); // Test istemcisini hazirlar.
            var clientId = await RegisterAsync(client); // Istemciyi kaydeder.
            var code = await AuthorizeAsync(client, clientId); // Kullanici girisiyle gecerli kod alir.
            var tokens = await ExchangeAsync(client, clientId, code.Code, code.Verifier); // Gecerli erisim ve yenileme tokenlarini alir.
            app.UserState.IsValid = false; // Veritabanindaki oturum iptalini taklit eder.
            using var report = await McpAsync(client, tokens.GetProperty("access_token").GetString()); // Iptal edilen oturumun erisim tokeniyle rapor ister.
#if DEBUG // Debug derlemesindeki acik erisim tercihini korur.
            Assert.Equal(HttpStatusCode.OK, report.StatusCode); // Debug anonim erisiminin degismedigini denetler.
#else // Release derlemesinde kalici oturum denetimini dogrular.
            Assert.Equal(HttpStatusCode.Unauthorized, report.StatusCode); // Suresi dolmamis tokenin iptal edilmis oturumla reddedildigini denetler.
#endif // Derleme turune gore MCP erisim denetimini tamamlar.
            using var refresh = await TokenRequestAsync(client, new() { ["grant_type"] = "refresh_token", ["client_id"] = clientId, ["refresh_token"] = tokens.GetProperty("refresh_token").GetString()! }); // Iptal edilmis oturumla yeni token ister.
            Assert.Equal(HttpStatusCode.BadRequest, refresh.StatusCode); // Gecerli refresh tokenin iptal edilmis oturumla yenilenemedigini denetler.
        } // Oturum iptali beklentilerini tamamlar.
        finally { app.Dispose(); app.DeleteDatabase(); } // Teste ait sunucu ve dosyalari temizler.
    } // Oturum iptali testini tamamlar.

    [Theory] // Rapor ve ilgisiz rollerin OAuth davranisini birlikte denetler.
    [InlineData(RoleEnum.Report, HttpStatusCode.OK)] // Report rolunun rapora erisebildigini denetler.
    [InlineData(RoleEnum.Region, HttpStatusCode.Forbidden)] // Rapor izni olmayan rolun engellendigini denetler.
    public async Task OAuthTokensRespectReportRoles(RoleEnum role, HttpStatusCode expected) // Tokenin OAuth ile alinmasinin mevcut rol kuralini asamadigini test eder.
    { // OAuth rol testini baslatir.
        using var app = new OAuthTestApplication(); // Bagimsiz OAuth sunucusu hazirlar.
        app.UserState.Role = role; // Test kullanicisinin rolunu belirler.
        try // Test temizligini garanti eder.
        { // Rol beklentilerini baslatir.
            using var client = app.CreateOAuthClient(); // Test istemcisini hazirlar.
            var clientId = await RegisterAsync(client); // Istemciyi kaydeder.
            var code = await AuthorizeAsync(client, clientId); // Belirtilen roldeki kullaniciya kod verir.
            var tokens = await ExchangeAsync(client, clientId, code.Code, code.Verifier); // Kullanici rolunu tasiyan gercek OAuth tokenini alir.
            using var response = await McpAsync(client, tokens.GetProperty("access_token").GetString()); // Ayni MCP raporuna tokenla erismeyi dener.
#if DEBUG // Debug derlemesinin anonim erisim davranisini korur.
            expected = HttpStatusCode.OK; // Debug modunda tum roller icin anonim erisim beklentisini uygular.
#endif // Derleme turune gore rol beklentisini tamamlar.
            Assert.Equal(expected, response.StatusCode); // Derleme turune gore Report veya ilgisiz rol icin beklenen erisim sonucunu denetler.
        } // Rol beklentilerini tamamlar.
        finally { app.Dispose(); app.DeleteDatabase(); } // Test verilerini temizler.
    } // OAuth rol testini tamamlar.

    [Fact] // Kalici OAuth kayitlarinin yeniden baslatmada korundugunu dogrular.
    public async Task AuthorizationCodeSurvivesServerRestart() // Kod ve istemci bilgilerinin sadece bellekte tutulmadigini test eder.
    { // Kalicilik testini baslatir.
        using var first = new OAuthTestApplication(); // Ilk OAuth sunucusunu olusturur.
        try // Kalici test deposunun temizligini garanti eder.
        { // Kalicilik beklentilerini baslatir.
            using var client = first.CreateOAuthClient(); // Ilk sunucuya baglanir.
            var clientId = await RegisterAsync(client); // Istemciyi kalici depoya kaydeder.
            var code = await AuthorizeAsync(client, clientId); // Henuz kullanilmamis bir kod alir.
            first.Dispose(); // Ilk sunucuyu kapatir.
            using var second = new OAuthTestApplication(first.DatabasePath); // Ayni depoyla yeni sunucu baslatir.
            using var restartedClient = second.CreateOAuthClient(); // Yeniden baslatilmis sunucuya baglanir.
            var tokens = await ExchangeAsync(restartedClient, clientId, code.Code, code.Verifier); // Onceki sunucuda verilmis kodu degistirir.
            Assert.True(tokens.TryGetProperty("access_token", out _)); // Kalici istemci ve kod bilgilerinin korunmus oldugunu denetler.
        } // Kalicilik beklentilerini tamamlar.
        finally { first.Dispose(); first.DeleteDatabase(); } // Test sunucusunu ve kalici test deposunu temizler.
    } // Kalicilik testini tamamlar.

    [Fact] // Eski token akisinin OAuth eklenince bozulmadigini dogrular.
    public async Task ExistingApiTokenStillWorksAndMcpChallengeAdvertisesDiscovery() // JWT uyumlulugunu ve OAuth kesif basligini test eder.
    { // Uyumluluk testini baslatir.
        using var app = new OAuthTestApplication(); // Gercek JWT ve OAuth semalariyla sunucu olusturur.
        try // Test temizligini garanti eder.
        { // Uyumluluk beklentilerini baslatir.
            using var client = app.CreateOAuthClient(); // Test istemcisini olusturur.
            using var login = await client.PostAsJsonAsync("/v1/Auth/login", new { userName = "admin", password = "test-password" }); // Mevcut JSON giris adresinden gercek JWT alir.
            Assert.Equal(HttpStatusCode.OK, login.StatusCode); // Eski giris sozlesmesinin korundugunu denetler.
            var tokens = await login.Content.ReadFromJsonAsync<JsonElement>(); // Eski token yanitini okur.
            using var report = await McpAsync(client, tokens.GetProperty("accessToken").GetString()); // Eski tokeni MCP adresinde kullanir.
            Assert.True(report.StatusCode == HttpStatusCode.OK, report.IsSuccessStatusCode ? null : await report.Content.ReadAsStringAsync()); // Eski token uyumlulugu bozulursa guvenli protokol hata aciklamasini gosterir.
            using var anonymous = await McpAsync(client, null); // Token olmadan MCP istegi yapar.
#if DEBUG // Debug derlemesinde kullanicinin istedigi anonim erisimi dogrular.
            Assert.Equal(HttpStatusCode.OK, anonymous.StatusCode); // Debug rol etiketinin anonim erisimi korudugunu denetler.
#else // Release derlemesinde OAuth erisim kontrolunu dogrular.
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode); // Uretim derlemesinde token zorunlulugunu denetler.
            Assert.Contains("resource_metadata=", anonymous.Headers.WwwAuthenticate.ToString()); // Istemcinin OAuth kesif adresini 401 yanitinda alabildigini denetler.
#endif // Derleme turune gore erisim beklentilerini tamamlar.
        } // Uyumluluk beklentilerini tamamlar.
        finally { app.Dispose(); app.DeleteDatabase(); } // Test dosyalarini temizler.
    } // Uyumluluk testini tamamlar.

    private static async Task<string> RegisterAsync(HttpClient client) // Gercek dinamik istemci kaydini testlerde ortak kullanir.
    { // Test istemcisi kaydini baslatir.
        using var response = await client.PostAsJsonAsync("/oauth/register", new { client_name = "OAuth test client", redirect_uris = new[] { "https://client.example/callback" }, grant_types = new[] { "authorization_code", "refresh_token" }, response_types = new[] { "code" }, token_endpoint_auth_method = "none" }); // Public PKCE istemcisini kaydeder.
        Assert.Equal(HttpStatusCode.Created, response.StatusCode); // Kaydin basarili oldugunu denetler.
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(); // Kayit yanitini okur.
        return body.GetProperty("client_id").GetString()!; // Istemci kimligini test akisina verir.
    } // Test istemcisi kaydini tamamlar.

    private static Dictionary<string, string?> AuthorizationQuery(string clientId, string verifier) => new() { ["client_id"] = clientId, ["redirect_uri"] = "https://client.example/callback", ["response_type"] = "code", ["scope"] = "mcp:tools offline_access", ["state"] = "test-state", ["resource"] = "https://localhost:7187/mcp", ["code_challenge_method"] = "S256", ["code_challenge"] = WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))) }; // Gercek S256 challenge iceren authorization istegini olusturur.

    private static async Task<Dictionary<string, string>> LoginFieldsAsync(HttpClient client, string clientId, string verifier) // Tarayici formu ve antiforgery alanlarini alir.
    { // Form okumasini baslatir.
        using var response = await client.GetAsync(QueryHelpers.AddQueryString("/oauth/authorize", AuthorizationQuery(clientId, verifier))); // Dogrulanmis OAuth istegiyle giris formunu acar.
        Assert.True(response.StatusCode == HttpStatusCode.OK, response.IsSuccessStatusCode ? null : await response.Content.ReadAsStringAsync()); // Gecerli istegin form dondurdugunu ve protokol hatalarinin gorunur oldugunu denetler.
        var html = await response.Content.ReadAsStringAsync(); // Formun HTML icerigini okur.
        var fields = Regex.Matches(html, "<input type=\"hidden\" name=\"([^\"]+)\" value=\"([^\"]*)\">").ToDictionary(match => WebUtility.HtmlDecode(match.Groups[1].Value), match => WebUtility.HtmlDecode(match.Groups[2].Value)); // Gizli protokol ve koruma alanlarini tarayici gibi cozer.
        fields["userName"] = "admin"; // Formun test kullanici adini belirler.
        fields["password"] = "test-password"; // Formun yalnizca test icin tanimlanmis parolasini belirler.
        return fields; // POST icin hazir form alanlarini dondurur.
    } // Form okumasini tamamlar.

    private static async Task<(string Code, string Verifier)> AuthorizeAsync(HttpClient client, string clientId) // Giris yapip tek kullanimlik authorization code alir.
    { // Test yetkilendirmesini baslatir.
        var verifier = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32)); // Yeterli uzunlukta rastgele PKCE verifier uretir.
        var fields = await LoginFieldsAsync(client, clientId, verifier); // Korumali giris formunu alir.
        using var response = await client.PostAsync("/oauth/authorize", new FormUrlEncodedContent(fields)); // Giris ve kullanici iznini gonderir.
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode); // Basarili girisin kayitli callbacke yonlendirdigini denetler.
        var callback = response.Headers.Location!; // Sunucunun kayitli callback yanitini alir.
        Assert.Equal("client.example", callback.Host); // Kodun yalnizca kayitli istemciye gonderildigini denetler.
        var parameters = QueryHelpers.ParseQuery(callback.Query); // Callbackteki protokol alanlarini cozer.
        Assert.Equal("test-state", parameters["state"].ToString()); // Istemci korelasyon bilgisinin korundugunu denetler.
        return (parameters["code"].ToString(), verifier); // Kod ve gizli verifier degerini token testine verir.
    } // Test yetkilendirmesini tamamlar.

    private static Task<HttpResponseMessage> TokenRequestAsync(HttpClient client, Dictionary<string, string> values) => client.PostAsync("/oauth/token", new FormUrlEncodedContent(values)); // Standart form biciminde OAuth token istegi gonderir.

    private static async Task<JsonElement> ExchangeAsync(HttpClient client, string clientId, string code, string verifier) // Gecerli authorization code'u token ciftine cevirir.
    { // Test token degisimini baslatir.
        using var response = await TokenRequestAsync(client, new() { ["grant_type"] = "authorization_code", ["client_id"] = clientId, ["code"] = code, ["code_verifier"] = verifier, ["redirect_uri"] = "https://client.example/callback", ["resource"] = "https://localhost:7187/mcp" }); // Kodla iliskili tum baglama bilgilerini gonderir.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); // Gecerli kodun tokena donustugunu denetler.
        return await response.Content.ReadFromJsonAsync<JsonElement>(); // Token yanitini testte incelenmek uzere dondurur.
    } // Test token degisimini tamamlar.

    private static Task<HttpResponseMessage> McpAsync(HttpClient client, string? token) // Gercek MCP HTTP protokolu uzerinden rapor ister.
    { // MCP test istegini baslatir.
        var metadata = new Dictionary<string, object> // Guncel MCP istemci metadata alanlarini hazirlar.
        { // Istemci metadata alanlarini baslatir.
            ["io.modelcontextprotocol/protocolVersion"] = "2026-07-28", // MCP protokol surumunu her istekte bildirir.
            ["io.modelcontextprotocol/clientCapabilities"] = new { }, // Test istemcisinin ek yetenek istemedigini bildirir.
            ["io.modelcontextprotocol/clientInfo"] = new { name = "OAuthTests", version = "1.0" } // Test istemcisinin kimlik bilgisini bildirir.
        }; // Istemci metadata alanlarini tamamlar.
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp") { Content = JsonContent.Create(new { jsonrpc = "2.0", id = 1, method = "tools/call", @params = new { name = "get_product_sales", arguments = new { }, _meta = metadata } }) }; // Guncel metadata iceren tools/call istegi olusturur.
        request.Headers.Accept.ParseAdd("application/json"); // JSON MCP yanitlarini kabul eder.
        request.Headers.Accept.ParseAdd("text/event-stream"); // Streamable HTTP MCP yanitlarini kabul eder.
        request.Headers.Add("MCP-Protocol-Version", "2026-07-28"); // Baslangic oturumu gerektirmeyen guncel protokol surumunu belirtir.
        request.Headers.Add("Mcp-Method", "tools/call"); // Guncel Streamable HTTP yontem basligini istekle esler.
        request.Headers.Add("Mcp-Name", "get_product_sales"); // Arac adini HTTP yonlendirme basliginda da bildirir.
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token); // Varsa gercek erisim tokenini basliga ekler.
        return client.SendAsync(request); // Istegi gercek API hattindan gecirir.
    } // MCP test istegini tamamlar.
} // OAuth test sinifini tamamlar.
