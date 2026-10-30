using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using UluBilge.McpServer.Clients;
using UluBilge.McpServer.Configuration;
using UluBilge.McpServer.Contracts;

namespace UluBilge.McpServer.Authentication;

public sealed class ApiTokenProvider(
    ApiHttpClient http,
    IOptions<UluBilgeApiOptions> options,
    TimeProvider timeProvider) : IDisposable
{
    private static readonly TimeSpan RefreshWindow = TimeSpan.FromSeconds(30);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _stateLock = new();
    private TokenResponse? _tokens;
    private bool _reauthenticationRequired;

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            TokenResponse? current;
            lock (_stateLock)
            {
                ThrowIfInvalidated();
                current = _tokens;
            }

            var now = timeProvider.GetUtcNow();
            if (current is not null && current.RefreshTokenExpiresAtUtc <= now)
            {
                Invalidate();
                throw ReauthenticationError();
            }
            if (current is not null && current.ExpiresAtUtc > now.Add(RefreshWindow))
            {
                return current.AccessToken;
            }

            TokenResponse next;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, current is null ? "v1/Auth/login" : "v1/Auth/refresh");
                request.Content = current is null
                    ? JsonContent.Create(new LoginRequest(options.Value.UserName, options.Value.Password), options: ApiHttpClient.JsonOptions)
                    : JsonContent.Create(new RefreshRequest(current.RefreshToken), options: ApiHttpClient.JsonOptions);
                next = await http.SendAsync<TokenResponse>(request, cancellationToken);
            }
            catch (ApiException exception) when (exception.StatusCode == HttpStatusCode.TooManyRequests)
            {
                // Kesin olarak reddedilmiş refresh tüketilmemiştir; sonraki kullanıcı çağrısı bekledikten sonra deneyebilir.
                throw;
            }
            catch (Exception exception) when (exception is ApiException or OperationCanceledException)
            {
                if (current is not null || exception is ApiException { StatusCode: HttpStatusCode.Unauthorized })
                {
                    // Yenileme yanıtı kaybolduysa eski token tekrar kullanılamaz.
                    Invalidate();
                }
                throw;
            }

            if (!IsValid(next, timeProvider.GetUtcNow()))
            {
                Invalidate();
                throw new ApiException("Kimlik doğrulama yanıtı geçerli token ve süre bilgileri içermiyor. MCP sunucusunu yeniden başlatın.");
            }

            lock (_stateLock)
            {
                ThrowIfInvalidated();
                _tokens = next;
                return next.AccessToken;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Invalidate()
    {
        lock (_stateLock)
        {
            _tokens = null;
            _reauthenticationRequired = true;
        }
    }

    private static bool IsValid(TokenResponse tokens, DateTimeOffset now) =>
        !string.IsNullOrWhiteSpace(tokens.AccessToken)
        && tokens.AccessToken.Length <= 16_384
        && AuthenticationHeaderValue.TryParse("Bearer " + tokens.AccessToken, out var header)
        && header.Parameter == tokens.AccessToken
        && tokens.TokenType.Equals("Bearer", StringComparison.OrdinalIgnoreCase)
        && tokens.ExpiresIn > 0
        && tokens.ExpiresAtUtc > now
        && tokens.RefreshTokenExpiresAtUtc > now
        && tokens.RefreshToken is { Length: 43 }
        && tokens.RefreshToken.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');

    private void ThrowIfInvalidated()
    {
        if (_reauthenticationRequired)
        {
            throw ReauthenticationError();
        }
    }

    private static ApiException ReauthenticationError() =>
        new("Oturum yeniden kimlik doğrulama gerektiriyor. Kullanıcı bilgilerini kontrol edip MCP sunucusunu yeniden başlatın.", HttpStatusCode.Unauthorized);

    public void Dispose() => _gate.Dispose();
}
