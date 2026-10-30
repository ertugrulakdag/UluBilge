# UluBilge

.NET 10, SQL Server Northwind ve Dapper kullanan CQRS API. Command/Query, handler ve controller düzenini izler.

## Katmanlar

- **UluBilge.Api:** Controller'lar, HTTP yanıtları, merkezi Problem Details hata yönetimi ve OpenAPI.
- **UluBilge.App:** Command/Query handler'ları, doğrulama ve Unit of Work decorator'ları.
- **UluBilge.Data:** Repository arayüzleri, Dapper repository'leri, bağlantı ayarları ve Unit of Work.
- **UluBilge.Core:** Entity, sayfalama modeli ve uygulamanın kendi CQRS sözleşmeleri.
- **UluBilge.Migration:** Northwind SQL scriptlerini yazma bağlantısıyla çalıştıran konsol uygulaması.

Bağımlılık yönü `Api → App → Data → Core` şeklindedir.

## Bağlantılar ve Unit of Work

`src/UluBilge.Api/appsettings.json` içinde iki bağlantı bulunur:

- `ConnectionStrings:ReadConnection`: Query handler'ları kullanır.
- `ConnectionStrings:WriteConnection`: Command handler'ları kullanır.

Başlangıç ayarları `localhost` sunucusundaki `Northwind` veritabanını ve verilen SQL
kullanıcı bilgilerini kullanır. SQL Server farklı bir adreste veya instance'ta ise
iki bağlantının `Server` değerini düzenleyin. İki bağlantı bugün aynı sunucuya işaret
eder; okuma sunucusu ayrıldığında yalnızca `ReadConnection` değiştirilir.

Her handler çağrısı için bağlantı açılır. Query'ler okuma bağlantısında transaction
başlatmadan çalışır. Command'lar yazma bağlantısında transaction başlatır; handler
içindeki bütün repository okumaları ve yazmaları aynı bağlantı ve transaction'ı
kullanır. Başarıda commit, hata veya iptalde rollback uygulanır. Rollback, iptal
edilmiş HTTP token'ından bağımsız çalışır.

Bağlantı ve transaction `await using` kapsamındadır. Bağlantı açılışı, transaction
başlatma, handler veya commit başarısız olsa da kaynaklar serbest bırakılır.
Sonuçlar bağlantı kapanmadan belleğe alınır. Repository'ler bağlantı açmaz veya
kapatmaz; aktif Unit of Work dışında çalışamaz. Aynı Unit of Work üzerinde iç içe
veya eş zamanlı işlem reddedilir. Tek Command içindeki birden fazla repository
çağrısı aynı transaction'a katılır; birbirinden ayrı Command'lar ayrı transaction'dır.
SqlClient connection pooling etkin olduğunda dispose edilen bağlantı havuza iade
edilir; bu, uygulamanın açık bir bağlantı veya transaction tutması anlamına gelmez.

`ApplicationIntent=ReadOnly` yönlendirme niyetidir; SQL erişim yetkisi oluşturmaz.
Verilen `sa` hesabı iki bağlantıda da kullanıldığından veritabanı düzeyinde okuma
kısıtlaması yoktur. Yerel sertifika için `TrustServerCertificate=True` ayarlıdır.
Ayarlar ortam dosyaları veya `ConnectionStrings__ReadConnection` ve
`ConnectionStrings__WriteConnection` ortam değişkenleriyle değiştirilebilir.

## Çalıştırma

.NET 10 SDK ve erişilebilir, Northwind şeması kurulu bir SQL Server gerekir.

```powershell
dotnet build UluBilge.slnx
dotnet run --project src/UluBilge.Api --launch-profile http
```

HTTP adresi `http://localhost:5156`, HTTPS profili `https://localhost:7187`.
Development ortamında Scalar API dokümantasyonuna `http://localhost:5156/docs`
veya HTTPS profiliyle `https://localhost:7187/docs` adresinden ulaşılabilir.
OpenAPI belgesi `/openapi/v1.json` adresindedir. Bu iki uç nokta yalnızca
Development ortamında açıktır.

