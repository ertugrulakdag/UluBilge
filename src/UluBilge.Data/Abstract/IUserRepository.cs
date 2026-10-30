using UluBilge.Core.Entities;

namespace UluBilge.Data.Abstract
{
    public interface IUserRepository
    {
        Task<User?> GetByNormalizedUserNameForUpdateAsync(string normalizedUserName, CancellationToken cancellationToken = default);
        Task RecordFailedLoginAsync(int userId, int accessFailedCount, DateTime? lockoutEndUtc, DateTime now, CancellationToken cancellationToken = default);
        Task RecordSuccessfulLoginAsync(User user, DateTime now, CancellationToken cancellationToken = default);
    }
}
