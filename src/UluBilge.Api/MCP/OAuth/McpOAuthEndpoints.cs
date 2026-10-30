using System.Collections.Immutable;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Net;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using UluBilge.App.Commands.AuthHandler;
using UluBilge.Core.Commands;
using UluBilge.Core.Entities;
using UluBilge.Core.Enums;
using UluBilge.Data.Abstract;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace UluBilge.Api.MCP.OAuth;

public static partial class McpOAuthExtensions
{
    private const string SecurityStampClaim = "ulubilge_security_stamp";

    private static async Task<IResult> AuthorizeAsync(HttpContext context, McpOAuthOptions settings,
        IAntiforgery antiforgery, IOpenIddictApplicationManager applications,
        IAsyncCommandHandler<LoginAsync.Command, LoginAsync.Result?> loginHandler,
        IOptionsMonitor<JwtBearerOptions> jwtOptions, CancellationToken ct)
    {
        var request = context.GetOpenIddictServerRequest() ?? throw new InvalidOperationException("OAuth isteği bulunamadı.");
        if (!request.GetResources().SequenceEqual([settings.Resource.AbsoluteUri], StringComparer.Ordinal))
            return OAuthError(Errors.InvalidTarget, "İstenen kaynak bu MCP sunucusuna ait değil.");
        if (!request.HasScope(McpOAuthOptions.Scope))
            return OAuthError(Errors.InvalidScope, "mcp:tools kapsamı gereklidir.");

        var application = await applications.FindByClientIdAsync(request.ClientId!, ct);
        var clientName = application is null ? "İstemci" : await applications.GetDisplayNameAsync(application, ct) ?? request.ClientId!;
        if (HttpMethods.IsGet(context.Request.Method))
            return LoginForm(context, request, clientName, antiforgery);

        try
        {
            await antiforgery.ValidateRequestAsync(context);
        }
        catch (AntiforgeryValidationException)
        {
            return LoginForm(context, request, clientName, antiforgery, "Formun süresi doldu. Tekrar deneyin.", 400);
        }

        var form = await context.Request.ReadFormAsync(ct);
        if (form["decision"] == "deny") return OAuthError(Errors.AccessDenied, "Kullanıcı bağlantıya izin vermedi.");
        if (form["decision"] != "allow") return OAuthError(Errors.InvalidRequest, "Bağlantı onayı gereklidir.");

        var command = new LoginAsync.Command { UserName = form["userName"].ToString(), Password = form["password"].ToString() };
        var errors = new List<ValidationResult>();
        if (!Validator.TryValidateObject(command, new ValidationContext(command), errors, validateAllProperties: true))
            return LoginForm(context, request, clientName, antiforgery, "Kullanıcı adı ve parola geçerli biçimde girilmelidir.", 400);

        var login = await loginHandler.HandleAsync(command, ct);
        if (login is null)
            return LoginForm(context, request, clientName, antiforgery, "Kullanıcı adı veya parola geçersiz.", 401);

        // Mevcut LoginAsync parola, kilit ve kullanici kontrollerini uygular.
        var validation = await new JsonWebTokenHandler().ValidateTokenAsync(login.AccessToken,
            jwtOptions.Get(JwtBearerDefaults.AuthenticationScheme).TokenValidationParameters);
        if (!validation.IsValid) throw new InvalidOperationException("Giriş tokenı doğrulanamadı.");
        var user = new ClaimsPrincipal(validation.ClaimsIdentity);
        if (!user.IsInRole(nameof(RoleEnum.Report)) && !user.IsInRole(nameof(RoleEnum.Admin)))
            return OAuthError(Errors.AccessDenied, "Bu bağlantı için Report veya Admin rolü gereklidir.");

        var identity = new ClaimsIdentity(TokenValidationParameters.DefaultAuthenticationType, Claims.Name, Claims.Role);
        identity.AddClaims(user.Claims.Where(claim => claim.Type is Claims.Subject or Claims.Name or Claims.Role));
        var principal = new ClaimsPrincipal(identity);
        principal.SetScopes(request.GetScopes());
        principal.SetResources(settings.Resource.AbsoluteUri);
        SetClaimDestinations(principal);
        return Results.SignIn(principal, authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private static async Task<IResult> ExchangeAsync(HttpContext context, McpOAuthOptions settings,
        IUserRepository users, IUnitOfWork unitOfWork, CancellationToken ct)
    {
        var request = context.GetOpenIddictServerRequest() ?? throw new InvalidOperationException("OAuth isteği bulunamadı.");
        if (!request.IsAuthorizationCodeGrantType() && !request.IsRefreshTokenGrantType())
            return OAuthError(Errors.UnsupportedGrantType, "Bu token akışı desteklenmiyor.");
        if (!request.GetResources().SequenceEqual([settings.Resource.AbsoluteUri], StringComparer.Ordinal))
            return OAuthError(Errors.InvalidTarget, "İstenen kaynak bu MCP sunucusuna ait değil.");

        // Kod, PKCE, istemci ve refresh token kontrollerini OpenIddict yapar.
        var authentication = await context.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        var principal = authentication.Principal;
        if (principal is null || !int.TryParse(principal.GetClaim(Claims.Subject), out var userId))
            return OAuthError(Errors.InvalidGrant, "Yetkilendirme geçersiz veya süresi dolmuş.");

        var user = await unitOfWork.ExecuteQueryAsync(token => users.GetByUserIdAsync(userId, token), ct);
        var knownRoles = Enum.GetValues<RoleEnum>().Aggregate(RoleEnum.None, (mask, role) => mask | role);
        if (user is null || !user.IsActive || user.LockoutEndUtc > DateTime.UtcNow || (user.RoleEnum & ~knownRoles) != 0
            || (!user.RoleEnum.HasFlag(RoleEnum.Report) && !user.RoleEnum.HasFlag(RoleEnum.Admin)))
        {
            return OAuthError(Errors.InvalidGrant, "Kullanıcının bu bağlantı için erişimi artık geçerli değil.");
        }
        if (request.IsRefreshTokenGrantType() && principal.GetClaim(SecurityStampClaim) != user.SecurityStamp.ToString("N"))
            return OAuthError(Errors.InvalidGrant, "Hesabın güvenlik bilgileri değişti. Yeniden giriş yapın.");

        // Yenilemede eski tokenin rolleri yerine veritabanindaki guncel roller kullanilir.
        principal.SetClaim(SecurityStampClaim, user.SecurityStamp.ToString("N"));
        principal.SetClaim(Claims.Subject, user.Id.ToString(CultureInfo.InvariantCulture));
        principal.SetClaim(Claims.Name, user.UserName);
        principal.SetClaims(Claims.Role, Enum.GetValues<RoleEnum>().Where(role => role != RoleEnum.None && user.RoleEnum.HasFlag(role))
            .Select(role => role.ToString()).ToImmutableArray());
        SetClaimDestinations(principal);
        return Results.SignIn(principal, authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private static void SetClaimDestinations(ClaimsPrincipal principal)
    {
        foreach (var claim in principal.Claims)
        {
            if (claim.Type is Claims.Subject or Claims.Name or Claims.Role)
                claim.SetDestinations(Destinations.AccessToken);
        }
    }

    private static IResult OAuthError(string error, string description) => Results.Forbid(
        authenticationSchemes: [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme],
        properties: new AuthenticationProperties(new Dictionary<string, string?>
        {
            [OpenIddictServerAspNetCoreConstants.Properties.Error] = error,
            [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = description
        }));

    private static IResult LoginForm(HttpContext context, OpenIddictRequest request, string clientName,
        IAntiforgery antiforgery, string? error = null, int status = 200)
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.Pragma = "no-cache";
        // OpenIddict tam redirect_uri degerini bu handler'dan once dogrular.
        // Chromium form POST'unun ardindaki OAuth yonlendirmesini de CSP ile denetler.
        var callbackOrigin = new Uri(request.RedirectUri!, UriKind.Absolute).GetLeftPart(UriPartial.Authority);
        context.Response.Headers.ContentSecurityPolicy = $"default-src 'none'; style-src 'unsafe-inline'; form-action 'self' {callbackOrigin}; frame-ancestors 'none'; base-uri 'none'";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
        context.Response.Headers.XContentTypeOptions = "nosniff";
        var csrf = antiforgery.GetAndStoreTokens(context);
        var fields = new Dictionary<string, string?>
        {
            ["client_id"] = request.ClientId, ["redirect_uri"] = request.RedirectUri,
            ["response_type"] = request.ResponseType, ["scope"] = request.Scope, ["state"] = request.State,
            ["resource"] = request.GetResources().Single(), ["code_challenge"] = request.CodeChallenge,
            ["code_challenge_method"] = request.CodeChallengeMethod, [csrf.FormFieldName] = csrf.RequestToken
        };
        var hidden = string.Join("\n", fields.Where(field => field.Value is not null).Select(field =>
            $"<input type=\"hidden\" name=\"{WebUtility.HtmlEncode(field.Key)}\" value=\"{WebUtility.HtmlEncode(field.Value)}\" />"));
        var html = $$"""
            <!doctype html>
            <html lang="tr"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
            <title>UluBilge bağlantısı</title>
            <style>body{font:16px system-ui;max-width:430px;margin:10vh auto;padding:24px}input{display:block;width:95%;padding:10px;margin:6px 0 18px}button{padding:10px 16px;margin:8px 8px 0 0}.error{color:#b42318}</style>
            </head><body><h1>UluBilge'e bağlan</h1>
            <p><strong>{{WebUtility.HtmlEncode(clientName)}}</strong>, ürün satış raporlarını okumak için izin istiyor.</p>
            <p>{{(request.HasScope(Scopes.OfflineAccess) ? "Bağlantının devam etmesi için erişim en fazla 7 gün boyunca otomatik yenilenebilir." : "Erişim kısa süreliğine verilecektir.")}}</p>
            <p class="error" role="alert">{{WebUtility.HtmlEncode(error)}}</p>
            <form method="post" action="/oauth/authorize">
            {{hidden}}
            <label>Kullanıcı adı<input name="userName" maxlength="64" autocomplete="username" required></label>
            <label>Parola<input type="password" name="password" maxlength="128" autocomplete="current-password" required></label>
            <button type="submit" name="decision" value="allow">Giriş yap ve izin ver</button>
            <button type="submit" name="decision" value="deny" formnovalidate>İptal</button>
            </form></body></html>
            """;
        return Results.Content(html, "text/html; charset=utf-8", Encoding.UTF8, status);
    }
}
