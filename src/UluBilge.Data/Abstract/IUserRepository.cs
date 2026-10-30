using UluBilge.Core.Entities;

namespace UluBilge.Data.Abstract
{
    public interface IUserRepository
    {
        Task<User?> GetByNormalizedUserNameForUpdateAsync(string normalizedUserName, CancellationToken ct = default);
        Task<User?> GetByUserIdAsync(int userId, CancellationToken ct = default);
        Task RecordFailedLoginAsync(int userId, int accessFailedCount, DateTime? lockoutEndUtc, DateTime now, CancellationToken ct = default);
        Task RecordSuccessfulLoginAsync(User user, DateTime now, CancellationToken ct = default);
    }
}