Northwind henüz kurulu değilse hedef veritabanını oluşturduktan sonra migration
uygulaması elle çalıştırılabilir:

```powershell
dotnet run --project src/UluBilge.Migration
```

Migration yalnızca API projesindeki `appsettings.json` dosyasını kullanır ve
`WriteConnection` ile bağlanır; ortam seçimi yapılmaz.
Mevcut master script bazı Northwind nesnelerini silip yeniden oluşturur; yalnızca
kurulumunu yapmak istediğiniz veritabanında çalıştırın. API başlatılırken migration
otomatik çalıştırılmaz.

## Region CRUD

Bütün Region uç noktaları Bearer access token gerektirir. Okuma işlemlerinde
`regions:read`, ekleme/güncelleme/silmede `regions:write` yetkisi aranır.

| HTTP | Yol | İşlem |
| --- | --- | --- |
| GET | `/v1/Region/GetRegions?pageNumber=1&pageSize=20` | Sayfalı liste |
| GET | `/v1/Region/GetRegion/1` | Tek kayıt |
| POST | `/v1/Region/Add` | Ekleme |
| PUT | `/v1/Region/Update` | Güncelleme |
| DELETE | `/v1/Region/Delete/5` | Silme |

Ekleme ve güncelleme gövdesi:

```json
{
  "regionId": 5,
  "regionDescription": "Marmara"
}
```

Northwind'de `RegionID` identity değildir; pozitif ID gönderilmelidir.
`RegionDescription` zorunludur ve en fazla 50 karakterdir. `nchar(50)` alanının
sonundaki boşluklar okurken temizlenir. Sayfa numarası en az 1, sayfa boyutu 1–100
aralığındadır; varsayılan boyut 20'dir.

Başarılı yanıtlar SitePanosu'ndaki gibi `data` alanıyla sarılır. Geçersiz istekler
400, bulunamayan kayıtlar 404, aynı ID ve Territories bağlantısı nedeniyle silme
çatışmaları 409 Problem Details yanıtı alır. Region şemasında `IsActive` olmadığından
silme fiziksel olarak yapılır.

`src/UluBilge.Api/UluBilge.Api.http` dosyasında giriş ve Bearer token ile
çalıştırılabilecek CRUD istekleri bulunur.

## Ürün satış raporu

`GET /v1/Report/GetProductSales?year=1997&topCount=10` ürünleri indirim sonrası
net satış tutarına göre azalan sırada listeler. Bearer token ve `Report` veya
`Admin` rollerinden biri gerekir. İstek, diğer sorgular gibi Query handler ve
repository üzerinden `ReadConnection` bağlantısında çalışır.

Debug derlemelerinde `RoleAuthorize` kullanılan controller ve metotlarda kimlik
ve rol kontrolü atlanır. Release derlemelerinde token ve rol kontrolleri uygulanır.

`year` isteğe bağlıdır; verilmezse tüm siparişler rapora katılır. Yıl aralığı
1753–9999'dur ve filtre `Orders.OrderDate` alanına uygulanır. `topCount` isteğe
bağlıdır; gönderilmezse veya null ise 10 kullanılır. Değer verildiğinde 1–100
arasında olmalıdır. Hatalı parametreler 400 döndürür.

Yanıttaki `data.data` listesinde `productId`, `productName`, `totalQuantity`,
`orderCount` ve `netSales` bulunur. Net satış, sipariş satırındaki
`UnitPrice × Quantity × (1 − Discount)` tutarları toplanıp iki ondalığa yuvarlanarak
hesaplanır; kargo bedeli dahil değildir. Eşit tutarlarda ürün numarasıyla sıralanır.
Satışı olmayan ürünler listelenmez; eşleşen satış yoksa boş liste döner.

## MCP ürün satış tool'u

