using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using UluBilge.Api.Models;
using UluBilge.App.Commands.AuthHandler;

namespace UluBilge.Test;

// Çalışan API'ye HTTP isteği gönderir. API'yi veya veritabanını oluşturmaz.
internal sealed class ApiTestClient : IDisposable
{
    public HttpClient Client { get; } = new()
    {
        BaseAddress = new Uri(Environment.GetEnvironmentVariable("ULUBILGE_TEST_URL") ?? "https://localhost:7187"),
        Timeout = TimeSpan.FromSeconds(30)
    };

    public async Task<LoginAsync.Result> LoginAsync()
    {
        using var response = await Client.PostAsJsonAsync("/v1/Auth/Login", new
        {
            userName = Environment.GetEnvironmentVariable("ULUBILGE_TEST_USERNAME") ?? "admin",
            password = Environment.GetEnvironmentVariable("ULUBILGE_TEST_PASSWORD") ?? "admin"
        });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), "API adresini ve giriş bilgilerini kontrol edin.");
        var body = await response.Content.ReadFromJsonAsync<BaseResponse<LoginAsync.Result>>();
        Assert.That(body, Is.Not.Null);
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body!.Data.AccessToken);
        return body.Data;
    }

    public void Dispose() => Client.Dispose();
}
