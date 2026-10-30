# Controller testleri

Auth ve Region testleri `HttpClient` ile çalışan API'ye istek gönderir. API'nin bağlı olduğu mevcut veritabanı kullanılır.

Önce API'yi Visual Studio'dan HTTPS profiliyle veya şu komutla başlatın:

```powershell
dotnet run --project src/UluBilge.Api --launch-profile https
```

Ardından ayrı bir terminalde testleri çalıştırın:

```powershell
dotnet test test/UluBilge.Test/UluBilge.Test.csproj -c Release --filter "FullyQualifiedName~AuthControllerTests|FullyQualifiedName~RegionControllerTests"
```

Varsayılan adres `https://localhost:7187`, giriş bilgileri `admin / admin`. Kullanıcı mevcut veritabanında bulunmalı ve Admin veya Region rolüne sahip olmalıdır. Farklı bilgiler için:

```powershell
$env:ULUBILGE_TEST_URL = "https://localhost:7187"
$env:ULUBILGE_TEST_USERNAME = "admin"
$env:ULUBILGE_TEST_PASSWORD = "admin"
```

- `AuthControllerTests`: Giriş, geçersiz/boş bilgiler, token ile kullanıcı bilgisi ve eksik/geçersiz token kontrolü.
- `RegionControllerTests`: Tek test içinde **ekle → güncelle → listele → ID ile getir → sil** sırası izlenir. Sonrasında ID ile sorgulama `404` dönmelidir. Bir adım başarısız olursa testin oluşturduğu kayıt yine API üzerinden silinir.
- `ApiTestClient`: API adresini, giriş işlemini ve HTTP istemcisini içerir.

API'nin giriş sınırı dakikada 5 istektir. Bu testler toplam 5 giriş isteği gönderir; arka arkaya çalıştırırken bir dakika ara verin.

`McpTransportAuthenticationTests`, API'nin gerçek OAuth ve MCP servis kayıtlarını
`TestServer` üzerinde çalıştırır. SQL Server bağlantısı açmadan, geçerli MCP token'ıyla
`initialize` ve `tools/list` isteklerini, stateless GET/DELETE için 405 yanıtını,
anonim isteklerin ve REST API token'larının reddedilmesini kontrol eder. Böylece
desteklenmeyen GET isteğinin istemciyi yeniden OAuth girişine yönlendirmesi önlenir:

```powershell
dotnet test test/UluBilge.Test/UluBilge.Test.csproj -c Release --filter FullyQualifiedName~McpTransportAuthenticationTests
```