Streamable HTTP MCP sunucusu `/mcp` adresindedir. `get_product_sales` tool'u,
`ReportController.GetProductSalesAsync` ile aynı Query handler'ı ve yanıt modelini
kullanır. `year` ve `topCount` isteğe bağlıdır; `topCount` gönderilmezse veya null
ise 10 kullanılır. Sonuç MCP `structuredContent` alanında `data.data` listesiyle
döner. Geçersiz yıl veya ürün sayısı, açıklamasıyla birlikte MCP tool hatası döndürür.

Örnek `tools/call` parametreleri:

```json
{
  "name": "get_product_sales",
  "arguments": { "year": 1997, "topCount": 10 }
}
```

Release derlemelerinde MCP adresi de Bearer token ve `Report` veya `Admin`
rollerinden birini gerektirir. Debug derlemelerinde mevcut `RoleAuthorize`
davranışıyla anonim erişilebilir.

## MCP OAuth girişi

OpenIddict 7.7.2, authorization code + PKCE (`S256`) ve refresh token akışlarını
yönetir. MCP istemcisi sunucuyu keşfeder, `/oauth/register` ile kendi callback
adresini kaydeder ve kullanıcıyı giriş formuna yönlendirir. Form, istemcinin adını
ve rapor erişimi isteğini gösterir; **Giriş yap ve izin ver** işlemi mevcut kullanıcı
adı/parola, hesap kilitleme ve oturum kurallarıyla doğrulanır.

| HTTP | Adres | İşlev |
| --- | --- | --- |
| GET | `/.well-known/oauth-protected-resource/mcp` | MCP kaynağını, `mcp:tools` iznini ve OAuth sunucusunu bildirir |
| GET | `/.well-known/oauth-authorization-server` | Authorization, token, kayıt ve iptal adreslerini bildirir |
| GET | `/.well-known/openid-configuration` | Aynı sunucunun alternatif keşif belgesini sunar |
| POST | `/oauth/register` | İstemciyi ve callback adreslerini kalıcı kaydeder |
| GET / POST | `/oauth/authorize` | Giriş formunu ve açık kullanıcı iznini işler |
| POST | `/oauth/token` | Kodu veya refresh tokenı standart OAuth token yanıtına dönüştürür |
| POST | `/oauth/revoke` | İstemcinin OAuth tokenını iptal eder |

Kodlar kısa ömürlü ve tek kullanımlıktır. Kod değişiminde PKCE, `client_id` ve
callback adresi OpenIddict tarafından doğrulanır. Callback için HTTPS veya yerel
masaüstü istemcilerine ait loopback HTTP kabul edilir. Kayıtlı olmayan adreslere
kod gönderilmez. `none`, `client_secret_post` ve `client_secret_basic` istemci
yöntemleri desteklenir; secret gerektiğinde rastgele üretilir ve OpenIddict
tarafından hash'lenerek saklanır. PKCE bütün istemciler için zorunludur.

OAuth access tokenının audience değeri MCP adresidir ve `mcp:tools` izni aranır.
Refresh tokenlar her kullanımda yenilenir; tekrar kullanımları reddedilir. Token
kaydının geçerliliği ve mevcut veritabanı oturumu her OAuth erişiminde kontrol
edilir. Kullanıcı pasifleştirilirse, rolü veya security stamp değeri değişirse ya da
oturumu iptal edilirse OAuth erişimi de geçersiz olur. `offline_access` istenirse
refresh token verilir. Eski `/v1/Auth/login` tokenları MCP'de desteklenmeye devam eder.

OAuth istemci, authorization code ve token kayıtları varsayılan olarak
`src/UluBilge.Api/App_Data/oauth.db` dosyasında tutulur. Bu, Northwind iş verilerinden
ayrı kalıcı bir SQLite deposudur; uygulama ilk açılışta tablolarını oluşturur.
`App_Data` Git'e dahil edilmez. Yeniden başlatmalarda istemci ve tokenların
korunması için bu dosya ve kullanılan anahtarlar saklanmalıdır.

