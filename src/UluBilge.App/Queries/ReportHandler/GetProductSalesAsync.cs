using System.ComponentModel.DataAnnotations;
using UluBilge.Core.Models;
using UluBilge.Core.Queries;
using UluBilge.Data.Abstract;

namespace UluBilge.App.Queries.ReportHandler
{
    public static class GetProductSalesAsync
    {
        public sealed class Query : IQuery<Result>
        {
            [Range(1753, 9999, ErrorMessage = "Yıl 1753 ile 9999 arasında olmalıdır...")]
            public int? Year { get; set; }

            [Range(1, 100, ErrorMessage = "Ürün sayısı 1 ile 100 arasında olmalıdır.")]
            public int? TopCount { get; set; }
        }

        public sealed class Result
        {
            public IReadOnlyList<ProductSalesReportItem> Data { get; init; } = [];
        }

        public sealed class Handler(IReportRepository reportRepository) : IAsyncQueryHandler<Query, Result>
        {
            public async Task<Result> HandleAsync(Query query, CancellationToken ct = default)
            {
                var data = await reportRepository.GetProductSalesAsync(query.Year, query.TopCount ?? 10, ct);
                return new Result { Data = data };
            }
        }
    }
}
