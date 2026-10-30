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

HTTP transport varsayılan olarak stateless çalışır. Geçerli MCP token'ıyla gelen
`GET /mcp` ve `DELETE /mcp` istekleri `405 Method Not Allowed` ve `Allow: POST`
alır. Bu istekler de MCP policy'siyle doğrulanır; böylece genel REST JWT kuralı
yanlış bir 401 üreterek `mcp-remote` köprüsünü tekrar girişe yönlendirmez.

## MCP OAuth: ChatGPT ve Claude

OAuth servisleri, Minimal API endpoint'leri ve token yardımcıları API projesindeki
`Extentions/McpOAuthExtensions.cs` dosyasındadır. `Program.cs` içinde `AddMcpOAuth`
servisleri hazırlar, `MapMcpOAuth` OAuth ve korumalı MCP endpoint'lerini kaydeder.
Kullanıcı adı/parola doğrulaması mevcut `LoginAsync` command'ını kullanır. MCP
access token'ları mevcut JWT imzalama anahtarıyla üretilir; issuer ve audience
değerleri MCP kaynağına özeldir.

| Adres | Görevi |
| --- | --- |
| `/.well-known/oauth-protected-resource/mcp` | MCP kaynağını ve yetkilendirme sunucusunu bildirir |
| `/.well-known/oauth-authorization-server` | OAuth endpoint'lerini, scope ve PKCE desteğini bildirir |
| `POST /oauth/register` | Public istemciyi tam dönüş adresleriyle kaydeder |
| `GET /oauth/authorize` | İstemciyi doğrular; giriş ve izin formunu gösterir |
| `POST /oauth/login` | Form güvenliğini ve kullanıcıyı doğrular; tek kullanımlık kod üretir |
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
client secret boş bırakılır, PKCE `S256` zorunludur. Bu sade akış yalnızca public
istemcileri destekler. Dinamik kayıt destekleyen istemciler `/oauth/register`
üzerinden `redirect_uris` ve isteğe bağlı `client_name` göndererek client ID alır.
Kayıt dönüş adresleri HTTPS olmalıdır; yalnızca Development ortamında HTTP
loopback IP adresleri de kabul edilir. Yetkilendirme sırasında dönüş adresi,
kaydedilmiş tam adreslerden biriyle birebir eşleşmelidir.

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

Authorization code ve refresh token'lar rastgele üretilir; bellekte yalnızca
SHA-256 özetleri tutulur. Kodlar 2 dakika geçerlidir ve eş zamanlı isteklerde de
yalnızca bir kez tüketilir. Süresi dolmuş kayıtlar OAuth isteklerinde temizlenir.
Dinamik istemci kayıtları da bellektedir; en fazla 1000 toplam istemci kabul edilir.
Bu yapı tek uygulama süreci içindir: yeniden başlatmada, Production dahil, yeniden
giriş gerekir; dinamik istemciler tekrar kaydolur. Mevcut access token'lar, aynı
imzalama anahtarı kullanılıyorsa süreleri bitene kadar geçerlidir. Çoklu instance
ve yeniden başlatmalar arasında oturum devamlılığı için ortak kalıcı kayıt gerekir.

Production'da mevcut `Jwt:SigningKeyBase64` değerini secret store veya ortam
değişkeni üzerinden verin. OAuth için ayrıca veritabanı veya imzalama/şifreleme
sertifikası gerekmez; sunucu HTTPS üzerinden çalışır. Refresh token iptali mevcut
access token'ları anında iptal etmez; bu token'lar kısa geçerlilik süreleri boyunca
kullanılabilir.

### Claude Desktop ile yerel debug

Claude Desktop'ta **Settings → Developer → Edit config** ile açılan yerel MCP
yapılandırması kullanılır. Windows'ta dosya genellikle
`%APPDATA%\Claude\claude_desktop_config.json` adresindedir; **Edit config** ile
açılan dosyayı düzenleyin. `mcpServers`, dosyanın kökünde `preferences` ile aynı
seviyede olmalıdır. Mevcut `preferences` değerlerini ve diğer MCP kayıtlarını
koruyarak şu kaydı ekleyin; örnekteki boş `preferences` yalnızca yerleşimi gösterir:

API’ye ulaşılıyor, ancak Node.js yerel HTTPS sertifikasına güvenmiyor.
Önce terminalde geliştirme sertifikasını güvenilir yapın:
```
dotnet dev-certs https --trust
New-Item -ItemType Directory -Force "$env:USERPROFILE\.aspnet\https" | Out-Null
dotnet dev-certs https --export-path "$env:USERPROFILE\.aspnet\https\localhost.pem" --format PEM
```
dotnet dev-certs https --trust, localhost.pem dosyasını oluşturmaz. Bu dosyayı ayrıca dışa aktarmış olmanız gerekiyor.
Test-Path Sonuç True olmalı
```json
 preferences > altına
 "mcpServers": {
    "ulu-bilge-tool": {
      "command": "npx",
      "args": [
        "-y",
        "mcp-remote",
        "https://localhost:7187/mcp",
        "--host",
        "127.0.0.1"
      ],
      "env": {
        "NODE_EXTRA_CA_CERTS": "C:\\Users\\ertug\\.aspnet\\https\\localhost.pem"
      }
    }
  }
```
Bu ayar Node’un Windows’taki güvenilen sertifikaları kullanmasını sağlar. Bilgisayarınızdaki Node.js v24.14.0 bunu destekliyor.
API https profiliyle çalışırken Claude Desktop’ı tamamen kapatıp yeniden açın.

