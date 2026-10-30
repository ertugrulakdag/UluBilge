using UluBilge.Core.Entities;

namespace UluBilge.Data.Abstract
{
    public interface IUserTokenRepository
    {
        Task<UserToken?> GetByTokenHashForUpdateAsync(byte[] tokenHash, CancellationToken ct = default);
        Task<long> InsertAsync(UserToken token, CancellationToken ct = default);
        Task ConsumeAsync(long tokenId, long? replacedByTokenId, DateTime now, CancellationToken ct = default);
        Task RevokeFamilyAsync(Guid familyId, DateTime now, CancellationToken ct = default);
    }
}
