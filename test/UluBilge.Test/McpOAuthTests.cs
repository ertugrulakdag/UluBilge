using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.IdentityModel.JsonWebTokens;
using UluBilge.Core.Enums;

namespace UluBilge.Test;

[TestFixture]
public sealed class McpOAuthTests
{
    private OAuthTestApp host = null!;
    [SetUp] public async Task SetUp() { host = new OAuthTestApp(); await host.StartAsync(); }
    [TearDown] public async Task TearDown() => await host.DisposeAsync();

    [Test]
    public async Task Discovery_advertises_pkce_refresh_and_canonical_resource()
    {
        var metadata = await OAuthTestApp.JsonAsync(await host.Client.GetAsync("/.well-known/oauth-authorization-server"));
        Assert.That(metadata.GetProperty("issuer").GetString(), Is.EqualTo(OAuthTestApp.ServerUrl + "/"));
        Assert.That(metadata.GetProperty("authorization_endpoint").GetString(), Is.EqualTo(OAuthTestApp.ServerUrl + "/oauth/authorize"));
        Assert.That(metadata.GetProperty("grant_types_supported").EnumerateArray().Select(value => value.GetString()), Does.Contain("refresh_token"));
        Assert.That(metadata.GetProperty("code_challenge_methods_supported").EnumerateArray().Select(value => value.GetString()), Is.EqualTo(new[] { "S256" }));
        Assert.That(metadata.GetProperty("authorization_response_iss_parameter_supported").GetBoolean(), Is.True);
        Assert.That(metadata.TryGetProperty("registration_endpoint", out _), Is.False);
        var resource = await OAuthTestApp.JsonAsync(await host.Client.GetAsync("/.well-known/oauth-protected-resource/mcp"));
        Assert.That(resource.GetProperty("resource").GetString(), Is.EqualTo(OAuthTestApp.Resource));
        Assert.That(resource.GetProperty("authorization_servers")[0].GetString(), Is.EqualTo(OAuthTestApp.ServerUrl + "/"));
    }

    [Test]
    public async Task Anonymous_mcp_is_protected_in_every_build()
    {
        var response = await host.McpAsync(null);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(response.Headers.WwwAuthenticate.ToString(), Does.Contain("resource_metadata="));
        Assert.That(response.Headers.WwwAuthenticate.ToString(), Does.Contain("/.well-known/oauth-protected-resource/mcp"));
    }

