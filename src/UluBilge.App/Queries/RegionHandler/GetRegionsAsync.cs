using System.ComponentModel.DataAnnotations;
using UluBilge.Core.Entities;
using UluBilge.Core.Models;
using UluBilge.Core.Queries;
using UluBilge.Data.Abstract;

namespace UluBilge.App.Queries.RegionHandler
{
    public static class GetRegionsAsync
    {
        public sealed class Query : IQuery<Result>
        {
            [Range(1, int.MaxValue, ErrorMessage = "Sayfa numarası en az 1 olmalıdır.")]
            public int PageNumber { get; set; } = 1;

            [Range(1, 100, ErrorMessage = "Sayfa boyutu 1 ile 100 arasında olmalıdır.")]
            public int PageSize { get; set; } = 20;
        }

        public sealed class Result : PagedResult<Region>
        {
        }

        public sealed class Handler(IRegionRepository regionRepository) : IAsyncQueryHandler<Query, Result>
        {
            public async Task<Result> HandleAsync(Query query, CancellationToken ct = default)
            {
                var regions = await regionRepository.GetAllPagingAsync(query.PageNumber, query.PageSize, ct);

                return new Result
                {
                    Data = regions.Data,
                    Total = regions.Total,
                    PageNumber = regions.PageNumber,
                    PageSize = regions.PageSize
                };
            }
        }
    }
}
