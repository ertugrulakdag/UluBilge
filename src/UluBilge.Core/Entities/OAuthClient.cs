namespace UluBilge.Core.Entities
{
    public sealed class OAuthClient
    {
        public string ClientId { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAtUtc { get; set; }
        public string[] RedirectUris { get; set; } = [];
    }
}
