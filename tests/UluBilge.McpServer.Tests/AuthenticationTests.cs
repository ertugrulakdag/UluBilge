using System.Net;
using System.Text.Json;
using UluBilge.McpServer.Clients;

namespace UluBilge.McpServer.Tests;

public sealed class AuthenticationTests
{
    [Fact]
    public async Task LoginUsesExistingAuthEndpointAndCachesTokenForRegionRequests()
    {
        ApiTestContext? context = null;
        using (context = new ApiTestContext((request, _) => Task.FromResult(
            request.Method == HttpMethod.Post ? context!.Token() : ApiTestContext.Regions())))
        {
            var first = await context.Api.GetRegionsAsync();
            await context.Api.GetRegionsAsync();

            Assert.Equal("Eastern", Assert.Single(first.Items).RegionDescription);
            Assert.Equal(1, first.Total);
            var requests = context.Requests.ToArray();
            Assert.Equal(3, requests.Length);
            Assert.Equal("/v1/Auth/login", requests[0].PathAndQuery);
            Assert.Null(requests[0].Authorization);
            using var login = JsonDocument.Parse(requests[0].Body!);
            Assert.Equal(ApiTestContext.UserName, login.RootElement.GetProperty("userName").GetString());
            Assert.Equal(ApiTestContext.Password, login.RootElement.GetProperty("password").GetString());
            Assert.All(requests.Skip(1), request =>
            {
                Assert.Equal("/v1/Region/GetRegions?pageNumber=1&pageSize=20", request.PathAndQuery);
                Assert.Equal("Bearer access-one", request.Authorization);
            });
        }
    }

