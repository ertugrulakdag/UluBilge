using System.ComponentModel.DataAnnotations;
using UluBilge.Core.Models;
using UluBilge.Core.Queries;
using UluBilge.Data.Abstract;

namespace UluBilge.App.Queries.ReportHandler
{
    public static class GetProductSalesAsync
    {
        public sealed class Query : IQuery<Result>, IValidatableObject
        {
            [Range(1753, 9999, ErrorMessage = "Yıl 1753 ile 9999 arasında olmalıdır...")]
            public int? Year { get; set; }

            [Range(1, 100, ErrorMessage = "Ürün sayısı 1 ile 100 arasında olmalıdır.")]
            public int? TopCount { get; set; }

            public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
            {
                if (Year is int year && year > DateTime.UtcNow.Year)
                {
                    yield return new ValidationResult("Yıl içinde bulunduğumuz yıldan büyük olamaz.", [nameof(Year)]);
                }

                if (Year is null && TopCount is > 50)
                {
                    yield return new ValidationResult("Yıl belirtilmediğinde ürün sayısı en fazla 50 olabilir.", [nameof(TopCount)]);
                }
            }
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
