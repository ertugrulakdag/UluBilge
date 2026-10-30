using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace UluBilge.McpServer.Configuration;

public sealed class UluBilgeApiOptionsValidator(IHostEnvironment environment) : IValidateOptions<UluBilgeApiOptions>
{
    public ValidateOptionsResult Validate(string? name, UluBilgeApiOptions options)
    {
        var errors = new List<string>();
        if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps
                && !(environment.IsDevelopment() && uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback))
            || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
        {
            errors.Add("UluBilgeApi:BaseUrl geçerli bir HTTPS adresi olmalıdır; Development ortamında yerel HTTP adresi kullanılabilir. Adres kullanıcı bilgisi, sorgu veya fragment içeremez.");
        }

        if (string.IsNullOrWhiteSpace(options.UserName) || options.UserName.Length > 64)
        {
            errors.Add("UluBilgeApi:UserName zorunludur ve en fazla 64 karakter olabilir.");
        }
        if (string.IsNullOrWhiteSpace(options.Password) || options.Password.Length > 128)
        {
            errors.Add("UluBilgeApi:Password zorunludur ve en fazla 128 karakter olabilir.");
        }
        if (options.TimeoutSeconds is < 1 or > 120)
        {
            errors.Add("UluBilgeApi:TimeoutSeconds 1 ile 120 arasında olmalıdır.");
        }

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
