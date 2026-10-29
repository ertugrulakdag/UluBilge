using UluBilge.Core.Enums;

namespace UluBilge.Core.Entities
{
    public sealed class User
    {
        public int Id { get; set; }
        public string UserName { get; set; } = string.Empty;
        public string NormalizedUserName { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public bool EmailConfirmed { get; set; }
        public int AccessFailedCount { get; set; }
        public DateTime? LockoutEndUtc { get; set; }
        public Guid SecurityStamp { get; set; }
        public RoleEnum RoleEnum { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public DateTime? UpdatedAtUtc { get; set; }
        public DateTime? LastLoginAtUtc { get; set; }
        public bool IsActive { get; set; }
    }
}
