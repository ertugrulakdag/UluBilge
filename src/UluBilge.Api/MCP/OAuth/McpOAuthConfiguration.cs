using System.Security.Cryptography.X509Certificates;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.AspNetCore.Authentication;
using OpenIddict.Abstractions;
using OpenIddict.Validation.AspNetCore;
using UluBilge.Core.Enums;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace UluBilge.Api.MCP.OAuth;

public static partial class McpOAuthExtensions
{
    public static IServiceCollection AddMcpOAuth(this IServiceCollection services, IConfiguration configuration, IWebHostEnvironment environment)
    {
        var settings = configuration.GetSection("OAuth").Get<McpOAuthOptions>() ?? new();
        if (!Uri.TryCreate(settings.ServerUrl, UriKind.Absolute, out var url) || url.Scheme != Uri.UriSchemeHttps
            || url.AbsolutePath != "/" || !string.IsNullOrEmpty(url.Query) || !string.IsNullOrEmpty(url.Fragment)
            || !string.IsNullOrEmpty(url.UserInfo) || (!environment.IsDevelopment() && url.IsLoopback))
        {
            throw new InvalidOperationException("OAuth:ServerUrl, /mcp içermeyen bir HTTPS kök adresi olmalıdır. Production adresi localhost olamaz.");
        }
        if (settings.Clients.Count == 0 || settings.Clients.Any(client => string.IsNullOrWhiteSpace(client.ClientId)
            || client.RedirectUris.Count == 0 || client.RedirectUris.Any(value => !Uri.TryCreate(value, UriKind.Absolute, out var uri)
                || uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.Fragment) || !string.IsNullOrEmpty(uri.UserInfo)))
            || settings.Clients.Select(client => client.ClientId).Distinct(StringComparer.Ordinal).Count() != settings.Clients.Count)
        {
            throw new InvalidOperationException("OAuth istemcileri için benzersiz ClientId ve tam HTTPS RedirectUris adresleri gereklidir.");
        }

