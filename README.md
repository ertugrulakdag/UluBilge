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

Release derlemelerinde MCP adresi de Bearer token ve `Report` veya `Admin`
rollerinden birini gerektirir. Debug derlemelerinde mevcut `RoleAuthorize`
davranışıyla anonim erişilebilir.

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
adi ve rolunu dondurur. `MeAsync.Query`, `RequiresDatabase = false` ile
veritabanina baglanmadan calisir; diger query'lerde varsayilan deger `true`'dur.
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

Refresh token, oturum ailesi, her istekte oturum sorgusu ve sunucu tarafli
logout kaldirildi. Cikista istemci tokeni siler; suresi doldugunda yeniden giris
yapilir. Kullanici kapatma, parola veya rol degisikligi mevcut tokeni aninda
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
