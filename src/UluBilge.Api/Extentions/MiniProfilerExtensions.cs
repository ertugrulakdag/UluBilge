using Microsoft.Extensions.Caching.Memory;
using StackExchange.Profiling;
using StackExchange.Profiling.Storage;
using UluBilge.Api.Profiling;

namespace UluBilge.Api.Extentions
{
    public static class MiniProfilerExtensions
    {
        public const string Route = "/mini-profile";

        public static IServiceCollection AddRequestProfiling(this IServiceCollection services)
        {
            services.AddMiniProfiler(options =>
            {
                options.RouteBasePath = Route;
                options.ColorScheme = ColorScheme.Dark;
                options.ShouldProfile = _ => false; // İstekleri middleware ölçer.
                options.ResultsAuthorize = _ => true;
                options.ResultsListAuthorize = _ => true;
            });
            services.AddOptions<MiniProfilerOptions>().Configure<IMemoryCache>((options, cache) => options.Storage = new MemoryCacheStorage(cache, TimeSpan.FromMinutes(60 * 24)));
            return services;
        }

        public static IApplicationBuilder UseRequestProfiling(this IApplicationBuilder app) => app.UseMiddleware<RequestProfilingMiddleware>();
    }
}
