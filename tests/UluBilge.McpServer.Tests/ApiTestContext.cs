using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using UluBilge.McpServer.Authentication;
using UluBilge.McpServer.Clients;
using UluBilge.McpServer.Configuration;
using UluBilge.McpServer.Tools;

namespace UluBilge.McpServer.Tests;

internal sealed class ApiTestContext : IDisposable
{
    public const string UserName = "mcp-test-user";
    public const string Password = "test-password-never-log";
    public static string RefreshA { get; } = new('A', 43);
    public static string RefreshB { get; } = new('B', 43);
    public static string RefreshC { get; } = new('C', 43);
    private readonly DelegateHandler _handler;

    public TestTimeProvider Time { get; } = new();
    public ConcurrentQueue<RecordedRequest> Requests { get; } = new();
    public ApiTokenProvider Tokens { get; }
    public UluBilgeApiClient Api { get; }
    public RegionTools Tools { get; }

    public ApiTestContext(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond, TimeSpan? timeout = null)
    {
        _handler = new DelegateHandler(async (request, ct) =>
        {
            Requests.Enqueue(new RecordedRequest(request.Method, request.RequestUri!.PathAndQuery,
                request.Headers.Authorization?.ToString(), request.Content is null ? null : await request.Content.ReadAsStringAsync(ct)));
            return await respond(request, ct);
        });
        var http = new ApiHttpClient(new TestClientFactory(_handler, timeout), Time);
        Tokens = new ApiTokenProvider(http, Options.Create(new UluBilgeApiOptions { UserName = UserName, Password = Password }), Time);
        Api = new UluBilgeApiClient(http, Tokens);
        Tools = new RegionTools(Api);
    }

    public HttpResponseMessage Token(string accessToken = "access-one", string? refreshToken = null) => Json(new
    {
        accessToken,
        tokenType = "Bearer",
        expiresIn = 300,
        expiresAtUtc = Time.GetUtcNow().AddMinutes(5),
        refreshToken = refreshToken ?? RefreshA,
        refreshTokenExpiresAtUtc = Time.GetUtcNow().AddDays(7)
    });

    public static HttpResponseMessage Regions(bool empty = false) => Json(new
    {
        data = new
        {
            data = empty ? [] : new[] { new { regionId = 1, regionDescription = "Eastern" } },
            total = empty ? 0 : 1,
            pageNumber = 1,
            pageSize = 20
        }
    });

    public static HttpResponseMessage Json<T>(T value, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = JsonContent.Create(value) };

    public void Dispose()
    {
        Tokens.Dispose();
        _handler.Dispose();
    }

    private sealed class TestClientFactory(HttpMessageHandler handler, TimeSpan? timeout) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false)
        {
            BaseAddress = new Uri("https://api.example.test/"),
            Timeout = timeout ?? TimeSpan.FromSeconds(5),
            MaxResponseContentBufferSize = 1_048_576
        };
    }

    private sealed class DelegateHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request, cancellationToken);
    }
}

internal sealed record RecordedRequest(HttpMethod Method, string PathAndQuery, string? Authorization, string? Body);

internal sealed class TestTimeProvider : TimeProvider
{
    private long _utcTicks = new DateTimeOffset(2026, 10, 30, 9, 0, 0, TimeSpan.Zero).Ticks;
    public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref _utcTicks), TimeSpan.Zero);
    public void Advance(TimeSpan interval) => Interlocked.Add(ref _utcTicks, interval.Ticks);
}
