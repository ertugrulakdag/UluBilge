# UluBilge MCP sunucusu

`src/UluBilge.McpServer`, mevcut UluBilge API'ye HTTP üzerinden bağlanan .NET 10 stdio MCP sunucusudur. İlk araç `get_regions`, `AuthController` ile giriş yapar ve aldığı Bearer tokenla `RegionController.GetRegionsAsync` işlemini çağırır.

Bir MCP süreci yapılandırılan tek API kullanıcısını temsil eder. Araç çıktısı, bağlı MCP istemcisinin ekranında okunabilir liste ve yapılandırılmış JSON olarak gösterilir.

## Gereksinimler

- .NET 10 SDK.
- Çalışan UluBilge API ve API'nin erişebildiği Northwind veritabanı; Users/UserTokens tabloları da hazır olmalı.
- API'de aktif, Admin rolünü içeren bir kullanıcı.
- HTTPS kullanıldığında güvenilir API sertifikası. Geliştirme sertifikası gerektiğinde `dotnet dev-certs https --trust` ile hazırlanabilir.
- Grafik MCP Inspector için desteklenen Node.js sürümü. Güncel Inspector rehberi Node.js 22.19.0 veya üzerini istiyor; C# MCP sunucusunun kendisi Node.js gerektirmez. [Inspector gereksinimleri](https://modelcontextprotocol.io/docs/2026-07-28/tools/inspector)

Sunucu otomatik migration çalıştırmaz. Mevcut veritabanının hazırlanması için [ana README](../README.md) içindeki migration açıklamasını izleyin.

## 1. API'yi çalıştırma

Repo kökünde, ayrı bir terminalde:

```powershell
dotnet run --project src/UluBilge.Api --launch-profile https
```

Mevcut profile göre API adresi `https://localhost:7187/` olur. Development ortamında API dokümantasyonu `https://localhost:7187/docs` adresindedir.

## 2. MCP kimlik bilgilerini yapılandırma

Geliştirme için MCP projesinin kendi user-secrets deposunu kullanın. Bu depo API projesinin JWT ayarlarından ayrıdır. Aşağıdaki PowerShell örneği parolayı ekranda göstermeden okur ve komut satırı argümanına yerleştirmeden JSON üzerinden kaydeder:

```powershell
$mcpUserName = Read-Host "API kullanıcı adı"
$mcpSecurePassword = Read-Host "API parolası" -AsSecureString
$mcpCredential = [System.Management.Automation.PSCredential]::new($mcpUserName, $mcpSecurePassword)
$mcpSettings = @{
    "UluBilgeApi:UserName" = $mcpUserName
    "UluBilgeApi:Password" = $mcpCredential.GetNetworkCredential().Password
}
$mcpSettings | ConvertTo-Json | dotnet user-secrets set --project src/UluBilge.McpServer
Remove-Variable mcpSettings, mcpCredential, mcpSecurePassword, mcpUserName
```

User-secrets yalnızca Development ortamında yüklenir. İstemci başlatma örneklerinde bu nedenle `--environment Development` kullanılıyor.

Diğer ortamlarda kimlik bilgilerini güvenli yapılandırmadan şu ortam değişkenlerine sağlayabilirsiniz:

| Ortam değişkeni | Açıklama |
| --- | --- |
| `UluBilgeApi__BaseUrl` | API adresi; varsayılan `https://localhost:7187/`. API bir yol öneki altında barındırılıyorsa adres bu öneki içerebilir. |
| `UluBilgeApi__UserName` | API kullanıcısı. |
| `UluBilgeApi__Password` | API parolası. |
| `UluBilgeApi__TimeoutSeconds` | İstek zaman aşımı; varsayılan 30, izin verilen aralık 1–120 saniye. |

Öncelik: `appsettings.json` → Development user-secrets → ortam değişkenleri → komut satırı ayarları. Gerçek parolayı komut satırında veya repoya eklenecek istemci dosyalarında tutmayın.

Yapılandırma uygulamanın çıktı dizininden yüklenir; istemcinin çalışma dizini repo kökü olmak zorunda değildir. HTTPS sertifika denetimi korunur, otomatik HTTP yönlendirmeleri ve cookie kullanımı kapalıdır. Development ortamında yalnızca loopback adreslerine HTTP bağlantısı kabul edilir; örneğin `http://localhost:5156/`.

## 3. Derleme ve test

```powershell
dotnet build UluBilge.slnx
dotnet test tests/UluBilge.McpServer.Tests/UluBilge.McpServer.Tests.csproj
```

Testler gerçek veritabanı gerektirmez. HTTP sözleşmeleri, giriş ve refresh akışları, eş zamanlılık, iptal ve hata davranışları sınanır. Ayrıca ayrı MCP sunucu sürecine resmi C# MCP istemcisiyle bağlanılarak araç keşfi, varsayılan/sayfalı çağrılar ve metin/JSON çıktısı doğrulanır. Bu iletişim testlerinde API'yi yerel test sunucusu temsil eder.

## 4. Inspector ile ekranda gösterme

Repo kökünde, kimlik bilgileri kaydedildikten ve solution derlendikten sonra:

```powershell
$mcpServerDll = (Resolve-Path ./src/UluBilge.McpServer/bin/Debug/net10.0/UluBilge.McpServer.dll).Path
npx @modelcontextprotocol/inspector dotnet $mcpServerDll --environment Development
```

Inspector ekranında:

1. Stdio bağlantısını başlatın ve araç listesini yükleyin.
2. `get_regions` aracını seçin.
3. İlk çağrıda `pageNumber=1`, `pageSize=20` kullanın; alanları boş bırakırsanız aynı varsayılanlar uygulanır.
4. Sonuçtaki metin bölümünde bölge listesi, toplam kayıt ve sayfa bilgilerini inceleyin.
5. Yapılandırılmış içerikte `items`, `total`, `pageNumber`, `pageSize` alanlarını görün.

İlk başarılı çağrıdan sonra token geçerliyken tekrar login yapılmaz. Araç keşfi sırasında da API'ye login isteği gönderilmez.

MCP sürecini terminalde tek başına başlatmak kullanıcı menüsü açmaz; süreç bir MCP istemcisinden protokol mesajı bekler. Kullanıcıya gösterilecek liste istemcinin sonuç ekranına döner. Sunucu logları `stderr`, MCP mesajları `stdout` üzerinden iletilir.

## 5. Diğer MCP istemcileri

Stdio sunucusu destekleyen bir istemci için genel yapılandırma örneği:

```json
{
  "mcpServers": {
    "ulubilge": {
      "command": "dotnet",
      "args": [
        "C:/PROJE_KONUMU/UluBilge/src/UluBilge.McpServer/bin/Debug/net10.0/UluBilge.McpServer.dll",
        "--environment",
        "Development"
      ],
      "env": {
        "UluBilgeApi__BaseUrl": "https://localhost:7187/"
      }
    }
  }
}
```

`C:/PROJE_KONUMU` bölümünü gerçek mutlak konumla değiştirin. İstemcinin yapılandırma biçimi farklıysa aynı komut, argüman ve ortam ayarlarını o istemcinin stdio sunucu kaydına girin. Geliştirme kimlik bilgileri MCP projesinin user-secrets deposundan alınır.

## 6. Araç sözleşmesi ve çıktı

```text
get_regions(pageNumber = 1, pageSize = 20)
```

Sayfa numarası en az 1, sayfa boyutu 1–100 olabilir. Geçersiz değerlerde API isteği yapılmaz. API adresi veya kimlik bilgileri araç parametresi değildir.

Aşağıdaki örnek, 30.10.2026 tarihinde gerçek API'ye MCP istemcisi üzerinden yapılan çağrıda alınan yerel Northwind sonucudur. Kayıtlar veritabanına göre değişebilir:

```text
Toplam kayıt: 4 | Sayfa: 1 | Sayfa boyutu: 20

| Bölge numarası | Açıklama |
| --- | --- |
| 1 | Eastern |
| 2 | Western |
| 3 | Northern |
| 4 | Southern |
```

Yapılandırılmış sonuç:

```json
{
  "items": [
    { "regionId": 1, "regionDescription": "Eastern" },
    { "regionId": 2, "regionDescription": "Western" },
    { "regionId": 3, "regionDescription": "Northern" },
    { "regionId": 4, "regionDescription": "Southern" }
  ],
  "total": 4,
  "pageNumber": 1,
  "pageSize": 20
}
```

## 7. Token ve hata davranışı

- Giriş ve refresh tokenları süreç belleğinde tutulur. Araç çıktısında ve loglarda gösterilmez.
- Access tokenın süresi dolmadan 30 saniye önce yenileme yapılır. Dönen yeni token çifti birlikte saklanır.
- Aynı süreçte giriş ve refresh istekleri koordine edilir; eş zamanlı çağrılar aynı refresh tokenı tekrar tüketmez.
- Oturum iptalini belirten `401` sonrasında yeniden login ile istek sessizce tekrarlanmaz. Kimlik bilgilerini kontrol ederek MCP sürecini yeniden başlatın.
- Refresh isteği iptal edilirse veya yanıtı kaybolursa oturum yeniden başlatılmalıdır. Böylece tüketilmiş olabilecek refresh token tekrar gönderilmez.
- `403`, API kullanıcısının gerekli Admin yetkisine sahip olmadığını belirtir.
- `429` yanıtında varsa bekleme süresi gösterilir; otomatik retry yapılmaz. Bekleyip aracı tekrar çağırabilirsiniz.
- Bağlantı, zaman aşımı ve JSON sözleşme hataları araç hatası olarak döner. Başarısız işlem boş liste gibi gösterilmez.
- API hata yanıtındaki güvenli izleme kimliği korunur; ham hata gövdesi ve title/detail alanları aktarılmaz.

MCP sürecinin kapatılması API'ye logout isteği göndermez. Oluşan oturum API'nin kendi bitiş ve iptal kurallarına tabidir. İlk sürümde ayrı login/logout MCP aracı bulunmaz.

Bu entegrasyon API'nin mevcut rol ve oturum kontrollerini kullanır. Önceki analizdeki SecurityStamp/refresh sınırlaması için [analiz belgesinin son bölümüne](mcpserver-analiz.md#12-mevcut-api-ile-ilgili-sınırlar-ve-onaya-sunulan-karar) bakın.
