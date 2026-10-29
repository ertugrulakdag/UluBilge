using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace UluBilge.Api.Authentication
{
    public static class AuthenticationServiceCollectionExtensions
    {
        public static IServiceCollection AddApiAuthentication(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
        {
            services.TryAddSingleton<IUserAuthenticator>(provider =>
            {
                if (!environment.IsDevelopment())
                {
                throw new InvalidOperationException("Development dışındaki ortamlarda AddApiAuthentication çağrısından önce veritabanı kullanan bir IUserAuthenticator uygulaması kaydedilmelidir. admin/admin deneme kullanıcısı bu ortamda kullanılamaz.");
                }

                return ActivatorUtilities.CreateInstance<DevelopmentUserAuthenticator>(provider);
            });
            services.AddHostedService<UserAuthenticationStartupValidator>();

            services.AddOptions<JwtOptions>()
                .Bind(configuration.GetSection(JwtOptions.SectionName))
                .ValidateOnStart();
            services.AddSingleton<IValidateOptions<JwtOptions>, JwtOptionsValidator>();
            services.AddSingleton<JwtKeyMaterial>();
            services.TryAddSingleton(TimeProvider.System);
            services.AddSingleton<IAccessTokenService, JwtAccessTokenService>();

            services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
            services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
                .Configure<IOptions<JwtOptions>, JwtKeyMaterial>((bearer, jwtOptions, keyMaterial) =>
                {
                    var settings = jwtOptions.Value;
                    bearer.RequireHttpsMetadata = true;
                    bearer.MapInboundClaims = false;
                    bearer.SaveToken = false;
                    bearer.IncludeErrorDetails = false;
                    bearer.TokenHandlers.Clear();
                    bearer.TokenHandlers.Add(new JsonWebTokenHandler { MaximumTokenSizeInBytes = 16_384 });
                    bearer.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidIssuer = settings.Issuer,
                        ValidateAudience = true,
                        ValidAudience = settings.Audience,
                        RequireAudience = true,
                        ValidateIssuerSigningKey = true,
                        IssuerSigningKey = keyMaterial.Key,
                        RequireSignedTokens = true,
                        ValidateLifetime = true,
                        RequireExpirationTime = true,
                        ClockSkew = TimeSpan.FromSeconds(15),
                        ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                        ValidTypes = ["at+jwt"],
                        NameClaimType = "name",
                        RoleClaimType = "role",
                        LogValidationExceptions = false,
                        IncludeTokenOnFailedValidation = false
                    };
                    bearer.Events = new JwtBearerEvents
                    {
                        OnTokenValidated = context =>
                        {
                            var now = context.HttpContext.RequestServices.GetRequiredService<TimeProvider>()
                                .GetUtcNow().ToUnixTimeSeconds();
                            if (!HasSingleValue(context.Principal, "sub")
                                || !HasSingleValue(context.Principal, "name")
                                || !HasSingleValue(context.Principal, "jti")
                                || context.SecurityToken is not JsonWebToken token
                                || !token.TryGetPayloadValue<long>("iat", out var issuedAt)
                                || !token.TryGetPayloadValue<long>("nbf", out var notBefore)
                                || !token.TryGetPayloadValue<long>("exp", out var expiresAt)
                                || issuedAt <= 0
                                || issuedAt > now + 15
                                || notBefore > issuedAt
                                || expiresAt <= issuedAt
                                || expiresAt - issuedAt > settings.AccessTokenLifetimeMinutes * 60L)
                            {
                            context.Fail("Geçersiz erişim belirteci.");
                            }

                            return Task.CompletedTask;
                        },
                        OnChallenge = async context =>
                        {
                            context.HandleResponse();
                            context.Response.Headers.WWWAuthenticate = "Bearer";
                            context.Response.Headers.CacheControl = "no-store";
                            await Results.Problem(statusCode: StatusCodes.Status401Unauthorized,
                                title: "Kimlik doğrulama gerekli",
                            detail: "Bearer şemasıyla geçerli bir erişim belirteci gönderin.").ExecuteAsync(context.HttpContext);
                        },
                        OnForbidden = context =>
                        {
                            context.Response.Headers.CacheControl = "no-store";
                            return Results.Problem(statusCode: StatusCodes.Status403Forbidden,
                                title: "Erişim reddedildi",
                                detail: "Bu işlem için gerekli yetkiye sahip değilsiniz.").ExecuteAsync(context.HttpContext);
                        }
                    };
                })
                .ValidateOnStart();

            services.AddAuthorization(options =>
            {
                options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
                options.AddPolicy(AuthorizationPolicies.ReadRegions, policy => policy.RequireAuthenticatedUser().RequireClaim("permission", AuthorizationPolicies.ReadRegions));
                options.AddPolicy(AuthorizationPolicies.WriteRegions, policy => policy.RequireAuthenticatedUser().RequireClaim("permission", AuthorizationPolicies.WriteRegions));
            });

            services.AddRateLimiter(options =>
            {
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                options.AddPolicy("auth-login", context => RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 5,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                        AutoReplenishment = true
                    }));
                // Farklı IP adreslerinden gelen toplam giriş denemelerini de sınırla; yönlendirme başlıklarına kendiliğinden güvenme.
                options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                    context.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName == "auth-login"
                        ? RateLimitPartition.GetFixedWindowLimiter("login-global", _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = 20,
                            Window = TimeSpan.FromMinutes(1),
                            QueueLimit = 0,
                            AutoReplenishment = true
                        })
                        : RateLimitPartition.GetNoLimiter("other-endpoints"));
                options.OnRejected = async (context, cancellationToken) =>
                {
                    if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                    {
                        context.HttpContext.Response.Headers.RetryAfter = Math.Ceiling(retryAfter.TotalSeconds)
                            .ToString(CultureInfo.InvariantCulture);
                    }

                    context.HttpContext.Response.Headers.CacheControl = "no-store";
                    await Results.Problem(statusCode: StatusCodes.Status429TooManyRequests,
                        title: "Çok fazla giriş denemesi",
                        detail: "Bir süre bekleyip yeniden deneyin.").ExecuteAsync(context.HttpContext);
                };
            });

            return services;
        }

        private static bool HasSingleValue(ClaimsPrincipal? principal, string claimType)
        {
            var values = principal?.FindAll(claimType).ToArray();
            return values is { Length: 1 } && !string.IsNullOrWhiteSpace(values[0].Value)
                && values[0].Value.Length <= 128;
        }
    }
}
