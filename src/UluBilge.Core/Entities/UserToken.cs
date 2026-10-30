namespace UluBilge.Core.Entities
{
    public sealed class UserToken
    {
        public long Id { get; set; }
        public int UserId { get; set; }
        public byte[] TokenHash { get; set; } = [];
        public Guid FamilyId { get; set; }
        public Guid? SecurityStamp { get; set; }
        public string? OAuthClientId { get; set; }
        public string? OAuthResource { get; set; }
        public string? OAuthScope { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public DateTime ExpiresAtUtc { get; set; }
        public DateTime? ConsumedAtUtc { get; set; }
        public DateTime? RevokedAtUtc { get; set; }
        public long? ReplacedByTokenId { get; set; }
    }
}
