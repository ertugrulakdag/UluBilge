using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using System.Security.Cryptography;
using System.Threading.RateLimiting;

namespace UluBilge.App.Authentication
{
    public static class JwtServiceCollectionExtensions
    {
        public static IServiceCollection AddApiAuthentication(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
        {
            var settings = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
            if (string.IsNullOrWhiteSpace(settings.Issuer) || string.IsNullOrWhiteSpace(settings.Audience)
                || settings.AccessTokenLifetimeMinutes is < 1 or > 15 || settings.RefreshTokenLifetimeDays is < 1 or > 30)
            {
                throw new InvalidOperationException("Jwt:Issuer, Jwt:Audience, 1-15 dakika access token ve 1-30 gun refresh token suresi gereklidir.");
            }

            // Yalnizca Development ortaminda anahtar verilmeden calisilabilir.
            if (settings.SigningKeyBase64 is null && environment.IsDevelopment())
            {
                settings.SigningKeyBase64 = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            }

            byte[] keyBytes;
            try
            {
                keyBytes = Convert.FromBase64String(settings.SigningKeyBase64 ?? string.Empty);
            }
            catch (FormatException)
            {
                throw new InvalidOperationException("Jwt:SigningKeyBase64 gecerli Base64 olmalidir.");
            }

            if (keyBytes.Length < 32)
            {
                throw new InvalidOperationException("Jwt:SigningKeyBase64 en az 32 rastgele bayt icermelidir.");
            }

            var key = new SymmetricSecurityKey(keyBytes);
            services.AddSingleton(settings);
            services.AddSingleton(new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
            services.AddScoped<AuthTokenService>();

            services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
                options.IncludeErrorDetails = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = settings.Issuer,
                    ValidateAudience = true,
                    ValidAudience = settings.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = key,
                    ValidateLifetime = true,
                    RequireExpirationTime = true,
                    RequireSignedTokens = true,
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    ClockSkew = TimeSpan.FromSeconds(15),
                    NameClaimType = "name",
                    RoleClaimType = JwtOptions.RoleClaimType
                };
            });

            services.AddAuthorization(options =>
                options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

            services.AddRateLimiter(options =>
            {
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                options.AddPolicy("auth-login", context => RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 10,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0
                    }));
                options.AddPolicy("auth-refresh", context => RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 30,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0
                    }));
            });

            return services;
        }
    }
}