    [Test]
    public async Task Local_desktop_client_can_authorize_and_use_mcp_with_pkce_in_development()
    {
        var form = await host.FormAsync(clientId: OAuthTestApp.LocalClientId, redirectUri: OAuthTestApp.LocalRedirectUri);
        Assert.That(form.Response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var login = await host.LoginAsync(form);
        Assert.That(login.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
        Assert.That(login.Headers.Location!.GetLeftPart(UriPartial.Path), Is.EqualTo(OAuthTestApp.LocalRedirectUri));
        var code = QueryHelpers.ParseQuery(login.Headers.Location.Query)["code"].ToString();
        var exchange = await host.ExchangeAsync(code, form.Verifier, clientId: OAuthTestApp.LocalClientId,
            redirectUri: OAuthTestApp.LocalRedirectUri);
        Assert.That(exchange.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var tokens = await OAuthTestApp.JsonAsync(exchange);
        Assert.That((await host.McpAsync(tokens.GetProperty("access_token").GetString())).IsSuccessStatusCode, Is.True);
        Assert.That((await host.RefreshAsync(tokens.GetProperty("refresh_token").GetString()!,
            clientId: OAuthTestApp.LocalClientId)).StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [TestCase("http://192.168.1.10:3334/oauth/callback")]
    [TestCase("http://example.com/oauth/callback")]
    [TestCase("http://localhost:3334/oauth/callback")]
    [TestCase("http://127.0.0.1:3334/oauth/callback#fragment")]
    public async Task Development_desktop_client_rejects_non_loopback_ip_or_fragment_redirects(string redirect)
    {
        await using var invalid = new OAuthTestApp(localRedirectUri: redirect);
        Assert.ThrowsAsync<InvalidOperationException>(() => invalid.StartAsync());
    }

    [Test]
    public async Task Production_does_not_register_development_desktop_client()
    {
        await using var production = new OAuthTestApp(production: true);
        await production.StartAsync();
        var form = await production.FormAsync(clientId: OAuthTestApp.LocalClientId, redirectUri: OAuthTestApp.LocalRedirectUri);
        Assert.That(form.Response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(form.Response.Headers.Location, Is.Null);
        Assert.That(production.Work.Commands, Is.Zero);
    }

    [Test]
    public async Task Consent_form_encodes_state_and_requires_csrf()
    {
        var form = await host.FormAsync(state: "\"><script>alert(1)</script>");
        Assert.That(form.Response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(form.Html, Does.Not.Contain("<script>"));
        Assert.That(form.Response.Headers.GetValues("Content-Security-Policy").Single(), Does.Contain("frame-ancestors 'none'"));
        Assert.That(form.Response.Headers.CacheControl?.NoStore, Is.True);
        Assert.That((await host.LoginAsync(form, csrf: false)).StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(host.Work.Commands, Is.Zero);
    }

    [TestCase(OAuthTestApp.ClientId, OAuthTestApp.RedirectUri)]
    [TestCase("ulubilge-claude", "https://claude.ai/api/mcp/auth_callback")]
    [TestCase(OAuthTestApp.LocalClientId, OAuthTestApp.LocalRedirectUri)]
    public async Task Login_form_csp_allows_only_self_and_the_validated_clients_callback_origin(string clientId, string redirectUri)
    {
        var form = await host.FormAsync(clientId: clientId, redirectUri: redirectUri);
        Assert.That(form.Response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var policy = form.Response.Headers.GetValues("Content-Security-Policy").Single();
        var formAction = policy.Split(';', StringSplitOptions.TrimEntries).Single(value => value.StartsWith("form-action "));
        Assert.That(formAction.Split(' ', StringSplitOptions.RemoveEmptyEntries), Is.EqualTo(new[]
        {
            "form-action", "'self'", new Uri(redirectUri).GetLeftPart(UriPartial.Authority)
        }));
        Assert.That(policy, Does.Contain("default-src 'none'").And.Contain("frame-ancestors 'none'").And.Contain("base-uri 'none'"));

        var failedLogin = await host.LoginAsync(form, password: "wrong-password");
        Assert.That(failedLogin.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(failedLogin.Headers.GetValues("Content-Security-Policy").Single(), Is.EqualTo(policy));
    }

    [Test]
    public async Task Authorization_echoes_state_and_issuer()
    {
        var response = await host.LoginAsync(await host.FormAsync(state: "opaque & state"));
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
        var query = QueryHelpers.ParseQuery(response.Headers.Location!.Query);
        Assert.That(query["state"].ToString(), Is.EqualTo("opaque & state"));
        Assert.That(query["iss"].ToString(), Is.EqualTo(OAuthTestApp.ServerUrl + "/"));
        Assert.That(query["code"].ToString(), Is.Not.Empty);
    }

    [TestCase("https://evil.example/callback")]
    [TestCase("https://chatgpt.com/connector_platform_oauth_redirect/extra")]
    public async Task Unregistered_redirect_is_not_followed(string redirect)
    {
        var form = await host.FormAsync(redirectUri: redirect);
        Assert.That(form.Response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(form.Response.Headers.Location, Is.Null);
    }

    [Test]
    public async Task Unknown_client_is_rejected_before_login()
    {
        var form = await host.FormAsync(clientId: "unknown-client");
        Assert.That(form.Response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(host.Work.Commands, Is.Zero);
    }

    [Test]
    public async Task Plain_pkce_is_rejected()
    {
        var form = await host.FormAsync(method: "plain");
        Assert.That(form.Response.StatusCode, Is.Not.EqualTo(HttpStatusCode.OK));
        Assert.That(form.Html, Does.Not.Contain("name=\"password\""));
    }

    [TestCase("https://evil.example/mcp")]
    [TestCase("")]
    public async Task Authorization_rejects_wrong_or_missing_resource(string resource)
    {
        var form = await host.FormAsync(resource: resource);
        Assert.That(form.Response.StatusCode, Is.Not.EqualTo(HttpStatusCode.OK));
        Assert.That(form.Html, Does.Not.Contain("name=\"password\""));
    }

    [Test]
    public async Task User_must_explicitly_allow_connection()
    {
        var response = await host.LoginAsync(await host.FormAsync(), decision: "deny");
        Assert.That(QueryHelpers.ParseQuery(response.Headers.Location!.Query)["error"].ToString(), Is.EqualTo("access_denied"));
        Assert.That(host.Work.Commands, Is.Zero);
    }

    [Test]
    public async Task Wrong_password_is_rejected_and_failure_is_recorded()
    {
        var response = await host.LoginAsync(await host.FormAsync(), password: "wrong-password");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(host.Users.User!.AccessFailedCount, Is.EqualTo(1));
        Assert.That(host.Work.Commands, Is.EqualTo(1));
    }

    [Test]
    public async Task User_without_report_or_admin_role_cannot_authorize()
    {
        host.Users.User!.RoleEnum = RoleEnum.Region;
        var response = await host.LoginAsync(await host.FormAsync());
        Assert.That(QueryHelpers.ParseQuery(response.Headers.Location!.Query)["error"].ToString(), Is.EqualTo("access_denied"));
    }

    [Test]
    public async Task Code_issues_short_lived_mcp_access_token_and_refresh_token()
    {
        var (code, verifier) = await host.CodeAsync();
        var response = await host.ExchangeAsync(code, verifier);
        var tokens = await OAuthTestApp.JsonAsync(response);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(tokens.GetProperty("token_type").GetString(), Is.EqualTo("Bearer"));
        Assert.That(tokens.GetProperty("expires_in").GetInt32(), Is.InRange(1, 300));
        Assert.That(tokens.GetProperty("refresh_token").GetString(), Is.Not.Null.And.Not.Empty);
        Assert.That(response.Headers.CacheControl?.NoStore, Is.True);
        var token = tokens.GetProperty("access_token").GetString()!;
        var jwt = new JsonWebToken(token);
        Assert.That(jwt.Audiences, Is.EqualTo(new[] { OAuthTestApp.Resource }));
        Assert.That(jwt.Subject, Is.EqualTo("7"));
        Assert.That(jwt.Claims.Any(claim => claim.Type.Contains("password", StringComparison.OrdinalIgnoreCase)), Is.False);
        Assert.That(jwt.Claims.Any(claim => claim.Type == "ulubilge_security_stamp"), Is.False);
        Assert.That((await host.McpAsync(token)).IsSuccessStatusCode, Is.True);
        using var api = new HttpRequestMessage(HttpMethod.Get, "/test/api");
        api.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        Assert.That((await host.Client.SendAsync(api)).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task Wrong_verifier_cannot_exchange_code()
    {
        var (code, _) = await host.CodeAsync();
        var response = await host.ExchangeAsync(code, new string('x', 43));
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That((await OAuthTestApp.JsonAsync(response)).TryGetProperty("access_token", out _), Is.False);
    }

    [Test]
    public async Task Authorization_code_is_single_use()
    {
        var (code, verifier) = await host.CodeAsync();
        Assert.That((await host.ExchangeAsync(code, verifier)).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await host.ExchangeAsync(code, verifier)).StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task Code_is_bound_to_client_and_redirect()
    {
        var (code, verifier) = await host.CodeAsync();
        Assert.That((await host.ExchangeAsync(code, verifier, clientId: "ulubilge-claude", redirectUri: "https://claude.ai/api/mcp/auth_callback")).StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That((await host.ExchangeAsync(code, verifier, redirectUri: "https://evil.example/callback")).StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task Token_endpoint_rejects_different_resource()
    {
        var (code, verifier) = await host.CodeAsync();
        Assert.That((await host.ExchangeAsync(code, verifier, resource: "https://evil.example/mcp")).StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task Refresh_rotates_and_replay_revokes_the_grant()
    {
        var (code, verifier) = await host.CodeAsync();
        var original = await OAuthTestApp.JsonAsync(await host.ExchangeAsync(code, verifier));
        var oldRefresh = original.GetProperty("refresh_token").GetString()!;
        var response = await host.RefreshAsync(oldRefresh);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var refreshed = await OAuthTestApp.JsonAsync(response);
        var newRefresh = refreshed.GetProperty("refresh_token").GetString()!;
        Assert.That(newRefresh, Is.Not.EqualTo(oldRefresh));
        Assert.That((await host.McpAsync(refreshed.GetProperty("access_token").GetString())).IsSuccessStatusCode, Is.True);
        Assert.That((await host.RefreshAsync(oldRefresh)).StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That((await host.RefreshAsync(newRefresh)).StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task Refresh_is_bound_to_client()
    {
        var (code, verifier) = await host.CodeAsync();
        var tokens = await OAuthTestApp.JsonAsync(await host.ExchangeAsync(code, verifier));
        Assert.That((await host.RefreshAsync(tokens.GetProperty("refresh_token").GetString()!, "ulubilge-claude")).StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [TestCase("inactive")]
    [TestCase("deleted")]
    [TestCase("role-removed")]
    public async Task Refresh_rechecks_user_and_roles(string change)
    {
        var (code, verifier) = await host.CodeAsync();
        var tokens = await OAuthTestApp.JsonAsync(await host.ExchangeAsync(code, verifier));
        if (change == "inactive") host.Users.User!.IsActive = false;
        if (change == "deleted") host.Users.User = null;
        if (change == "role-removed") host.Users.User!.RoleEnum = RoleEnum.Region;
        Assert.That((await host.RefreshAsync(tokens.GetProperty("refresh_token").GetString()!)).StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task Api_login_token_cannot_be_used_as_mcp_token()
    {
        var login = await host.Client.PostAsJsonAsync("/v1/Auth/login", new { userName = "reporter", password = OAuthTestApp.Password });
        var token = (await OAuthTestApp.JsonAsync(login)).GetProperty("data").GetProperty("accessToken").GetString();
        Assert.That((await host.McpAsync(token)).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task Refresh_is_rejected_after_security_stamp_change()
    {
        var (code, verifier) = await host.CodeAsync();
        var tokens = await OAuthTestApp.JsonAsync(await host.ExchangeAsync(code, verifier));
        host.Users.User!.SecurityStamp = Guid.NewGuid();
        Assert.That((await host.RefreshAsync(tokens.GetProperty("refresh_token").GetString()!)).StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task Expired_authorization_code_is_rejected()
    {
        var (code, verifier) = await host.CodeAsync();
        host.Clock.Advance(TimeSpan.FromMinutes(3));
        Assert.That((await host.ExchangeAsync(code, verifier)).StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task Expired_access_token_is_rejected()
    {
        var (code, verifier) = await host.CodeAsync();
        var tokens = await OAuthTestApp.JsonAsync(await host.ExchangeAsync(code, verifier));
        host.Clock.Advance(TimeSpan.FromMinutes(6));
        Assert.That((await host.McpAsync(tokens.GetProperty("access_token").GetString())).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task Expired_refresh_token_is_rejected()
    {
        var (code, verifier) = await host.CodeAsync();
        var tokens = await OAuthTestApp.JsonAsync(await host.ExchangeAsync(code, verifier));
        host.Clock.Advance(TimeSpan.FromDays(8));
        Assert.That((await host.RefreshAsync(tokens.GetProperty("refresh_token").GetString()!)).StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task Production_refresh_survives_restart_with_persisted_keys_and_store()
    {
        await using var production = new OAuthTestApp(production: true);
        await production.StartAsync();
        var (code, verifier) = await production.CodeAsync();
        var tokens = await OAuthTestApp.JsonAsync(await production.ExchangeAsync(code, verifier));
        await production.RestartAsync();
        var response = await production.RefreshAsync(tokens.GetProperty("refresh_token").GetString()!);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await production.McpAsync((await OAuthTestApp.JsonAsync(response)).GetProperty("access_token").GetString())).IsSuccessStatusCode, Is.True);
    }
}
