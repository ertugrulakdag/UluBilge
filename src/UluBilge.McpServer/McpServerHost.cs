using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UluBilge.McpServer.Authentication;
using UluBilge.McpServer.Clients;
using UluBilge.McpServer.Configuration;
using UluBilge.McpServer.Tools;

namespace UluBilge.McpServer;

public static class McpServerHost
{
    public static IHost Create(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = args,
            ApplicationName = typeof(McpServerHost).Assembly.GetName().Name,
            ContentRootPath = AppContext.BaseDirectory
        });

        builder.Configuration.Sources.Clear();
        builder.Configuration.SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false);
        if (builder.Environment.IsDevelopment())
        {
            builder.Configuration.AddUserSecrets(typeof(McpServerHost).Assembly, optional: true, reloadOnChange: false);
        }
        builder.Configuration.AddEnvironmentVariables().AddCommandLine(args);

        // stdout yalnızca MCP protokolüne aittir. Windows EventLog sağlayıcısı da kullanılmaz.
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole(options =>
        {
            options.LogToStandardErrorThreshold = LogLevel.Trace;
        });

        builder.Services.AddOptions<UluBilgeApiOptions>()
            .Bind(builder.Configuration.GetSection(UluBilgeApiOptions.SectionName))
            .ValidateOnStart();
        builder.Services.AddSingleton<IValidateOptions<UluBilgeApiOptions>, UluBilgeApiOptionsValidator>();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddHttpClient(ApiHttpClient.ClientName, (services, client) =>
        {
            var options = services.GetRequiredService<IOptions<UluBilgeApiOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
            client.MaxResponseContentBufferSize = 1_048_576;
        })
            .RemoveAllLoggers()
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                AllowAutoRedirect = false,
                UseCookies = false
            });
        builder.Services.AddSingleton<ApiHttpClient>();
        builder.Services.AddSingleton<ApiTokenProvider>();
        builder.Services.AddSingleton<UluBilgeApiClient>();
        builder.Services.AddMcpServer().WithStdioServerTransport().WithTools<RegionTools>();

        return builder.Build();
    }
}
