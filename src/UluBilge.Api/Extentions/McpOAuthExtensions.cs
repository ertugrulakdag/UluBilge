using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using ModelContextProtocol.AspNetCore.Authentication;
using UluBilge.App.Authentication;
using UluBilge.App.Commands.AuthHandler;
using UluBilge.Core.Commands;
using UluBilge.Core.Entities;
using UluBilge.Core.Enums;
using UluBilge.Data.Abstract;

namespace UluBilge.Api.Extentions
{
    public static class McpOAuthExtensions
    {
        public static IServiceCollection AddMcpOAuth(this IServiceCollection services, IConfiguration configuration, IWebHostEnvironment environment)
        {
            var serverUrl = configuration["OAuth:ServerUrl"]?.TrimEnd('/') ?? "";
            if (!Uri.TryCreate(serverUrl, UriKind.Absolute, out var issuer) || issuer.Scheme != "https"
                || issuer.AbsolutePath != "/" || issuer.Query != "" || issuer.Fragment != "" || issuer.UserInfo != ""
                || (!environment.IsDevelopment() && issuer.IsLoopback))
                throw new InvalidOperationException("OAuth:ServerUrl, /mcp içermeyen bir HTTPS kök adresi olmalıdır. Production adresi localhost olamaz.");

            var resource = serverUrl + "/mcp";
            var clients = configuration.GetSection("OAuth:Clients").Get<OAuthClient[]>() ?? [];
            if (environment.IsDevelopment())
                clients = [.. clients, .. configuration.GetSection("OAuth:DevelopmentClients").Get<OAuthClient[]>() ?? []];
            if (clients.Any(client => string.IsNullOrWhiteSpace(client.ClientId) || client.RedirectUris.Length == 0
                || client.RedirectUris.Any(uri => !IsAllowedRedirectUri(uri, environment.IsDevelopment())))
                || clients.Select(client => client.ClientId).Distinct(StringComparer.Ordinal).Count() != clients.Length)
                throw new InvalidOperationException("OAuth istemcileri için benzersiz ClientId ve tam HTTPS RedirectUris adresleri gereklidir. Development ortamında HTTP loopback IP adresleri de kullanılabilir.");

            var registeredClients = new ConcurrentDictionary<string, OAuthClient>(clients.ToDictionary(client => client.ClientId, StringComparer.Ordinal));
            services.TryAddSingleton(TimeProvider.System);
            services.AddAuthentication().AddJwtBearer("McpBearer", _ => { }).AddMcp(options =>
            {
                options.ForwardAuthenticate = "McpBearer";
                options.ResourceMetadataUri = new Uri(serverUrl + "/.well-known/oauth-protected-resource/mcp");
                options.ResourceMetadata = new() { Resource = resource, AuthorizationServers = { issuer.AbsoluteUri }, ScopesSupported = ["mcp:tools", "offline_access"] };
            });
            services.AddOptions<JwtBearerOptions>("McpBearer").Configure<SigningCredentials, TimeProvider>((options, signing, clock) =>
            {
                options.MapInboundClaims = false;
                options.IncludeErrorDetails = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = issuer.AbsoluteUri, ValidAudience = resource, IssuerSigningKey = signing.Key,
                    ValidateIssuer = true, ValidateAudience = true, ValidateIssuerSigningKey = true, ValidateLifetime = true,
                    RequireExpirationTime = true, RequireSignedTokens = true, ValidAlgorithms = [signing.Algorithm],
                    NameClaimType = "name", RoleClaimType = JwtOptions.RoleClaimType, ClockSkew = TimeSpan.FromSeconds(15),
                    LifetimeValidator = (start, end, _, parameters) => end.HasValue && (!start.HasValue || start <= end)
                        && (!start.HasValue || start <= clock.GetUtcNow().UtcDateTime + parameters.ClockSkew)
                        && end > clock.GetUtcNow().UtcDateTime - parameters.ClockSkew
                };
            });
            services.AddAuthorization(options => options.AddPolicy("McpOAuth", policy => policy
                .AddAuthenticationSchemes(McpAuthenticationDefaults.AuthenticationScheme).RequireAuthenticatedUser()
                .RequireRole(nameof(RoleEnum.Report), nameof(RoleEnum.Admin))
                .RequireAssertion(context => context.User.FindAll("scope").Any(claim => claim.Value.Split(' ').Contains("mcp:tools")))));
            services.AddAntiforgery(options =>
            {
                options.Cookie.Name = "__Host-UluBilge.OAuth.Csrf";
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                options.Cookie.SameSite = SameSiteMode.Strict;
            });
            services.Configure<FormOptions>(options => { options.ValueCountLimit = 32; options.ValueLengthLimit = 4096; options.KeyLengthLimit = 128; });
            services.AddRateLimiter(options => options.AddPolicy("oauth-token", context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 })));
            services.AddSingleton(new OAuthState(serverUrl, issuer, registeredClients));
            return services;
        }

        public static WebApplication MapMcpOAuth(this WebApplication app)
        {
            var state = app.Services.GetRequiredService<OAuthState>();
            var serverUrl = state.ServerUrl;
            var issuer = state.Issuer;
            var resource = serverUrl + "/mcp";
            var registeredClients = state.Clients;
            var authCodes = state.AuthCodes;
            var refreshTokens = state.RefreshTokens;
            var clock = app.Services.GetRequiredService<TimeProvider>();
            var signing = app.Services.GetRequiredService<SigningCredentials>();
            var jwtSettings = app.Services.GetRequiredService<JwtOptions>();

            app.MapGet("/.well-known/oauth-authorization-server", () => Results.Json(new
            {
                issuer = issuer.AbsoluteUri,
                authorization_endpoint = serverUrl + "/oauth/authorize",
                token_endpoint = serverUrl + "/oauth/token",
                registration_endpoint = serverUrl + "/oauth/register",
                response_types_supported = new[] { "code" },
                response_modes_supported = new[] { "query" },
                grant_types_supported = new[] { "authorization_code", "refresh_token" },
                code_challenge_methods_supported = new[] { "S256" },
                scopes_supported = new[] { "mcp:tools", "offline_access" },
                token_endpoint_auth_methods_supported = new[] { "none" },
                authorization_response_iss_parameter_supported = true
            })).AllowAnonymous().ExcludeFromDescription();

            app.MapPost("/oauth/register", (ClientRegistration request) =>
            {
                if (request.RedirectUris is not { Length: > 0 and <= 10 } || request.RedirectUris.Any(uri => !IsAllowedRedirectUri(uri, app.Environment.IsDevelopment())))
                    return OAuthError("invalid_redirect_uri", "Tam HTTPS dönüş adresleri gereklidir.");
                if (request.ClientName?.Length > 128 || request.TokenEndpointAuthMethod is not (null or "none")
                    || request.ResponseTypes is not null && !request.ResponseTypes.SequenceEqual(["code"])
                    || request.GrantTypes is not null && (request.GrantTypes.Any(grant => grant is not ("authorization_code" or "refresh_token"))
                        || !request.GrantTypes.Contains("authorization_code")))
                    return OAuthError("invalid_client_metadata", "Yalnızca public client, authorization_code ve refresh_token desteklenir.");
                if (registeredClients.Count >= 1000)
                    return OAuthError("temporarily_unavailable", "İstemci kayıt sınırına ulaşıldı.", 503);

                var client = new OAuthClient(NewToken(), request.ClientName ?? "MCP istemcisi", request.RedirectUris);
                registeredClients[client.ClientId] = client;
                return Results.Json(new
                {
                    client_id = client.ClientId, client_name = client.DisplayName, redirect_uris = client.RedirectUris,
                    grant_types = new[] { "authorization_code", "refresh_token" }, response_types = new[] { "code" }, token_endpoint_auth_method = "none"
                }, statusCode: StatusCodes.Status201Created);
            }).AllowAnonymous().RequireRateLimiting("oauth-token").ExcludeFromDescription();

            app.MapGet("/oauth/authorize", AuthorizeAsync).AllowAnonymous().ExcludeFromDescription();
            app.MapPost("/oauth/login", AuthorizeAsync).AllowAnonymous().RequireRateLimiting("auth-login").ExcludeFromDescription();

            app.MapPost("/oauth/token", async (HttpContext context, IUserRepository users, IUnitOfWork work, CancellationToken ct) =>
            {
                NoStore(context);
                if (!context.Request.HasFormContentType) return OAuthError("invalid_request", "Form gövdesi gereklidir.");
                var form = await context.Request.ReadFormAsync(ct);
                if (form.Any(field => field.Value.Count != 1)) return OAuthError("invalid_request", "Tekrarlanan parametreler desteklenmiyor.");
                var clientId = form["client_id"].ToString();
                if (!registeredClients.ContainsKey(clientId)) return OAuthError("invalid_client", "İstemci kayıtlı değil.");
                if (form["resource"] != resource) return OAuthError("invalid_target", "İstenen kaynak bu MCP sunucusuna ait değil.");
                RemoveExpiredTokens();

                if (form["grant_type"] == "authorization_code")
                {
                    var codeHash = HashToken(form["code"].ToString());
                    var verifier = form["code_verifier"].ToString();
                    if (!authCodes.TryGetValue(codeHash, out var code) || code.ExpiresAt <= clock.GetUtcNow()
                        || code.ClientId != clientId || code.RedirectUri != form["redirect_uri"]
                        || !Regex.IsMatch(verifier, "\\A[A-Za-z0-9._~-]{43,128}\\z")
                        || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(code.CodeChallenge), Encoding.ASCII.GetBytes(HashToken(verifier)))
                        || !authCodes.TryRemove(codeHash, out _))
                        return OAuthError("invalid_grant", "Kod, istemci, dönüş adresi veya PKCE doğrulaması geçersiz.");

                    var user = await work.ExecuteQueryAsync(token => users.GetByUserIdAsync(code.UserId, token), ct);
                    if (!CanUseMcp(user) || user!.SecurityStamp != code.SecurityStamp)
                        return OAuthError("invalid_grant", "Kullanıcı erişimi artık geçerli değil.");
                    return GenerateTokens(user, clientId, code.Scope);
                }
                if (form["grant_type"] == "refresh_token")
                {
                    var hash = HashToken(form["refresh_token"].ToString());
                    if (!refreshTokens.TryGetValue(hash, out var session) || session.ClientId != clientId || session.ExpiresAt <= clock.GetUtcNow())
                        return OAuthError("invalid_grant", "Refresh token geçersiz veya süresi dolmuş.");
                    if (form.TryGetValue("scope", out var scope) && scope.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries).Except(session.Scope.Split(' ')).Any())
                        return OAuthError("invalid_scope", "İzin verilen kapsam genişletilemez.");
                    var user = await work.ExecuteQueryAsync(token => users.GetByUserIdAsync(session.UserId, token), ct);
                    lock (session)
                    {
                        // Eski tokenin tekrar kullanılması aynı oturumun bütün refresh tokenlarını iptal eder.
                        if (session.Revoked || session.CurrentTokenHash != hash || session.ExpiresAt <= clock.GetUtcNow()
                            || !CanUseMcp(user) || user!.SecurityStamp != session.SecurityStamp)
                        {
                            session.Revoked = true;
                            return OAuthError("invalid_grant", "Oturum geçersiz. Yeniden giriş yapın.");
                        }
                        return GenerateTokens(user!, clientId, scope.Count == 1 ? scope.ToString() : session.Scope, session);
                    }
                }
                return OAuthError("unsupported_grant_type", "Bu token akışı desteklenmiyor.");
            }).AllowAnonymous().RequireRateLimiting("oauth-token").ExcludeFromDescription();

            app.MapMcp("/mcp").RequireAuthorization("McpOAuth");
            // MCP icin token, kapsam ve Report/Admin rolu her derlemede zorunludur.

            return app;

            async Task<IResult> AuthorizeAsync(HttpContext context, IAntiforgery antiforgery,
                IAsyncCommandHandler<LoginAsync.Command, LoginAsync.Result?> loginHandler,
                IOptionsMonitor<JwtBearerOptions> jwtOptions, IUserRepository users, IUnitOfWork work, CancellationToken ct)
            {
                NoStore(context);
                if (!HttpMethods.IsGet(context.Request.Method) && !context.Request.HasFormContentType)
                    return OAuthError("invalid_request", "Form gövdesi gereklidir.");
                var values = HttpMethods.IsGet(context.Request.Method) ? context.Request.Query.AsEnumerable() : (await context.Request.ReadFormAsync(ct)).AsEnumerable();
                if (values.Any(field => field.Value.Count != 1 || field.Value.ToString().Length > 4096))
                    return OAuthError("invalid_request", "Geçersiz veya tekrarlanan parametre.");
                var fields = values.ToDictionary(field => field.Key, field => field.Value.ToString(), StringComparer.Ordinal);
                var clientId = fields.GetValueOrDefault("client_id", "");
                var redirectUri = fields.GetValueOrDefault("redirect_uri", "");
                if (!registeredClients.TryGetValue(clientId, out var client) || !client.RedirectUris.Contains(redirectUri, StringComparer.Ordinal))
                    return OAuthError("invalid_request", "İstemci veya dönüş adresi kayıtlı değil.");
                if (fields.GetValueOrDefault("response_type") != "code") return OAuthError("unsupported_response_type", "Yalnızca code desteklenir.");
                if (fields.GetValueOrDefault("resource") != resource) return OAuthError("invalid_target", "İstenen kaynak bu MCP sunucusuna ait değil.");
                var scopes = fields.GetValueOrDefault("scope", "mcp:tools").Split(' ', StringSplitOptions.RemoveEmptyEntries).Distinct().ToArray();
                if (!scopes.Contains("mcp:tools") || scopes.Any(scope => scope is not ("mcp:tools" or "offline_access")))
                    return OAuthError("invalid_scope", "mcp:tools kapsamı gereklidir; offline_access isteğe bağlıdır.");
                var challenge = fields.GetValueOrDefault("code_challenge", "");
                if (fields.GetValueOrDefault("code_challenge_method") != "S256" || !Regex.IsMatch(challenge, "\\A[A-Za-z0-9_-]{43}\\z"))
                    return OAuthError("invalid_request", "PKCE S256 gereklidir.");
                if (fields.TryGetValue("response_mode", out var mode) && mode != "query")
                    return OAuthError("invalid_request", "Yalnızca query yanıt modu desteklenir.");
                if (HttpMethods.IsGet(context.Request.Method)) return LoginForm();
                try { await antiforgery.ValidateRequestAsync(context); }
                catch (AntiforgeryValidationException) { return LoginForm("Formun süresi doldu. Tekrar deneyin.", 400); }
                if (fields.GetValueOrDefault("decision") == "deny") return Redirect("error", "access_denied");
                if (fields.GetValueOrDefault("decision") != "allow") return OAuthError("invalid_request", "Bağlantı onayı gereklidir.");

                var command = new LoginAsync.Command { UserName = fields.GetValueOrDefault("userName", ""), Password = fields.GetValueOrDefault("password", "") };
                if (!Validator.TryValidateObject(command, new ValidationContext(command), [], true))
                    return LoginForm("Kullanıcı adı ve parola geçerli biçimde girilmelidir.", 400);
                var login = await loginHandler.HandleAsync(command, ct);
                if (login is null) return LoginForm("Kullanıcı adı veya parola geçersiz.", 401);
                var validation = await new JsonWebTokenHandler().ValidateTokenAsync(login.AccessToken, jwtOptions.Get(JwtBearerDefaults.AuthenticationScheme).TokenValidationParameters);
                if (!validation.IsValid || !int.TryParse(validation.ClaimsIdentity.FindFirst("sub")?.Value, out var userId))
                    throw new InvalidOperationException("Giriş tokenı doğrulanamadı.");
                var user = await work.ExecuteQueryAsync(token => users.GetByUserIdAsync(userId, token), ct);
                if (!CanUseMcp(user)) return Redirect("error", "access_denied");
                RemoveExpiredTokens();
                var code = NewToken();
                authCodes[HashToken(code)] = new(userId, user!.SecurityStamp, clientId, redirectUri, challenge, string.Join(' ', scopes), clock.GetUtcNow().AddMinutes(2));
                return Redirect("code", code);

                IResult Redirect(string key, string value) => Results.Redirect(QueryHelpers.AddQueryString(redirectUri,
                    new Dictionary<string, string?> { [key] = value, ["state"] = fields.GetValueOrDefault("state"), ["iss"] = issuer.AbsoluteUri }));

                IResult LoginForm(string? error = null, int status = 200)
                {
                    context.Response.Headers.ContentSecurityPolicy = $"default-src 'none'; style-src 'unsafe-inline'; form-action 'self' {new Uri(redirectUri).GetLeftPart(UriPartial.Authority)}; frame-ancestors 'none'; base-uri 'none'";
                    context.Response.Headers["Referrer-Policy"] = "no-referrer";
                    context.Response.Headers.XContentTypeOptions = "nosniff";
                    var csrf = antiforgery.GetAndStoreTokens(context);
                    var hiddenFields = fields.Where(field => field.Key is "client_id" or "redirect_uri" or "response_type" or "resource" or "scope" or "state" or "code_challenge" or "code_challenge_method").ToDictionary();
                    hiddenFields[csrf.FormFieldName] = csrf.RequestToken!;
                    var hidden = string.Join('\n', hiddenFields.Select(field => $"<input type=\"hidden\" name=\"{WebUtility.HtmlEncode(field.Key)}\" value=\"{WebUtility.HtmlEncode(field.Value)}\" />"));
                    return Results.Content($$"""
                        <!doctype html><html lang="tr"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>UluBilge bağlantısı</title>
                        <style>body{font:16px system-ui;max-width:430px;margin:10vh auto;padding:24px}input{display:block;width:95%;padding:10px;margin:6px 0 18px}button{padding:10px 16px;margin:8px}.error{color:#b42318}</style></head>
                        <body><h1>UluBilge'e bağlan</h1><p><strong>{{WebUtility.HtmlEncode(client.DisplayName)}}</strong>, ürün satış raporlarını okumak için izin istiyor.</p>
                        <p>{{(scopes.Contains("offline_access") ? "Erişim en fazla 7 gün boyunca otomatik yenilenebilir." : "Erişim kısa süreliğine verilecektir.")}}</p>
                        <p class="error" role="alert">{{WebUtility.HtmlEncode(error)}}</p><form method="post" action="/oauth/login">{{hidden}}
                        <label>Kullanıcı adı<input name="userName" maxlength="64" autocomplete="username" required></label>
                        <label>Parola<input type="password" name="password" maxlength="128" autocomplete="current-password" required></label>
                        <button type="submit" name="decision" value="allow">Giriş yap ve izin ver</button><button type="submit" name="decision" value="deny" formnovalidate>İptal</button>
                        </form></body></html>
                        """, "text/html; charset=utf-8", Encoding.UTF8, status);
                }
            }

            IResult GenerateTokens(User user, string clientId, string scope, RefreshTokenData? session = null)
            {
                var now = clock.GetUtcNow();
                var accessToken = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
                {
                    Issuer = issuer.AbsoluteUri, Audience = resource, IssuedAt = now.UtcDateTime, NotBefore = now.UtcDateTime,
                    Expires = now.AddMinutes(jwtSettings.AccessTokenLifetimeMinutes).UtcDateTime, SigningCredentials = signing,
                    Claims = new Dictionary<string, object>
                    {
                        ["sub"] = user.Id.ToString(CultureInfo.InvariantCulture), ["name"] = user.UserName, ["scope"] = scope,
                        [JwtOptions.RoleClaimType] = Enum.GetValues<RoleEnum>().Where(role => role != RoleEnum.None && user.RoleEnum.HasFlag(role)).Select(role => role.ToString()).ToArray()
                    }
                });
                var result = new Dictionary<string, object> { ["access_token"] = accessToken, ["token_type"] = "Bearer", ["expires_in"] = jwtSettings.AccessTokenLifetimeMinutes * 60, ["scope"] = scope };
                if (scope.Split(' ').Contains("offline_access"))
                {
                    session ??= new(user.Id, user.SecurityStamp, clientId, scope, now.AddDays(7));
                    var refreshToken = NewToken();
                    session.CurrentTokenHash = HashToken(refreshToken);
                    session.Scope = scope;
                    refreshTokens[session.CurrentTokenHash] = session;
                    result["refresh_token"] = refreshToken;
                }
                else if (session is not null) session.Revoked = true;
                return Results.Json(result);
            }

            bool CanUseMcp(User? user) => user is { IsActive: true } && !(user.LockoutEndUtc > clock.GetUtcNow().UtcDateTime)
                && (user.RoleEnum & ~Enum.GetValues<RoleEnum>().Aggregate(RoleEnum.None, (mask, role) => mask | role)) == 0
                && (user.RoleEnum.HasFlag(RoleEnum.Report) || user.RoleEnum.HasFlag(RoleEnum.Admin));

            void RemoveExpiredTokens()
            {
                var now = clock.GetUtcNow();
                foreach (var entry in authCodes.Where(entry => entry.Value.ExpiresAt <= now)) authCodes.TryRemove(entry.Key, out _);
                foreach (var entry in refreshTokens.Where(entry => entry.Value.ExpiresAt <= now)) refreshTokens.TryRemove(entry.Key, out _);
            }

            static string Base64UrlEncode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            static string NewToken() => Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
            static string HashToken(string token) => Base64UrlEncode(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
            static IResult OAuthError(string error, string description, int status = 400) => Results.Json(new { error, error_description = description }, statusCode: status);
            static void NoStore(HttpContext context) { context.Response.Headers.CacheControl = "no-store"; context.Response.Headers.Pragma = "no-cache"; }
        }

        private static bool IsAllowedRedirectUri(string? value, bool development) => value?.Length <= 2048 && Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && uri.Fragment == "" && uri.UserInfo == "" && (uri.Scheme == "https" || (development
                && uri.Scheme == "http" && uri.IsLoopback && uri.HostNameType is UriHostNameType.IPv4 or UriHostNameType.IPv6));

        private sealed record OAuthState(string ServerUrl, Uri Issuer, ConcurrentDictionary<string, OAuthClient> Clients)
        {
            public ConcurrentDictionary<string, AuthCodeData> AuthCodes { get; } = new();
            public ConcurrentDictionary<string, RefreshTokenData> RefreshTokens { get; } = new();
        }

        private sealed record OAuthClient(string ClientId, string DisplayName, string[] RedirectUris);
        private sealed record AuthCodeData(int UserId, Guid SecurityStamp, string ClientId, string RedirectUri, string CodeChallenge, string Scope, DateTimeOffset ExpiresAt);
        private sealed class RefreshTokenData(int userId, Guid securityStamp, string clientId, string scope, DateTimeOffset expiresAt)
        {
            public int UserId { get; } = userId;
            public Guid SecurityStamp { get; } = securityStamp;
            public string ClientId { get; } = clientId;
            public string Scope { get; set; } = scope;
            public DateTimeOffset ExpiresAt { get; } = expiresAt;
            public string? CurrentTokenHash { get; set; }
            public bool Revoked { get; set; }
        }
        private sealed record ClientRegistration(
            [property: JsonPropertyName("redirect_uris")] string[]? RedirectUris,
            [property: JsonPropertyName("client_name")] string? ClientName,
            [property: JsonPropertyName("grant_types")] string[]? GrantTypes,
            [property: JsonPropertyName("response_types")] string[]? ResponseTypes,
            [property: JsonPropertyName("token_endpoint_auth_method")] string? TokenEndpointAuthMethod);
    }
}
