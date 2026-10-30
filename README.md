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

## Ortak hata yönetimi

Handler'lar HTTP yanıtı oluşturmaz; hatalar mevcut `ApiExceptionHandler` tarafından
Problem Details biçiminde döndürülür. `NotFoundException` HTTP 404'e,
`ValidationException` HTTP 400'e çevrilir. `MeAsync`, kullanıcı veritabanında
bulunamadığında `NotFoundException` fırlatır; açıklama yanıtın `detail` alanındadır.

API model doğrulaması, alan ve sınıf kurallarındaki birden fazla hatayı `errors`
içinde toplar. Alan hataları olsa da sınıf kuralları çalıştığından `Validate`
metotları eksik veya null alanlara dayanıklı olmalıdır. Decorator'lar yerleşik
`Validator.ValidateObject` kullanır; bu çağrı ilk doğrulama hatasında exception
fırlatır. MCP rapor tool'u bu hatayı bir tool hatasına çevirir.

Exception fırlatılması işlem akışını durdurur ve command transaction'ı geri alınır.
Bağımsız iş kuralı hataları exception fırlatıldıktan sonra otomatik toplanmaz.
Beklenmeyen hatalar merkezi handler'da güvenli bir 500 yanıtına çevrilir;
ayrıntıları istemciye gönderilmez.

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

Migration kendi projesindeki `src/UluBilge.Migration/appsettings.json` dosyasını
kullanır ve `WriteConnection` ile bağlanır; ortam seçimi yapılmaz. Bu dosya derleme
ve yayın çıktısına kopyalanır.
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
1753 ile içinde bulunduğumuz yıl arasındadır ve filtre `Orders.OrderDate` alanına uygulanır. `topCount` isteğe
bağlıdır; gönderilmezse veya null ise 10 kullanılır. Değer verildiğinde 1–100
arasında olmalıdır; yıl belirtilmediğinde üst sınır 50'dir. Bu iki ek örnek iş
kuralı Query'deki `IValidatableObject` ile API ve MCP için ortak doğrulanır.
Hatalı parametreler API'de 400 döndürür.

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

MCP adresi bütün derlemelerde OAuth Bearer token, `mcp:tools` kapsamı ve `Report`
veya `Admin` rollerinden birini gerektirir.

## MCP OAuth: ChatGPT ve Claude

`Program.cs` içinde iki kayıt vardır: `AddMcpOAuth` servisleri hazırlar,
`MapMcpOAuthAsync` endpoint'leri ve onaylı istemcileri kaydeder. Yapı
`MCP/OAuth` klasöründedir. OAuth protokol kontrollerini OpenIddict uygular;
kullanıcı adı/parola doğrulaması mevcut `LoginAsync` command'ını kullanır.

| Adres | Görevi |
| --- | --- |
| `/.well-known/oauth-protected-resource/mcp` | MCP kaynağını ve yetkilendirme sunucusunu bildirir |
| `/.well-known/oauth-authorization-server` | OAuth endpoint'lerini, scope ve PKCE desteğini bildirir |
| `/.well-known/openid-configuration` | Aynı keşif belgesinin OIDC adresi |
| `GET /oauth/authorize` | İstemciyi doğrular; giriş ve izin formunu gösterir |
| `POST /oauth/authorize` | Form güvenliğini ve kullanıcıyı doğrular; tek kullanımlık kod üretir |
| `POST /oauth/token` | Kodu token ile değiştirir veya refresh token ile erişimi yeniler |
| `/mcp` | OAuth token ile araçlara erişim sağlar |

Akış: istemci keşif belgesini okur → kullanıcı giriş yapıp izin verir → istemci
kodu PKCE `S256` doğrulamasıyla token'a çevirir → `/mcp` isteklerini
`Authorization: Bearer ...` ile gönderir. `resource` değeri her iki OAuth isteğinde
de keşif belgesindeki MCP adresiyle aynı olmalıdır.

`appsettings.json` içindeki `OAuth:ServerUrl` başlangıçta `https://localhost:7187`.
Kaynak adresi bunun sonuna `/mcp` eklenerek oluşur. İstemcileri eklerken:

