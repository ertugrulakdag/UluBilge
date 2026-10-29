using Microsoft.Extensions.Options;

namespace UluBilge.Api.Authentication
{
    internal sealed class JwtOptionsValidator(IHostEnvironment environment) : IValidateOptions<JwtOptions>
    {
        public ValidateOptionsResult Validate(string? name, JwtOptions options)
        {
            var errors = new List<string>();
            if (string.IsNullOrWhiteSpace(options.Issuer) || options.Issuer.Length > 200)
            {
                errors.Add("Jwt:Issuer alanı 1 ile 200 karakter arasında olmalıdır.");
            }

            if (string.IsNullOrWhiteSpace(options.Audience) || options.Audience.Length > 200)
            {
                errors.Add("Jwt:Audience alanı 1 ile 200 karakter arasında olmalıdır.");
            }

            if (options.AccessTokenLifetimeMinutes is < 1 or > 15)
            {
                errors.Add("Jwt:AccessTokenLifetimeMinutes değeri 1 ile 15 dakika arasında olmalıdır.");
            }

            if (options.RefreshTokenLifetimeDays is < 1 or > 30)
            {
                errors.Add("Jwt:RefreshTokenLifetimeDays değeri 1 ile 30 gün arasında olmalıdır.");
            }

            if (options.SigningKeyBase64 is null)
            {
                if (!environment.IsDevelopment())
                {
                    errors.Add("Development dışındaki ortamlarda Jwt:SigningKeyBase64 değeri güvenli bir gizli bilgi deposundan sağlanmalıdır.");
                }
            }
            else
            {
                Span<byte> keyBuffer = stackalloc byte[128];
                if (!Convert.TryFromBase64String(options.SigningKeyBase64, keyBuffer, out var bytesWritten) || bytesWritten < 32)
                {
                    errors.Add("Jwt:SigningKeyBase64 değeri, kriptografik olarak rastgele üretilmiş 32 ile 128 baytlık bir anahtarın Base64 karşılığı olmalıdır.");
                }

                System.Security.Cryptography.CryptographicOperations.ZeroMemory(keyBuffer);
            }

            return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
        }
    }
}
