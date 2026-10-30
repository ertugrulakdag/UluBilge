# UluBilge

.NET 10, SQL Server Northwind ve Dapper kullanan CQRS API. Command/Query, handler ve controller düzenini izler.

## Katmanlar

- **UluBilge.Api:** Controller'lar, HTTP yanıtları, merkezi Problem Details hata yönetimi ve OpenAPI.
- **UluBilge.App:** Command/Query handler'ları, doğrulama ve Unit of Work decorator'ları.
- **UluBilge.Data:** Repository arayüzleri, Dapper repository'leri, bağlantı ayarları ve Unit of Work.
- **UluBilge.Core:** Entity, sayfalama modeli ve uygulamanın kendi CQRS sözleşmeleri.
- **UluBilge.Migration:** Northwind SQL scriptlerini yazma bağlantısıyla çalıştıran konsol uygulaması.
- **UluBilge.McpServer:** Mevcut API'ye AuthController ile giriş yapan ve Region listesini MCP istemcilerine sunan stdio sunucusu.

Bağımlılık yönü `Api → App → Data → Core` şeklindedir.
MCP sunucusu API'ye HTTP üzerinden bağlanır; bu katmanlara doğrudan proje referansı vermez.

## MCP ile bölge listeleme

`src/UluBilge.McpServer` projesindeki `get_regions(pageNumber=1, pageSize=20)` aracı,
AuthController üzerinden login/refresh yaparak Region listesini getirir. Sonuç,
MCP Inspector veya bağlı istemcide metin ve yapılandırılmış JSON olarak gösterilir.
API kullanıcısının Admin rolüne sahip olması gerekir; kimlik bilgileri user-secrets
veya ortam değişkenleriyle sağlanır.

Kurulum, Inspector komutu ve istemci yapılandırması: [MCP kullanım belgesi](docs/mcpserver-kullanim.md).
Mimari kararlar: [MCP analizi](docs/mcpserver-analiz.md).

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

Bütün Region uç noktaları Bearer access token ve `RoleEnum.Admin` rolünü gerektirir.

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

## JWT ve veritabanı oturumları

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

Kimlik doğrulama bütün ortamlarda `DatabaseUserAuthenticationService` ve
`dbo.Users` tablosu üzerinden yapılır. Örnek admin hesabı kullanıcı migration'ıyla
oluşturulur. Parolalar IdentityV3 PasswordHasher ve 210.000 iterasyonla doğrulanır.
Beş başarısız girişte hesap 15 dakika kilitlenir. Kullanıcının aktifliği, rolü ve
oturumu API tarafından kontrol edilir.

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

Giriş yanıtı access tokenla birlikte refresh token döndürür. `POST /v1/Auth/refresh`
mevcut refresh tokenı tüketerek yeni bir token çifti oluşturur; zincirin mutlak
bitiş tarihi uzatılmaz. Refresh tokenın tekrar kullanılması token ailesini iptal
eder. `POST /v1/Auth/logout` Bearer tokenın bağlı olduğu oturum ailesini iptal
eder. Refresh tokenlar veritabanında SHA-256 özetiyle saklanır. Her korumalı
istekte oturumun geçerliliği veritabanından denetlenir.

MCP testleri SQL Server gerektirmeden HTTP sözleşmelerini, token yönetimini ve
ayrı süreçler arasındaki gerçek MCP iletişimini doğrular:

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
