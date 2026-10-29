using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace UluBilge.Api.Authentication
{
    internal sealed class JwtKeyMaterial
    {
        public JwtKeyMaterial(IOptions<JwtOptions> options, ILogger<JwtKeyMaterial> logger)
        {
            var signingKey = options.Value.SigningKeyBase64;
            var keyBytes = signingKey is null
                ? RandomNumberGenerator.GetBytes(32)
                : Convert.FromBase64String(signingKey);

            Key = new SymmetricSecurityKey(keyBytes);
            SigningCredentials = new SigningCredentials(Key, SecurityAlgorithms.HmacSha256);

            if (signingKey is null)
            {
                logger.LogWarning("Geliştirme ortamındaki JWT imzalama anahtarı geçicidir. API yeniden başlatıldığında mevcut erişim belirteçleri geçersiz olur. Anahtarı kalıcı yapmak için Jwt:SigningKeyBase64 değerini user-secrets içinde yapılandırın.");
            }
        }

        public SymmetricSecurityKey Key { get; }
        public SigningCredentials SigningCredentials { get; }
    }
}
