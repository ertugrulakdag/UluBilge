using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using UluBilge.Api.Controllers;
using UluBilge.Api.Extentions;

namespace UluBilge.Test;

[TestFixture]
public sealed class OpenApiSchemaTests
{
    private WebApplication application = null!;
    private HttpClient client = null!;

    [OneTimeSetUp]
    public async Task SetUp()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development,
            ApplicationName = typeof(AuthController).Assembly.FullName
        });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddControllers().AddApplicationPart(typeof(AuthController).Assembly);
        builder.Services.AddApiOpenApi(builder.Environment);
        application = builder.Build();
        application.MapControllers();
        application.MapOpenApi().AllowAnonymous();
        await application.StartAsync();
        client = application.GetTestClient();
    }

    [OneTimeTearDown]
    public async Task TearDown()
    {
        client?.Dispose();
        if (application is not null)
            await application.DisposeAsync();
    }

    [Test]
    public async Task RequestSchemas_SeparateNestedCommands_AndRefreshRequiresOnlyRefreshToken()
    {
        using var document = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));
        var root = document.RootElement;
        var loginBody = RequestBody(root, "/v1/Auth/Login", "post");
        var refreshBody = RequestBody(root, "/v1/Auth/RefreshToken", "post");
        var addBody = RequestBody(root, "/v1/Region/Add", "post");
        var updateBody = RequestBody(root, "/v1/Region/Update", "put");
        var references = new[] { loginBody, refreshBody, addBody, updateBody }
            .Select(body => body.GetProperty("schema").GetProperty("$ref").GetString()).ToArray();
        Assert.That(references.Distinct().Count(), Is.EqualTo(4));

        var refreshSchema = Resolve(root, refreshBody.GetProperty("schema"));
        Assert.Multiple(() =>
        {
            Assert.That(PropertyNames(Resolve(root, loginBody.GetProperty("schema"))), Is.EquivalentTo(new[] { "userName", "password" }));
            Assert.That(PropertyNames(refreshSchema), Is.EqualTo(new[] { "refreshToken" }));
            Assert.That(refreshSchema.GetProperty("required").EnumerateArray().Select(value => value.GetString()), Does.Contain("refreshToken"));
            Assert.That(refreshSchema.GetProperty("properties").GetProperty("refreshToken").GetProperty("minLength").GetInt32(), Is.EqualTo(43));
            Assert.That(PropertyNames(Resolve(root, addBody.GetProperty("schema"))), Is.EquivalentTo(new[] { "regionId", "regionDescription" }));
            Assert.That(PropertyNames(Resolve(root, updateBody.GetProperty("schema"))), Is.EquivalentTo(new[] { "regionId", "regionDescription" }));
            Assert.That(refreshBody.TryGetProperty("example", out _), Is.False);
            Assert.That(loginBody.GetProperty("example").GetProperty("userName").GetString(), Is.EqualTo("admin"));
        });
    }

    [Test]
    public async Task ResponseSchemas_SeparateGenericResponsesWithDifferentNestedResults()
    {
        using var document = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));
        var root = document.RootElement;
        var login = ResponseData(root, "/v1/Auth/Login", "post");
        var me = ResponseData(root, "/v1/Auth/Me", "get");
        var add = ResponseData(root, "/v1/Region/Add", "post");
        var update = ResponseData(root, "/v1/Region/Update", "put");
        Assert.Multiple(() =>
        {
            Assert.That(PropertyNames(login), Does.Contain("accessToken").And.Contain("refreshToken"));
            Assert.That(PropertyNames(me), Is.EquivalentTo(new[] { "id", "userName", "roleEnum", "isActive" }));
            Assert.That(PropertyNames(add), Is.EquivalentTo(new[] { "status", "regionId" }));
            Assert.That(PropertyNames(update), Is.EqualTo(new[] { "status" }));
        });
    }

    private static JsonElement RequestBody(JsonElement root, string path, string method) =>
        root.GetProperty("paths").GetProperty(path).GetProperty(method).GetProperty("requestBody").GetProperty("content").GetProperty("application/json");

    private static JsonElement ResponseData(JsonElement root, string path, string method)
    {
        var response = root.GetProperty("paths").GetProperty(path).GetProperty(method).GetProperty("responses").GetProperty("200")
            .GetProperty("content").GetProperty("application/json").GetProperty("schema");
        return Resolve(root, Resolve(root, response).GetProperty("properties").GetProperty("data"));
    }

    private static JsonElement Resolve(JsonElement root, JsonElement schema) => schema.TryGetProperty("$ref", out var reference)
        ? root.GetProperty("components").GetProperty("schemas").GetProperty(reference.GetString()!.Split('/')[^1])
        : schema;

    private static string[] PropertyNames(JsonElement schema) => schema.GetProperty("properties").EnumerateObject().Select(property => property.Name).ToArray();
}
