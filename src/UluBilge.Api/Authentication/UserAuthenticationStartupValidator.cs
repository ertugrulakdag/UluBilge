namespace UluBilge.Api.Authentication
{
    internal sealed class UserAuthenticationStartupValidator(IServiceScopeFactory scopeFactory) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken)
        {
            // Uygulamanın değiştirdiği kayıtlar dahil, son kullanıcı doğrulama kaydını başlangıçta denetle.
            using var scope = scopeFactory.CreateScope();
            _ = scope.ServiceProvider.GetRequiredService<IUserAuthenticator>();
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
