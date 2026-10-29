using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace UluBilge.Api.Authentication
{
    public sealed class DevelopmentUserAuthenticator : IUserAuthenticator
    {
        private readonly AuthenticatedUser _user;
        private readonly PasswordHasher<AuthenticatedUser> _passwordHasher;
        private readonly string _passwordHash;

        public DevelopmentUserAuthenticator(IHostEnvironment environment)
        {
            if (!environment.IsDevelopment())
            {
                throw new InvalidOperationException("Geliştirme amaçlı kullanıcı doğrulaması yalnızca Development ortamında kullanılabilir.");
            }

            _user = new AuthenticatedUser(
                "development-admin",
                "admin",
                Array.AsReadOnly(new[] { "Admin" }),
                Array.AsReadOnly(new[] { "regions:read", "regions:write" }));
            _passwordHasher = new PasswordHasher<AuthenticatedUser>(Options.Create(new PasswordHasherOptions
            {
                CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV3,
                IterationCount = 210_000
            }));
            _passwordHash = _passwordHasher.HashPassword(_user, "admin");
        }

        public Task<AuthenticatedUser?> AuthenticateAsync(string userName, string password, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var passwordResult = _passwordHasher.VerifyHashedPassword(_user, _passwordHash, password);
            var userNameMatches = string.Equals(userName, _user.UserName, StringComparison.Ordinal);

            cancellationToken.ThrowIfCancellationRequested();
            var authenticated = userNameMatches && passwordResult != PasswordVerificationResult.Failed;
            return Task.FromResult<AuthenticatedUser?>(authenticated ? _user : null);
        }
    }
}
