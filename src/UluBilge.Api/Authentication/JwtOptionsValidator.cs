using Microsoft.Extensions.Options;

namespace UluBilge.Api.Authentication;

internal sealed class JwtOptionsValidator(IHostEnvironment environment) : IValidateOptions<JwtOptions>
{
    public ValidateOptionsResult Validate(string? name, JwtOptions options)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(options.Issuer) || options.Issuer.Length > 200)
        {
            errors.Add("Jwt:Issuer must contain between 1 and 200 characters.");
        }

        if (string.IsNullOrWhiteSpace(options.Audience) || options.Audience.Length > 200)
        {
            errors.Add("Jwt:Audience must contain between 1 and 200 characters.");
        }

        if (options.AccessTokenLifetimeMinutes is < 1 or > 15)
        {
            errors.Add("Jwt:AccessTokenLifetimeMinutes must be between 1 and 15.");
        }

        if (options.SigningKeyBase64 is null)
        {
            if (!environment.IsDevelopment())
            {
                errors.Add("Jwt:SigningKeyBase64 must be supplied through a secret store outside Development.");
            }
        }
        else
        {
            Span<byte> keyBuffer = stackalloc byte[128];
            if (!Convert.TryFromBase64String(options.SigningKeyBase64, keyBuffer, out var bytesWritten)
                || bytesWritten < 32)
            {
                errors.Add("Jwt:SigningKeyBase64 must encode 32 to 128 cryptographically random bytes.");
            }

            System.Security.Cryptography.CryptographicOperations.ZeroMemory(keyBuffer);
        }

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
