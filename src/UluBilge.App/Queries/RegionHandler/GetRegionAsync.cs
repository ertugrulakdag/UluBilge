using System.ComponentModel.DataAnnotations;
using UluBilge.Core.Entities;
using UluBilge.Core.Exceptions;
using UluBilge.Core.Queries;
using UluBilge.Data.Abstract;

namespace UluBilge.App.Queries.RegionHandler
{
    public static class GetRegionAsync
    {
        public sealed class Query : IQuery<Result>
        {
            [Range(1, int.MaxValue, ErrorMessage = "Bölge numarası sıfırdan büyük olmalıdır.")]
            public int RegionId { get; set; }
        }

        public sealed class Result
        {
            public required Region Item { get; init; }
        }

        public sealed class Handler(IRegionRepository regionRepository) : IAsyncQueryHandler<Query, Result>
        {
            public async Task<Result> HandleAsync(Query query, CancellationToken ct = default)
            {
                var region = await regionRepository.GetByIdAsync(query.RegionId, ct)
                    ?? throw new NotFoundException($"{query.RegionId} numaralı bölge bulunamadı.");

                return new Result { Item = region };
            }
        }
    }
}