| İstemci | OAuth client ID | Tanımlı callback |
| --- | --- | --- |
| ChatGPT | `ulubilge-chatgpt` | `https://chatgpt.com/connector_platform_oauth_redirect` |
| Claude | `ulubilge-claude` | `https://claude.ai/api/mcp/auth_callback` |

ChatGPT veya Claude'un bağlantı ayarlarında **kendi OAuth istemcini kullan**
seçeneğini ve ilgili client ID'yi girin. Başlangıçtaki istemciler public client'tır;
client secret boş bırakılır, PKCE zorunludur. Client secret kullanmak isterseniz
`OAuth:Clients:0:ClientSecret` veya `OAuth:Clients:1:ClientSecret` değerini secret
store/ortam değişkeninden verin; istemci confidential olur. Secret'ı bağlantı
ayarlarına da girin. Genel `/oauth/register` endpoint'i açılmaz.

Callback adresi istemcinin yönetim sayfasında farklı görünüyorsa, gösterilen
**tam adresi** ilgili `RedirectUris` listesine ekleyin. Joker adres kullanmayın.
Başka istemciler de aynı listeye ayrı client ID ve callback ile eklenebilir.

ChatGPT ve Claude'un bulut bağlantısı `localhost` adresine erişemez. Bağlantı
kurmadan önce sunucuyu erişilebilir HTTPS adresinde yayınlayın ve `OAuth:ServerUrl`
değerini o adresle değiştirin. TLS reverse proxy'de sonlandırılıyorsa ASP.NET Core'un
HTTPS bilgisini yalnızca güvenilen proxy'den alacak şekilde yapılandırılması gerekir.
HTTPS denetimi geliştirmede de kapatılmaz.
Yayında Release derlemesi ve Production ortamı kullanın; mevcut MVC controller'larındaki
`RoleAuthorize` Debug istisnası devam eder. `/mcp` bu istisnayı kullanmaz.

Access token ömrü `Jwt:AccessTokenLifetimeMinutes` ayarından gelir; varsayılan
5 dakikadır. `mcp:tools offline_access` isteyen istemci refresh token da alır.
Refresh token her kullanımda yenilenir; eski token'ın tekrar kullanılması ilgili
yetkilendirmeyi iptal eder. Yenilemede kullanıcı, aktiflik ve roller veritabanından
yeniden kontrol edilir. Kullanıcının `SecurityStamp` değeri değiştiğinde de yeni
giriş gerekir; parola sıfırlama işlemleri bu alanı da güncellemelidir.
Yenileme süresi ilk token'dan itibaren en fazla 7 gündür;
sonra yeniden giriş gerekir. MCP token'ı REST API için kullanılamaz; REST giriş
token'ı da MCP OAuth token'ı yerine kullanılamaz.

OpenIddict kayıtları varsayılan `App_Data/mcp-oauth.db` SQLite dosyasındadır.
Konum `OAuth:DatabasePath` ile değiştirilebilir. Bu dosya gizli yetkilendirme verisi
içerir; HTTP üzerinden sunulmamalı, dosya erişimi uygulama hesabıyla sınırlandırılmalı
ve yedeklenmelidir. SQLite yapılandırması tek sunucu içindir; çoklu instance için
ortak bir OpenIddict veritabanı ve ortak anahtarlar gerekir.

Development anahtarları geçicidir; API yeniden başlatılınca istemcide yeniden
bağlantı kurulmalıdır. Production'da geçici anahtar kullanılmaz. Ayrı imzalama ve
şifreleme PFX sertifikalarını, özel anahtarlarıyla birlikte sağlayın:
`OAuth:SigningCertificatePath`, `OAuth:SigningCertificatePassword`,
`OAuth:EncryptionCertificatePath`, `OAuth:EncryptionCertificatePassword`.
Parolaları ve mevcut `Jwt:SigningKeyBase64` değerini secret store veya ortam
değişkeni üzerinden verin. Sertifikalar, yerel HTTPS geliştirme sertifikasından
ayrıdır ve kaynak koduna eklenmez.

