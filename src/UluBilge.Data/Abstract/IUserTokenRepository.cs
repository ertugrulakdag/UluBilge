using UluBilge.Core.Entities;
using UluBilge.Core.Enums;

namespace UluBilge.Data.Abstract
{
    public interface IUserTokenRepository
    {
        Task<UserToken?> GetByHashAsync(byte[] tokenHash, CancellationToken cancellationToken = default);
        Task<long> InsertAsync(UserToken token, CancellationToken cancellationToken = default);
        Task<bool> TryConsumeAsync(long tokenId, long replacementId, DateTime now, CancellationToken cancellationToken = default);
        Task RevokeFamilyAsync(int userId, Guid familyId, DateTime now, CancellationToken cancellationToken = default);
        Task<bool> IsSessionValidAsync(int userId, Guid securityStamp, RoleEnum roleEnum, Guid familyId, DateTime now, CancellationToken cancellationToken = default);
    }
}