| Ayar | Açıklama |
| --- | --- |
| `OAuth:Issuer` | Uygulamanın dışarıdan erişilen sabit HTTPS kök adresi; örneğin `https://mcp.example.com/` |
| `OAuth:DatabasePath` | OAuth SQLite dosyasının yolu; varsayılan `App_Data/oauth.db` |
| `OAuth:AllowDynamicClientRegistration` | Dinamik kayıt ve keşifte kayıt adresi; varsayılan `true` |
| `OAuth:SigningCertificatePath` | Development dışındaki ortamlarda private key içeren imzalama PFX dosyasının tam yolu |
| `OAuth:SigningCertificatePassword` | İmzalama PFX parolası; environment variable veya secret store ile verilir |
| `OAuth:EncryptionCertificatePath` | Development dışındaki ortamlarda private key içeren ayrı şifreleme PFX dosyasının tam yolu |
| `OAuth:EncryptionCertificatePassword` | Şifreleme PFX parolası; environment variable veya secret store ile verilir |

Environment variable adlarında `:` yerine `__` kullanılır. Development ortamında
issuer varsayılanı `https://localhost:7187/` olur; farklı adres kullanılıyorsa
`OAuth__Issuer` açıkça ayarlanmalıdır. Yerel sertifikalar kullanıcı sertifika
deposunda saklanır. Development dışında issuer ve iki PFX dosyası zorunludur;
eksik ayarlarda uygulama başlangıçta hata verir.

Gerçek OAuth girişini yerelde görmek için uygulamayı **Release** yapılandırması ve
`https` launch profiliyle çalıştırın. Debug derlemesinde önceki `RoleAuthorize`
tercihi gereği MCP anonim erişilebilir; istemci OAuth girişini başlatmayabilir.
ChatGPT, Codex veya Claude gibi dış istemcilere bağlanırken MCP adresi dışarıdan
erişilebilir HTTPS adresi olmalı ve `OAuth:Issuer` aynı kök adresi göstermelidir.
Burada henüz OAuth Client ID Metadata Documents yayınlama/alma desteği yoktur;
istemciler dinamik kaydı veya önceden verilmiş client ID bilgilerini kullanabilir.

`OAuthFlowTests`, gerçek MCP SDK istemcisinin otomatik keşif, dinamik kayıt, giriş
ve rapor çağrısını; ayrıca PKCE, callback, form koruması, rol, oturum iptali,
refresh rotation ve yeniden başlatma sonrası kalıcılığı denetler. Testler Northwind
verilerini değiştirmeden ayrı geçici SQLite dosyaları kullanır. OAuth zorunluluğunu
da denetlemek için testleri `dotnet test UluBilge.slnx --configuration Release`
komutuyla çalıştırın.

## JWT ve kullanıcı girişi

Yerel geliştirmede `admin` kullanıcı adı ve `admin` parolasıyla giriş yapılabilir:

```http
POST https://localhost:7187/v1/Auth/login
Content-Type: application/json

{"userName":"admin","password":"admin"}
```

Yanıttaki `accessToken` değerini Scalar `/docs` ekranındaki **Bearer** alanına
yapıştırın veya isteklerde `Authorization: Bearer <accessToken>` başlığı gönderin.
`GET /v1/Auth/me` mevcut kullanıcıyı ve yetkilerini döndürür. Tokenlar URL veya
cookie üzerinden kabul edilmez; parola token içine yazılmaz.

`DatabaseUserAuthenticationService`, kullanıcıyı `dbo.Users` tablosunda doğrular.
Parolalar IdentityV3 PBKDF2 HMAC-SHA512 ve 210.000 iterasyonla hash'lenir.
Beş hatalı denemede hesap 15 dakika kilitlenir. Kullanıcı aktifliği, rolü,
security stamp ve kalıcı oturum kaydı token kullanılırken de denetlenir.
OAuth giriş formu aynı servisi kullandığı için bu kurallar iki giriş türünde ortaktır.