### Claude Desktop ile yerel debug

Claude Desktop'ta **Settings → Developer → Edit config** ile açılan yerel MCP
yapılandırması kullanılır. Bağlantı `Claude Desktop → mcp-remote →
https://localhost:7187/mcp` şeklindedir. API Visual Studio'da **https** profiliyle
F5 kullanılarak başlatılır; köprü ayrı bir API başlatmaz, çalışan debug sürecine
bağlanır.

Node.js ve `mcp-remote` köprüsü her geliştirme bilgisayarında ayrıca kurulur.
Yerel kurulum `mcp-remote` 0.15.2 kullanır. Claude yapılandırmasındaki
`mcpServers.ulubilge-local` kaydı, Node.js ile köprünün `dist/proxy.js` dosyasını
başlatır. Köprüye `--host 127.0.0.1`, `--transport http-only`,
`--resource https://localhost:7187/mcp` ve sabit callback portu `3334` verilir.
`--static-oauth-client-info` dosyası `ulubilge-claude-local` istemcisini,
`--static-oauth-client-metadata` dosyası `mcp:tools offline_access` kapsamını ve
`token_endpoint_auth_method: none` değerini belirtir.

HTTPS geliştirme sertifikasının yalnızca açık PEM kopyası `NODE_EXTRA_CA_CERTS`
ile Node.js'e tanıtılır. Claude yapılandırmasını değiştirirken mevcut
`preferences` ve diğer alanları koruyun. Microsoft Store kurulumunda bu dosya
uygulamanın `LocalCache/Roaming/Claude` dizininde olabilir. Kurulum dosyaları
ve Claude kaydındaki tam yollar makineye özeldir; kaynak koduna eklenmez.

`OAuth:DevelopmentClients` yalnızca Development ortamında kaydedilir. Yerel
istemci `ulubilge-claude-local`, tam dönüş adresi
`http://127.0.0.1:3334/oauth/callback` kullanır; köprü bu portu dinler. HTTP dönüş
adreslerine yalnızca Development ortamında ve loopback IP üzerinde izin verilir.
API'nin `/mcp` adresi HTTPS ve OAuth, PKCE, scope ve rol denetimlerini kullanmaya
devam eder.

API'yi yeniden başlattıktan sonra Claude Desktop'ı tamamen kapatıp açın. İlk
bağlantıda açılan tarayıcı formunda UluBilge kullanıcısıyla giriş yapıp izin verin;
kullanıcının Report veya Admin rolü olmalıdır. Sonra Claude'dan `get_product_sales`
aracını kullanmasını isteyin. API yeniden başlatıldığında geçici anahtarlar da
değişir; bağlantıyı yeniden doğrulamak gerekebilir. Köprü dosyaları, sertifika
kopyası ve OAuth oturumu `.local` altında tutulur ve Git'e eklenmez.

OAuth güvenlik testleri `dotnet test test/UluBilge.Test -c Release --filter FullyQualifiedName~McpOAuthTests` ile çalışır.
Gerçek Northwind bağlantısı kullanılmadan PKCE, callback, CSRF, token bağlamları,
refresh tekrar kullanımı ve rol denetimleri kontrol edilir.

Auth ve Region controller testleri çalışan API'ye HTTP isteği gönderir.
Başlatma ve çalıştırma adımları [test projesinin açıklamasındadır](test/UluBilge.Test/README.md).

## JWT ve kullanici girisi

Giris ve token islemleri App katmanindadir. `JwtBearer` paket referansi da
App projesindedir; API controller'i yalnizca command/query handler'larini cagirir.

| Dosya | Gorevi |
| --- | --- |
| `App/Commands/AuthHandler/LoginAsync.cs` | Kullanici/parola kontrolu ve JWT uretimi |
| `App/Queries/AuthHandler/MeAsync.cs` | Dogrulanmis token claimlerinden kullanici bilgisi |
| `App/Authentication/JwtOptions.cs` | Issuer, audience, imzalama anahtari ve token suresi |
| `App/Extensions/JwtServiceCollectionExtensions.cs` | JWT dogrulamasi ve servis kaydi |
| `Api/Authentication/RoleAuthorizeAttribute.cs` | Standart rol claimleriyle coklu rol ve Debug davranisi |

