using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using UluBilge.McpServer.Clients;

namespace UluBilge.McpServer.Tests;

public sealed class RegionToolTests
{
    [Theory]
    [InlineData(0, 20)]
    [InlineData(-1, 20)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    public async Task InvalidPagingReturnsToolErrorBeforeAnyNetworkRequest(int pageNumber, int pageSize)
    {
        using var context = new ApiTestContext((_, _) => throw new InvalidOperationException("Network must not be called"));
        var result = await context.Tools.GetRegionsAsync(pageNumber, pageSize);
        Assert.True(result.IsError);
        Assert.Contains("Sayfa", Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
        Assert.Empty(context.Requests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResultsContainReadableTextAndStructuredDataIncludingEmptyPages(bool empty)
    {
        ApiTestContext? context = null;
        using (context = new ApiTestContext((request, _) => Task.FromResult(request.Method == HttpMethod.Post ? context!.Token() : ApiTestContext.Regions(empty))))
        {
            var result = await context.Tools.GetRegionsAsync();
            Assert.False(result.IsError == true);
            var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
            Assert.Contains(empty ? "Bu sayfada" : "Eastern", text);
            Assert.Equal(empty ? 0 : 1, result.StructuredContent!.Value.GetProperty("items").GetArrayLength());
            Assert.Equal(20, result.StructuredContent.Value.GetProperty("pageSize").GetInt32());
            Assert.DoesNotContain(ApiTestContext.Password, text);
            Assert.DoesNotContain("access-one", text);
            Assert.DoesNotContain(ApiTestContext.RefreshA, text);
        }
    }

    [Theory]
    [InlineData("not JSON")]
    [InlineData("{}")]
    [InlineData("{\"data\":null}")]
    [InlineData("{\"data\":{\"data\":null,\"total\":0,\"pageNumber\":1,\"pageSize\":20}}")]
    [InlineData("{\"data\":{\"data\":[null],\"total\":1,\"pageNumber\":1,\"pageSize\":20}}")]
    [InlineData("{\"data\":{\"data\":[],\"total\":0,\"pageNumber\":2,\"pageSize\":20}}")]
    public async Task MalformedRegionResponseBecomesSafeToolError(string json)
    {
        ApiTestContext? context = null;
        using (context = new ApiTestContext((request, _) => Task.FromResult(request.Method == HttpMethod.Post ? context!.Token()
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) })))
        {
            var result = await context.Tools.GetRegionsAsync();
            Assert.True(result.IsError);
            Assert.Null(result.StructuredContent);
        }
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "Admin")]
    [InlineData(HttpStatusCode.TooManyRequests, "60 saniye")]
    [InlineData(HttpStatusCode.InternalServerError, "tamamlayamadı")]
    [InlineData(HttpStatusCode.Found, "HTTP")]
    public async Task ApiErrorsUseSafeMessagesAndPreserveTraceAndRetryInformation(HttpStatusCode status, string expected)
    {
        ApiTestContext? context = null;
        using (context = new ApiTestContext((request, _) =>
        {
            if (request.Method == HttpMethod.Post) return Task.FromResult(context!.Token());
            var response = ApiTestContext.Json(new { title = ApiTestContext.Password, detail = "access-one", traceId = "trace-123" }, status);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(60));
            return Task.FromResult(response);
        }))
        {
            var result = await context.Tools.GetRegionsAsync();
            Assert.True(result.IsError);
            var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
            Assert.Contains(expected, text);
            Assert.Contains("trace-123", text);
            Assert.DoesNotContain(ApiTestContext.Password, text);
            Assert.DoesNotContain("access-one", text);
            Assert.Equal(2, context.Requests.Count);
        }
    }

    [Fact]
    public async Task MissingTokenFieldsReturnErrorWithoutCallingRegionEndpoint()
    {
        using var context = new ApiTestContext((_, _) => Task.FromResult(ApiTestContext.Json(new { accessToken = "access-one" })));
        var result = await context.Tools.GetRegionsAsync();
        Assert.True(result.IsError);
        Assert.Single(context.Requests);
    }

    [Fact]
    public async Task CallerCancellationReachesHttpRequest()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ApiTestContext? context = null;
        using (context = new ApiTestContext(async (request, ct) =>
        {
            if (request.Method == HttpMethod.Post) return context!.Token();
            entered.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return ApiTestContext.Regions();
        }))
        {
            using var cancellation = new CancellationTokenSource();
            var call = context.Tools.GetRegionsAsync(cancellationToken: cancellation.Token);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);
        }
    }

    [Fact]
    public async Task CancelledCallerDoesNotStartAuthentication()
    {
        using var context = new ApiTestContext((_, _) => throw new InvalidOperationException("Network must not be called"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => context.Api.GetRegionsAsync(cancellationToken: cancellation.Token));
        Assert.Empty(context.Requests);
    }

    [Fact]
    public async Task HttpTimeoutIsReportedAsToolError()
    {
        using var context = new ApiTestContext(async (_, ct) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return ApiTestContext.Regions();
        }, TimeSpan.FromMilliseconds(100));
        var result = await context.Tools.GetRegionsAsync();
        Assert.True(result.IsError);
        Assert.Contains("zaman aşımı", Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
    }
}
