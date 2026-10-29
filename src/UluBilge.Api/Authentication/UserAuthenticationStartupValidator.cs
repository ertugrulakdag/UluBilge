namespace UluBilge.Api.Authentication;

internal sealed class UserAuthenticationStartupValidator(IServiceScopeFactory scopeFactory) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Validate the final registration at startup, including replacements registered by the app.
        using var scope = scopeFactory.CreateScope();
        _ = scope.ServiceProvider.GetRequiredService<IUserAuthenticator>();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