`POST /v1/Auth/login`, JSON govdesindeki `userName` ve `password` ile
`dbo.Users` tablosundaki kullaniciyi dogrular. Parolalar Identity PasswordHasher
ile kontrol edilir. Bes hatali denemede hesap on bes dakika kilitlenir; ayrica
IP basina dakikada bes giris denemesi kabul edilir.

Basarili yanitin `data` nesnesi `accessToken`, `tokenType`, `expiresIn` ve
`expiresAtUtc` alanlarini icerir. Tokeni `Authorization: Bearer <accessToken>`
basligiyla gonderin. `GET /v1/Auth/me`, `data` icinde tokenin kullanici numarasi,
adi ve rolunu dondurur; kullanicinin aktiflik bilgisi veritabanindan okunur.
Parola tokena yazilmaz; token URL veya cookie uzerinden alinmaz.

JWT'nin HS256 imzasi, issuer, audience ve gecerlilik suresi yerlesik JwtBearer
middleware'iyle dogrulanir. Token omru varsayilan bes dakika, izin verilen aralik
1-15 dakikadir; saat farki toleransi on bes saniyedir. Token dogrulamasi her
istekte veritabanina gitmez. Release derlemesinde gecersiz token 401, yetersiz rol
403 sonucunu verir. Onceki istek uzerine Debug derlemesinde `RoleAuthorize`
kullanilan yerlerde anonim erisim devam eder.

Ayarlar `Jwt:Issuer`, `Jwt:Audience`, `Jwt:AccessTokenLifetimeMinutes` ve
`Jwt:SigningKeyBase64` alanlaridir. Signing key, en az 32 rastgele baytin Base64
karsiligi olmalidir. Development disinda anahtar zorunludur; ortam degiskeni veya
secret store uzerinden verin. Ornegin ortam degiskeni adi
`Jwt__SigningKeyBase64` olur. Development'ta anahtar verilmezse surece ozel
rastgele anahtar kullanilir; yeniden baslatmada onceki tokenlar gecersiz olur.
HTTPS zorunlulugu Development disinda korunur.

REST girisinde refresh token veya sunucu tarafli logout yoktur; bunlar MCP OAuth
akisindan ayridir. Cikista REST istemcisi tokeni siler; suresi doldugunda yeniden
giris yapilir. Kullanici kapatma, parola veya rol degisikligi mevcut REST tokenini aninda
iptal etmez; en fazla token suresi ve saat toleransi sonunda etkili olur.
Onceki tokenlarda farkli rol claim bicimi kullanildigi icin bu degisiklikten
sonra yeniden giris yapilmalidir.

Kullanilmayan `UserToken` entity/repository kodu kaldirildi. Gecmis migration
ve mevcut `UserTokens` tablosu, var olan verileri etkilememek icin silinmedi;
sade giris akisi bu tabloyu kullanmaz.

## Yeni özellik ekleme

Entity'yi Core'a, repository arayüzünü `Data/Abstract` içine ve uygulamasını
`Data/Repository` içine ekleyin. App içinde `Commands/<Özellik>Handler` veya
`Queries/<Özellik>Handler` altında istek, sonuç ve handler tanımlayın.
App assembly'sinde `IAsyncCommandHandler<,>` veya `IAsyncQueryHandler<,>` uygulayan
public handler'lar ile `UluBilge.Data.Repository` namespace'i altındaki public
repository'ler otomatik kaydedilir. Doğrulama, bağlantı seçimi ve transaction
decorator'larla uygulanır. Kayıt kuralları API projesindeki
`Extentions/ApplicationServiceCollectionExtensions.cs` dosyasındadır; `Program.cs`
içinden `AddUluBilgeServices` ile çağrılır. JWT yapılandırması App projesindeki
`Extensions/JwtServiceCollectionExtensions.cs` üzerinden `AddApiAuthentication`
ile yapılır.
