using UluBilge.Core.Entities;
using UluBilge.Core.Models;

namespace UluBilge.Data.Abstract
{
    public interface IRegionRepository
    {
        Task<Region?> GetByIdAsync(int regionId, CancellationToken ct = default);
        Task<PagedResult<Region>> GetAllPagingAsync(int pageNumber, int pageSize, CancellationToken ct = default);
        Task<int> InsertAsync(Region region, CancellationToken ct = default);
        Task<int> UpdateAsync(Region region, CancellationToken ct = default);
        Task<int> DeleteAsync(int regionId, CancellationToken ct = default);
    }
}
