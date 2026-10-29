using UluBilge.Core.Entities;
using UluBilge.Core.Models;

namespace UluBilge.Data.Abstract;

public interface IRegionRepository
{
    Task<Region?> GetByIdAsync(int regionId, CancellationToken cancellationToken = default);
    Task<PagedResult<Region>> GetAllPagingAsync(int pageNumber, int pageSize, CancellationToken cancellationToken = default);
    Task<int> InsertAsync(Region region, CancellationToken cancellationToken = default);
    Task<int> UpdateAsync(Region region, CancellationToken cancellationToken = default);
    Task<int> DeleteAsync(int regionId, CancellationToken cancellationToken = default);
}
