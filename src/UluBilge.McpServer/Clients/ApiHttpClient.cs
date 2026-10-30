using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace UluBilge.McpServer.Clients;

public sealed class ApiHttpClient(IHttpClientFactory clients, TimeProvider timeProvider)
{
    public const string ClientName = "UluBilgeApi";
    internal static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true
    };

    internal async Task<T> SendAsync<T>(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            using var client = clients.CreateClient(ClientName);
            using var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw await CreateErrorAsync(response, cancellationToken);
            }

            return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken)
                ?? throw new ApiException("API boş bir yanıt döndürdü; beklenen yanıt sözleşmesi karşılanmadı.");
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException)
        {
            throw new ApiException("UluBilge API'ye bağlanılamadı. API adresini, HTTPS sertifikasını ve API'nin çalıştığını kontrol edin.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ApiException("UluBilge API isteği zaman aşımına uğradı.");
        }
        catch (JsonException)
        {
            throw new ApiException("API yanıtı beklenen JSON sözleşmesini karşılamıyor.");
        }
        catch (NotSupportedException)
        {
            throw new ApiException("API yanıtının içerik biçimi desteklenmiyor.");
        }
    }

    private async Task<ApiException> CreateErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var message = response.StatusCode switch
        {
            HttpStatusCode.BadRequest => "API isteği geçersiz olarak reddetti.",
            HttpStatusCode.Unauthorized => "Kimlik doğrulama başarısız veya oturum geçersiz. Kullanıcı bilgilerini kontrol edip MCP sunucusunu yeniden başlatın.",
            HttpStatusCode.Forbidden => "Bölge listesini görüntülemek için API kullanıcısının Admin yetkisi olmalıdır.",
            HttpStatusCode.TooManyRequests => "API istek sınırına ulaşıldı. Bir süre bekleyip yeniden deneyin.",
            >= HttpStatusCode.InternalServerError => "UluBilge API işlemi tamamlayamadı.",
            _ => "UluBilge API beklenmeyen bir HTTP yanıtı döndürdü."
        };

        var retryAfter = response.Headers.RetryAfter;
        var delay = retryAfter?.Delta ?? (retryAfter?.Date - timeProvider.GetUtcNow());
        if (response.StatusCode == HttpStatusCode.TooManyRequests && delay.HasValue)
        {
            message += $" Yeniden denemeden önce {Math.Max(0, Math.Ceiling(delay.Value.TotalSeconds)).ToString(CultureInfo.InvariantCulture)} saniye bekleyin.";
        }

        // API'nin title/detail alanları veya ham yanıtı kimlik bilgisi içerebilir; yalnızca izleme kimliğini aktar.
        try
        {
            using var problem = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            if (problem.RootElement.ValueKind == JsonValueKind.Object
                && problem.RootElement.TryGetProperty("traceId", out var trace)
                && trace.ValueKind == JsonValueKind.String
                && trace.GetString() is { Length: > 0 and <= 128 } traceId
                && traceId.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or ':' or '_'))
            {
                message += $" İzleme numarası: {traceId}";
            }
        }
        catch (JsonException)
        {
            // Problem Details olmayan hata yanıtlarında HTTP durumuna göre güvenli mesajı kullan.
        }

        return new ApiException(message, response.StatusCode);
    }
}