Node.js ve npm bilgisayarda kurulu, `npx` Claude'un erişebildiği `PATH` içinde
olmalıdır. `-y`, paket ilk kez indirilirken etkileşimli onay beklenmesini önler.
`ulu-bilge-tool`, Claude'da görünecek bağlantı adıdır.
Yapılandırma biçimi [MCP'nin Claude Desktop rehberinde](https://modelcontextprotocol.io/docs/develop/connect-local-servers)
ve [mcp-remote kullanım örneğinde](https://github.com/geelen/mcp-remote#usage) açıklanır.

Bu projede `--host 127.0.0.1` gereklidir: `mcp-remote` varsayılan olarak
`localhost` dönüş adresi üretir; uygulama Development ortamında HTTP dönüş
adresleri için loopback IP ister. Köprü `/oauth/register` üzerinden dinamik
istemci kaydı yapar. `resource` ve `mcp:tools offline_access` kapsamları sunucunun
metadata belgelerinden bulunur. Bu örnekte sabit client ID veya callback portu
tanımlamak gerekmez. [mcp-remote bayrakları](https://github.com/geelen/mcp-remote#flags)

API'yi Visual Studio'da **https** profiliyle F5 kullanarak veya şu komutla başlatın:

```powershell
dotnet run --project src/UluBilge.Api --launch-profile https
```

Bağlantı `Claude Desktop → mcp-remote → https://localhost:7187/mcp` şeklindedir;
`mcp-remote`, çalışan API'ye bağlanır. API'nin Development ortamında çalışması
yerel HTTP callback için gereklidir; `/mcp` HTTPS, OAuth, PKCE, scope ve rol
denetimlerini kullanır.

Node.js yerel HTTPS sertifikasına güvenmiyorsa, geliştirme sertifikasının açık
PEM kopyasını oluşturun ve `ulu-bilge-tool` nesnesine, `command` ve `args` ile aynı
seviyede `"env": { "NODE_EXTRA_CA_CERTS": "C:\\tam\\yol\\localhost.pem" }` ekleyin.
Yolu kendi bilgisayarınızdaki gerçek, mutlak PEM yoluyla değiştirin.
Açık sertifika dışa aktarma örneği:

```powershell
dotnet dev-certs https --export-path "C:\tam\yol\localhost.pem" --format PEM
```

Hedef klasör önceden mevcut olmalıdır.
[Sertifika dışa aktarma](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-dev-certs#options)
ve [Node.js ek sertifika ayarı](https://nodejs.org/api/cli.html#node_extra_ca_certsfile)
ilgili araçların belgelerinde açıklanır.

Kaydettikten sonra Claude Desktop'ı tamamen kapatıp yeniden açın. İlk bağlantıda
tarayıcıdaki UluBilge formunda giriş yapıp izin verin; kullanıcının `Report` veya
`Admin` rolü olmalıdır. Ardından Claude'dan `get_product_sales` aracını kullanmasını
isteyin. API yeniden başlatıldığında bellekteki istemci ve token kayıtları silinir;
bağlantının yeniden yetkilendirilmesi gerekir. Eski istemci bilgisi önbellekte
kalırsa yalnızca bu sunucuya ait `mcp-remote` kaydını yenileyin. Köprünün varsayılan
OAuth önbelleği kullanıcı dizinindeki `.mcp-auth` klasöründedir.

Auth ve Region controller testleri çalışan API'ye HTTP isteği gönderir.
Başlatma ve çalıştırma adımları [test projesinin açıklamasındadır](test/UluBilge.Test/README.md).

## JWT ve kullanici girisi

Giris ve token islemleri App katmanindadir. `JwtBearer` paket referansi da
App projesindedir; API controller'i yalnizca command/query handler'larini cagirir.

| Dosya | Gorevi |
| --- | --- |
| `App/Commands/AuthHandler/LoginAsync.cs` | Kullanici/parola kontrolu ve token ciftinin olusturulmasi |
| `App/Commands/AuthHandler/RefreshTokenAsync.cs` | Refresh token dogrulamasi, rotasyon ve tekrar kullanimda iptal |
| `App/Authentication/AuthTokenService.cs` | JWT ve rastgele refresh token uretimi |
| `App/Queries/AuthHandler/MeAsync.cs` | Dogrulanmis token claimlerinden kullanici bilgisi |
| `App/Authentication/JwtOptions.cs` | Issuer, audience, imzalama anahtari ve token suresi |
| `App/Extensions/JwtServiceCollectionExtensions.cs` | JWT dogrulamasi ve servis kaydi |
| `Api/Authentication/RoleAuthorizeAttribute.cs` | Standart rol claimleriyle coklu rol ve Debug davranisi |

`POST /v1/Auth/Login`, JSON govdesindeki `userName` ve `password` ile
`dbo.Users` tablosundaki kullaniciyi dogrular. Parolalar Identity PasswordHasher
ile kontrol edilir. Bes hatali denemede hesap on bes dakika kilitlenir; ayrica
IP basina dakikada bes giris denemesi kabul edilir.

Basarili yanitin `data` nesnesi `accessToken`, `tokenType`, `expiresIn`,
`expiresAtUtc`, `refreshToken` ve `refreshTokenExpiresAtUtc` alanlarini icerir.
Tokeni `Authorization: Bearer <accessToken>` basligiyla gonderin.
`GET /v1/Auth/Me`, `data` icinde tokenin kullanici numarasi,
adi ve rolunu dondurur; kullanicinin aktiflik bilgisi veritabanindan okunur.
Parola tokena yazilmaz; token URL veya cookie uzerinden alinmaz.

JWT'nin HS256 imzasi, issuer, audience ve gecerlilik suresi yerlesik JwtBearer
middleware'iyle dogrulanir. Token omru varsayilan bes dakika, izin verilen aralik
1-15 dakikadir; saat farki toleransi on bes saniyedir. Token dogrulamasi her
istekte veritabanina gitmez. Release derlemesinde gecersiz token 401, yetersiz rol
403 sonucunu verir. Onceki istek uzerine Debug derlemesinde `RoleAuthorize`
kullanilan yerlerde anonim erisim devam eder.

Ayarlar `Jwt:Issuer`, `Jwt:Audience`, `Jwt:AccessTokenLifetimeMinutes`,
`Jwt:RefreshTokenLifetimeDays` ve `Jwt:SigningKeyBase64` alanlaridir.
Signing key, en az 32 rastgele baytin Base64
karsiligi olmalidir. Development disinda anahtar zorunludur; ortam degiskeni veya
secret store uzerinden verin. Ornegin ortam degiskeni adi
`Jwt__SigningKeyBase64` olur. Development'ta anahtar verilmezse surece ozel
rastgele anahtar kullanilir; yeniden baslatmada onceki tokenlar gecersiz olur.
HTTPS zorunlulugu Development disinda korunur.

Access token suresi doldugunda `POST /v1/Auth/RefreshToken` istegini su govdeyle gonderin;
bu endpoint Bearer token gerektirmez:

```json
{
  "refreshToken": "giris-veya-onceki-yenileme-yanitindaki-refreshToken"
}
```

Yanit girisle ayni `data` alanlarini icerir. Her basarili yenilemeden sonra iki
tokeni de yenileriyle degistirin. Refresh token 32 rastgele bayttan uretilir;
`dbo.UserTokens` tablosunda yalnizca SHA-256 ozeti saklanir. Mevcut tablo semasi
yeterlidir, yeni migration gerekmez. Varsayilan refresh token suresi ilk giristen
itibaren 7 gundur; `Jwt:RefreshTokenLifetimeDays` ile 1-30 gun arasinda ayarlanir.
Rotasyon bu son tarihi uzatmaz. API yeniden baslasa da refresh token kayitlari korunur.

Yenileme ve eski tokenin tuketilmesi ayni transaction'dadir. Ayni kullaniciya ait
token islemleri kullanici satiri kilidiyle siralanir. Kullanilmis bir tokenin
tekrar gonderilmesi ilgili `FamilyId` zincirini iptal eder; diger girislerin
zincirleri etkilenmez. Bu davranis [RFC 9700 rotasyon yaklasimini](https://www.rfc-editor.org/rfc/rfc9700.html#section-4.14.2)
izler. Yenileme isteklerini istemcide sirayla gonderin; es zamanli ayni token
kullanimi da tekrar kullanim sayilir ve yeniden giris gerektirir.

Gecersiz, suresi dolmus veya iptal edilmis token `401`, eksik veya bozuk bicimli
govde `400` doner. IP basina dakikada 30 yenileme istegi kabul edilir.
Yenilemede kullanicinin aktifligi, kilit durumu ve guncel rolleri veritabanindan
okunur. Sunucu tarafli REST logout endpoint'i yoktur. Parola/SecurityStamp
degisikliginde token zincirlerini iptal etmek icin ilgili islemde `UserTokens`
kayitlari da iptal edilmelidir. Zincir iptali mevcut access tokeni aninda iptal
etmez; access token kendi suresi ve saat toleransi boyunca kullanilabilir.
REST refresh tokenlari ile MCP OAuth refresh tokenlari ayri akislardir.

Bruno koleksiyonunda `AuthController` altinda **Login → RefreshToken → Me**
sirasi kullanilabilir. Login ve RefreshToken, `accessToken` ve `refreshToken`
ortam degiskenlerini otomatik gunceller.

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
