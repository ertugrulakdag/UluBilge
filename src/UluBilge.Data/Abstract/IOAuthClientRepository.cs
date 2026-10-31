using UluBilge.Core.Entities;

namespace UluBilge.Data.Abstract
{
    public interface IOAuthClientRepository
    {
        Task<OAuthClient?> GetByClientIdAsync(string clientId, CancellationToken ct = default);
        Task<bool> TryInsertAsync(OAuthClient client, CancellationToken ct = default);
    }
}
