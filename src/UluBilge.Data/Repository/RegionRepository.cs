using Dapper;
using UluBilge.Core.Entities;
using UluBilge.Core.Models;
using UluBilge.Data.Abstract;

namespace UluBilge.Data.Repository
{
    public sealed class RegionRepository(IUnitOfWork unitOfWork) : IRegionRepository
    {
        public Task<Region?> GetByIdAsync(int regionId, CancellationToken ct = default)
        {
            const string sql = @"
            SELECT RegionID AS RegionId, RTRIM(RegionDescription) AS RegionDescription
            FROM dbo.Region
            WHERE RegionID = @RegionId;";

            return unitOfWork.Connection.QuerySingleOrDefaultAsync<Region>(new CommandDefinition(sql, new { RegionId = regionId }, unitOfWork.Transaction, cancellationToken: ct));
        }

        public async Task<PagedResult<Region>> GetAllPagingAsync(int pageNumber, int pageSize, CancellationToken ct = default)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(pageNumber, 1);
            ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);

            const string sql = @"
            SELECT COUNT(*) FROM dbo.Region;
            SELECT RegionID AS RegionId, RTRIM(RegionDescription) AS RegionDescription
            FROM dbo.Region
            ORDER BY RegionID
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;";

            var parameters = new { Offset = ((long)pageNumber - 1) * pageSize, PageSize = pageSize };
            await using var reader = await unitOfWork.Connection.QueryMultipleAsync(
                new CommandDefinition(sql, parameters, unitOfWork.Transaction, cancellationToken: ct));

            var total = await reader.ReadSingleAsync<int>();
            var data = (await reader.ReadAsync<Region>()).AsList();
            return new PagedResult<Region>
            {
                Data = data,
                Total = total,
                PageNumber = pageNumber,
                PageSize = pageSize
            };
        }

        public Task<int> InsertAsync(Region region, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(region);
            const string sql = "INSERT INTO dbo.Region (RegionID, RegionDescription) VALUES (@RegionId, @RegionDescription);";
            return ExecuteWriteAsync(sql, new { region.RegionId, region.RegionDescription }, ct);
        }

        public Task<int> UpdateAsync(Region region, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(region);
            const string sql = @"
            UPDATE dbo.Region
            SET RegionDescription = @RegionDescription
            WHERE RegionID = @RegionId;";

            return ExecuteWriteAsync(sql, new { region.RegionId, region.RegionDescription }, ct);
        }

        public Task<int> DeleteAsync(int regionId, CancellationToken ct = default) => ExecuteWriteAsync("DELETE FROM dbo.Region WHERE RegionID = @RegionId;", new { RegionId = regionId }, ct);

        private Task<int> ExecuteWriteAsync(string sql, object parameters, CancellationToken ct)
        {
            var transaction = unitOfWork.Transaction ?? throw new InvalidOperationException("Veri yazmak için etkin bir komut işlemi bulunmalıdır.");

            return unitOfWork.Connection.ExecuteAsync(new CommandDefinition(sql, parameters, transaction, cancellationToken: ct));
        }

    }
}
