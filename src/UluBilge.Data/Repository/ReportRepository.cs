using Dapper;
using UluBilge.Core.Models;
using UluBilge.Data.Abstract;

namespace UluBilge.Data.Repository
{
    public sealed class ReportRepository(IUnitOfWork unitOfWork) : IReportRepository
    {
        public async Task<IReadOnlyList<ProductSalesReportItem>> GetProductSalesAsync(int? year, int topCount, CancellationToken cancellationToken = default)
        {
            if (year.HasValue)
            {
                ArgumentOutOfRangeException.ThrowIfLessThan(year.Value, 1753);
                ArgumentOutOfRangeException.ThrowIfGreaterThan(year.Value, 9999);
            }
            ArgumentOutOfRangeException.ThrowIfLessThan(topCount, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(topCount, 100);

            const string sql = @"
            SELECT TOP (@TopCount)
                p.ProductID AS ProductId,
                p.ProductName,
                SUM(CAST(od.Quantity AS bigint)) AS TotalQuantity,
                COUNT(*) AS OrderCount,
                CAST(SUM(CAST(od.UnitPrice AS decimal(19, 4)) * od.Quantity
                    * (1 - CAST(od.Discount AS decimal(9, 6)))) AS decimal(19, 2)) AS NetSales
            FROM dbo.[Order Details] od
            INNER JOIN dbo.Orders o ON o.OrderID = od.OrderID
            INNER JOIN dbo.Products p ON p.ProductID = od.ProductID
            WHERE (@StartDate IS NULL OR o.OrderDate >= @StartDate)
                AND (@EndDate IS NULL OR o.OrderDate < @EndDate)
            GROUP BY p.ProductID, p.ProductName
            ORDER BY NetSales DESC, p.ProductID;";

            var parameters = new
            {
                TopCount = topCount,
                StartDate = year.HasValue ? new DateTime(year.Value, 1, 1) : (DateTime?)null,
                EndDate = year.HasValue && year.Value < 9999 ? new DateTime(year.Value + 1, 1, 1) : (DateTime?)null
            };

            var data = await unitOfWork.Connection.QueryAsync<ProductSalesReportItem>(
                new CommandDefinition(sql, parameters, unitOfWork.Transaction, cancellationToken: cancellationToken));
            return data.AsList();
        }
    }
}
