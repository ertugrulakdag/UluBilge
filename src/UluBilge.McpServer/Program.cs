using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using UluBilge.McpServer;

try
{
    using var host = McpServerHost.Create(args);
    await host.RunAsync();
    return 0;
}
catch (OptionsValidationException exception)
{
    await Console.Error.WriteLineAsync("MCP yapılandırması geçersiz: " + string.Join(" ", exception.Failures));
    return 1;
}
