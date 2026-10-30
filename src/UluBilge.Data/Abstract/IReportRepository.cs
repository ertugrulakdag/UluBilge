using UluBilge.Core.Models;

namespace UluBilge.Data.Abstract
{
    public interface IReportRepository
    {
        Task<IReadOnlyList<ProductSalesReportItem>> GetProductSalesAsync(int? year, int topCount, CancellationToken cancellationToken = default);
    }
}