        services.AddSingleton(settings);
        var databasePath = Path.GetFullPath(settings.DatabasePath, environment.ContentRootPath);
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        services.AddDbContext<McpOAuthDbContext>(options =>
        {
            options.UseSqlite(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = databasePath }.ToString());
            options.UseOpenIddict();
        });

        services.AddOpenIddict()
            .AddCore(options => options.UseEntityFrameworkCore().UseDbContext<McpOAuthDbContext>())
            .AddServer(options =>
            {
                options.SetIssuer(settings.Issuer);
                options.SetConfigurationEndpointUris("/.well-known/oauth-authorization-server", "/.well-known/openid-configuration");
                options.SetAuthorizationEndpointUris("/oauth/authorize");
                options.SetTokenEndpointUris("/oauth/token");
                options.AllowAuthorizationCodeFlow().AllowRefreshTokenFlow();
                options.RequireProofKeyForCodeExchange();
                options.Configure(server =>
                {
                    server.CodeChallengeMethods.Clear();
                    server.CodeChallengeMethods.Add(CodeChallengeMethods.Sha256);
                    server.ResponseModes.Clear();
                    server.ResponseModes.Add(ResponseModes.Query);
                    server.Scopes.Clear();
                });
                options.RegisterScopes(McpOAuthOptions.Scope, Scopes.OfflineAccess);
                options.RegisterResources(settings.Resource);
                options.SetAuthorizationCodeLifetime(TimeSpan.FromMinutes(2));
                options.SetAccessTokenLifetime(TimeSpan.FromMinutes(configuration.GetValue("Jwt:AccessTokenLifetimeMinutes", 5)));
                options.SetRefreshTokenLifetime(TimeSpan.FromDays(7));
                options.SetRefreshTokenReuseLeeway(TimeSpan.Zero);
                options.DisableSlidingRefreshTokenExpiration();
                options.DisableAccessTokenEncryption();

                if (environment.IsDevelopment())
                {
                    // Gelistirme anahtarlari surece ozeldir; yeniden baslatmada baglanti yenilenir.
                    options.AddEphemeralSigningKey().AddEphemeralEncryptionKey();
                }
                else
                {
                    options.AddSigningCertificate(LoadCertificate(settings.SigningCertificatePath, settings.SigningCertificatePassword, environment));
                    options.AddEncryptionCertificate(LoadCertificate(settings.EncryptionCertificatePath, settings.EncryptionCertificatePassword, environment));
                }

                options.UseAspNetCore().EnableAuthorizationEndpointPassthrough().EnableTokenEndpointPassthrough();
            })
            .AddValidation(options =>
            {
                options.UseLocalServer();
                options.AddAudiences(settings.Resource.AbsoluteUri);
                options.EnableTokenEntryValidation();
                options.UseAspNetCore();
            });

        services.AddAuthentication().AddMcp(options =>
        {
            options.ForwardAuthenticate = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
            options.ResourceMetadataUri = new Uri(settings.Issuer, ".well-known/oauth-protected-resource/mcp");
            options.ResourceMetadata = new()
            {
                Resource = settings.Resource.AbsoluteUri,
                AuthorizationServers = { settings.Issuer.AbsoluteUri },
                ScopesSupported = [McpOAuthOptions.Scope, Scopes.OfflineAccess]
            };
        });
        services.AddAuthorization(options => options.AddPolicy(McpOAuthOptions.Policy, policy => policy
            .AddAuthenticationSchemes(McpAuthenticationDefaults.AuthenticationScheme)
            .RequireAuthenticatedUser()
            .RequireRole(nameof(RoleEnum.Report), nameof(RoleEnum.Admin))
            .RequireAssertion(context => context.User.HasScope(McpOAuthOptions.Scope))));

        services.AddAntiforgery(options =>
        {
            options.Cookie.Name = "__Host-UluBilge.OAuth.Csrf";
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Strict;
        });
        services.Configure<FormOptions>(options =>
        {
            options.ValueCountLimit = 32;
            options.ValueLengthLimit = 4096;
            options.KeyLengthLimit = 128;
        });
        services.AddRateLimiter(options => options.AddPolicy("oauth-token", context => RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 })));
        return services;
    }

    public static async Task MapMcpOAuthAsync(this WebApplication app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<McpOAuthDbContext>().Database.EnsureCreatedAsync();
        var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        var settings = app.Services.GetRequiredService<McpOAuthOptions>();
        foreach (var client in settings.Clients)
        {
            var descriptor = new OpenIddictApplicationDescriptor
            {
                ClientId = client.ClientId,
                DisplayName = client.DisplayName,
                ClientType = string.IsNullOrEmpty(client.ClientSecret) ? ClientTypes.Public : ClientTypes.Confidential,
                ClientSecret = client.ClientSecret,
                ConsentType = ConsentTypes.Explicit,
                Permissions = { Permissions.Endpoints.Authorization, Permissions.Endpoints.Token,
                    Permissions.GrantTypes.AuthorizationCode, Permissions.GrantTypes.RefreshToken,
                    Permissions.ResponseTypes.Code, Permissions.Prefixes.Scope + McpOAuthOptions.Scope },
                Requirements = { Requirements.Features.ProofKeyForCodeExchange }
            };
            descriptor.AddResourcePermissions(settings.Resource.AbsoluteUri);
            foreach (var uri in client.RedirectUris) descriptor.RedirectUris.Add(new Uri(uri));
            var application = await manager.FindByClientIdAsync(client.ClientId);
            if (application is null) await manager.CreateAsync(descriptor);
            else await manager.UpdateAsync(application, descriptor);
        }

        var threshold = DateTimeOffset.UtcNow.AddDays(-7);
        await scope.ServiceProvider.GetRequiredService<IOpenIddictTokenManager>().PruneAsync(threshold);
        await scope.ServiceProvider.GetRequiredService<IOpenIddictAuthorizationManager>().PruneAsync(threshold);

        app.MapGet("/oauth/authorize", AuthorizeAsync).AllowAnonymous().ExcludeFromDescription();
        app.MapPost("/oauth/authorize", AuthorizeAsync).AllowAnonymous().RequireRateLimiting("auth-login").ExcludeFromDescription();
        app.MapPost("/oauth/token", ExchangeAsync).AllowAnonymous().RequireRateLimiting("oauth-token").ExcludeFromDescription();
    }

    private static X509Certificate2 LoadCertificate(string? path, string? password, IWebHostEnvironment environment)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException("Production için OAuth:SigningCertificatePath ve OAuth:EncryptionCertificatePath gereklidir.");
        return X509CertificateLoader.LoadPkcs12FromFile(Path.GetFullPath(path, environment.ContentRootPath), password, X509KeyStorageFlags.EphemeralKeySet);
    }
}
