namespace UluBilge.Api.Authentication
{
    public sealed class JwtOptions
    {
        public const string SectionName = "Jwt";

        public string Issuer { get; set; } = string.Empty;
        public string Audience { get; set; } = string.Empty;
        public string? SigningKeyBase64 { get; set; }
        public int AccessTokenLifetimeMinutes { get; set; } = 5;
    }
}