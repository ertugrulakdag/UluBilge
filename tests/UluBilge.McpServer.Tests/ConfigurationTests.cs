using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using UluBilge.McpServer.Configuration;

namespace UluBilge.McpServer.Tests;

public sealed class ConfigurationTests
{
    [Theory]
    [InlineData("https://example.test/api/", "Production", true)]
    [InlineData("http://localhost:5156/", "Development", true)]
    [InlineData("http://127.0.0.1:5156/", "Development", true)]
    [InlineData("http://localhost:5156/", "Production", false)]
    [InlineData("http://remote.example.test/", "Development", false)]
    [InlineData("https://user:password@example.test/", "Development", false)]
    [InlineData("https://example.test/?key=secret", "Development", false)]
    [InlineData("https://example.test/#fragment", "Development", false)]
    [InlineData("file:///tmp/api", "Development", false)]
    public void OnlyApprovedApiAddressesAreAccepted(string address, string environment, bool valid)
    {
        var validator = new UluBilgeApiOptionsValidator(new TestEnvironment(environment));
        var result = validator.Validate(null, new UluBilgeApiOptions
        {
            BaseUrl = address,
            UserName = ApiTestContext.UserName,
            Password = ApiTestContext.Password
        });
        Assert.Equal(valid, result.Succeeded);
        Assert.DoesNotContain("key=secret", result.FailureMessage ?? "");
    }

    [Fact]
    public void MissingCredentialsAndInvalidTimeoutHaveUsefulErrorsWithoutValues()
    {
        var validator = new UluBilgeApiOptionsValidator(new TestEnvironment("Production"));
        var result = validator.Validate(null, new UluBilgeApiOptions { TimeoutSeconds = 0 });
        Assert.True(result.Failed);
        Assert.Equal(3, result.Failures!.Count());
    }

    private sealed class TestEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "McpTests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