    [Fact]
    public async Task RefreshRotatesBothTokensAndUsesLatestRefreshToken()
    {
        var refreshCount = 0;
        ApiTestContext? context = null;
        using (context = new ApiTestContext((request, _) => Task.FromResult(request.RequestUri!.AbsolutePath switch
        {
            "/v1/Auth/login" => context!.Token(),
            "/v1/Auth/refresh" => ++refreshCount == 1
                ? context!.Token("access-two", ApiTestContext.RefreshB)
                : context!.Token("access-three", ApiTestContext.RefreshC),
            _ => ApiTestContext.Regions()
        })))
        {
            await context.Api.GetRegionsAsync();
            context.Time.Advance(TimeSpan.FromMinutes(4.75));
            await context.Api.GetRegionsAsync();
            context.Time.Advance(TimeSpan.FromMinutes(4.75));
            await context.Api.GetRegionsAsync();

            var refreshes = context.Requests.Where(request => request.PathAndQuery == "/v1/Auth/refresh").ToArray();
            Assert.Equal(2, refreshes.Length);
            using var first = JsonDocument.Parse(refreshes[0].Body!);
            using var second = JsonDocument.Parse(refreshes[1].Body!);
            Assert.Equal(ApiTestContext.RefreshA, first.RootElement.GetProperty("refreshToken").GetString());
            Assert.Equal(ApiTestContext.RefreshB, second.RootElement.GetProperty("refreshToken").GetString());
            Assert.Equal(new[] { "Bearer access-one", "Bearer access-two", "Bearer access-three" },
                context.Requests.Where(request => request.Method == HttpMethod.Get).Select(request => request.Authorization));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentRequestsPerformOnlyOneLoginOrRefresh(bool refreshing)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ApiTestContext? context = null;
        using (context = new ApiTestContext(async (request, ct) =>
        {
            if (request.Method == HttpMethod.Get) return ApiTestContext.Regions();
            if ((request.RequestUri!.AbsolutePath == "/v1/Auth/refresh") == refreshing)
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(ct);
            }
            return context!.Token();
        }))
        {
            if (refreshing)
            {
                await context.Api.GetRegionsAsync();
                context.Time.Advance(TimeSpan.FromMinutes(4.75));
            }
            var calls = Enumerable.Range(0, 20).Select(_ => context.Api.GetRegionsAsync()).ToArray();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            release.SetResult();
            await Task.WhenAll(calls);

            Assert.Single(context.Requests, request => request.PathAndQuery == "/v1/Auth/login");
            Assert.Equal(refreshing ? 1 : 0, context.Requests.Count(request => request.PathAndQuery == "/v1/Auth/refresh"));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LostRefreshResponseRequiresRestartAndNeverReplaysConsumedToken(bool timeout)
    {
        ApiTestContext? context = null;
        using (context = new ApiTestContext((request, _) =>
        {
            if (request.RequestUri!.AbsolutePath == "/v1/Auth/refresh")
            {
                if (timeout) throw new TaskCanceledException("sensitive upstream information");
                throw new HttpRequestException("sensitive upstream information");
            }
            return Task.FromResult(request.Method == HttpMethod.Post ? context!.Token() : ApiTestContext.Regions());
        }))
        {
            await context.Api.GetRegionsAsync();
            context.Time.Advance(TimeSpan.FromMinutes(4.75));
            var first = await Assert.ThrowsAsync<ApiException>(() => context.Api.GetRegionsAsync());
            var second = await Assert.ThrowsAsync<ApiException>(() => context.Api.GetRegionsAsync());

            Assert.DoesNotContain("sensitive", first.Message);
            Assert.Equal(HttpStatusCode.Unauthorized, second.StatusCode);
            Assert.Single(context.Requests, request => request.PathAndQuery == "/v1/Auth/refresh");
            Assert.Single(context.Requests, request => request.PathAndQuery == "/v1/Auth/login");
        }
    }

    [Fact]
    public async Task RefreshRateLimitPreservesTokenForLaterExplicitRetry()
    {
        var refreshCount = 0;
        ApiTestContext? context = null;
        using (context = new ApiTestContext((request, _) => Task.FromResult(request.RequestUri!.AbsolutePath switch
        {
            "/v1/Auth/login" => context!.Token(),
            "/v1/Auth/refresh" => ++refreshCount == 1
                ? ApiTestContext.Json(new { }, HttpStatusCode.TooManyRequests)
                : context!.Token("access-two", ApiTestContext.RefreshB),
            _ => ApiTestContext.Regions()
        })))
        {
            await context.Api.GetRegionsAsync();
            context.Time.Advance(TimeSpan.FromMinutes(4.75));
            var error = await Assert.ThrowsAsync<ApiException>(() => context.Api.GetRegionsAsync());
            Assert.Equal(HttpStatusCode.TooManyRequests, error.StatusCode);
            context.Time.Advance(TimeSpan.FromMinutes(1));
            await context.Api.GetRegionsAsync();

            Assert.Equal(2, refreshCount);
            Assert.Single(context.Requests, request => request.PathAndQuery == "/v1/Auth/login");
        }
    }

    [Fact]
    public async Task RejectedRegionSessionNeverSilentlyLogsInAgain()
    {
        ApiTestContext? context = null;
        using (context = new ApiTestContext((request, _) => Task.FromResult(request.Method == HttpMethod.Post
            ? context!.Token() : ApiTestContext.Json(new { }, HttpStatusCode.Unauthorized))))
        {
            await Assert.ThrowsAsync<ApiException>(() => context.Api.GetRegionsAsync());
            await Assert.ThrowsAsync<ApiException>(() => context.Api.GetRegionsAsync());
            Assert.Equal(2, context.Requests.Count);
        }
    }

    [Fact]
    public async Task RejectedLoginDoesNotRepeatPasswordAttempts()
    {
        using var context = new ApiTestContext((_, _) => Task.FromResult(ApiTestContext.Json(new { }, HttpStatusCode.Unauthorized)));
        await Assert.ThrowsAsync<ApiException>(() => context.Api.GetRegionsAsync());
        await Assert.ThrowsAsync<ApiException>(() => context.Api.GetRegionsAsync());
        Assert.Single(context.Requests);
    }

    [Fact]
    public async Task ExpiredRefreshSessionDoesNotAttemptLoginOrRefresh()
    {
        ApiTestContext? context = null;
        using (context = new ApiTestContext((request, _) => Task.FromResult(request.Method == HttpMethod.Post ? context!.Token() : ApiTestContext.Regions())))
        {
            await context.Api.GetRegionsAsync();
            context.Time.Advance(TimeSpan.FromDays(8));
            await Assert.ThrowsAsync<ApiException>(() => context.Api.GetRegionsAsync());
            Assert.Equal(2, context.Requests.Count);
        }
    }
}