JWT ayarları `Jwt:Issuer`, `Jwt:Audience` ve `Jwt:AccessTokenLifetimeMinutes`
alanlarıdır. Varsayılan ömür 5 dakika, izin verilen aralık 1–15 dakikadır.
HS256 imzası, issuer, audience, `at+jwt` türü, süre ve zorunlu kimlik claim'leri
doğrulanır; saat sapması toleransı 15 saniyedir. Tüm yeni uç noktalar varsayılan
olarak kimlik doğrulama gerektirir; anonim giriş ve Development dokümantasyonu
açıkça istisnadır. Geçersiz token 401, yetersiz yetki 403 döndürür.

İmzalama anahtarı repoda bulunmaz. Development'ta anahtar verilmezse süreç başına
kriptografik olarak rastgele geçici bir anahtar üretilir; API yeniden başladığında
eski tokenlar geçersiz olur. Geliştirme anahtarını kalıcı yapmak için PowerShell 7
ile user-secrets içine yazabilirsiniz:

```powershell
$jwtSigningKey = [Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
dotnet user-secrets set "Jwt:SigningKeyBase64" $jwtSigningKey --project src/UluBilge.Api
Remove-Variable jwtSigningKey
```

Development dışında `Jwt__SigningKeyBase64` ortam değişkeni veya bir secret
store üzerinden 32–128 rastgele byte'ın Base64 karşılığını sağlayın. Ortamlar
arasında farklı anahtar, issuer ve audience kullanın. Boş, bozuk veya kısa anahtar
uygulamanın açılmasını engeller; sessizce başka anahtara geçilmez. Anahtar
değiştirildiğinde mevcut tokenlar geçersiz olur.

Girişte IP başına dakikada 5, süreç genelinde dakikada 20 deneme sınırı vardır;
aşımda `Retry-After` başlığıyla 429 döner. Giriş gövdesi 4 KB ile sınırlıdır.
Token yanıtları önbelleklenmez, doğrulama hataları ayrıntı sızdırmaz. Development
dışında HTTPS zorunludur. Reverse proxy kullanılırsa yalnızca bilinen proxy'lerden
gelen forwarded header'lar yapılandırılmalıdır; uygulama bunlara kendiliğinden
güvenmez. Birden çok API örneği için giriş sınırı ve hesap kilitleme DB/gateway
katmanında ortak uygulanmalıdır.

`POST /v1/Auth/refresh`, JSON gövdesindeki `refreshToken` ile API tokenlarını
yeniler; refresh tokenları döndürülerek değiştirilir ve tekrar kullanımları
tespit edilir. `POST /v1/Auth/logout`, Bearer tokenın oturum ailesini iptal eder.
Bu API adresleri mevcut camelCase JSON sözleşmesini korur; OAuth istemcileri
form gövdeli `/oauth/token` ve standart snake_case OAuth yanıtlarını kullanır.
Tarayıcı istemcisi tokenı kalıcı `localStorage` yerine bellekte tutmalıdır.

Güvenlik testleri Northwind bağlantısı kullanmadan gerçek JWT/OAuth doğrulayıcıları
ve geçici OAuth deposuyla giriş ve yetkilendirme akışlarını denetler:

```powershell
dotnet test UluBilge.slnx
```

## Yeni özellik ekleme

Entity'yi Core'a, repository arayüzünü `Data/Abstract` içine ve uygulamasını
`Data/Repository` içine ekleyin. App içinde `Commands/<Özellik>Handler` veya
`Queries/<Özellik>Handler` altında istek, sonuç ve handler tanımlayın.
App assembly'sinde `IAsyncCommandHandler<,>` veya `IAsyncQueryHandler<,>` uygulayan
public handler'lar ile `UluBilge.Data.Repository` namespace'i altındaki public
repository'ler otomatik kaydedilir. Doğrulama, bağlantı seçimi ve transaction
decorator'larla uygulanır. Kayıt kuralları API projesindeki
`Extentions/ApplicationServiceCollectionExtensions.cs` dosyasındadır; `Program.cs`
içinden `AddUluBilgeServices` ile çağrılır. JWT yapılandırması aynı klasördeki
`JwtServiceCollectionExtensions.cs` üzerinden `AddApiAuthentication` ile yapılır.
