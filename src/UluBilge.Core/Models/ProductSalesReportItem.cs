namespace UluBilge.Core.Models
{
    public sealed class ProductSalesReportItem
    {
        public int ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public long TotalQuantity { get; set; }
        public int OrderCount { get; set; }
        public decimal NetSales { get; set; }
    }
}
