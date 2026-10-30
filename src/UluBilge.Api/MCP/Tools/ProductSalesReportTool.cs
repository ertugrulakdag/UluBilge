using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using UluBilge.Api.Models;
using UluBilge.App.Queries.ReportHandler;
using UluBilge.Core.Queries;

namespace UluBilge.Api.MCP.Tools
{
    [McpServerToolType]
    public static class ProductSalesReportTool
    {
        [McpServerTool(Name = "get_product_sales", ReadOnly = true, UseStructuredContent = true)]
        [Description("Northwind siparişlerinden ürün bazında satış raporu oluşturur ve indirim sonrası net satış tutarı en yüksek ürünleri listeler. Her ürün için ürün numarası ve adı, toplam satılan adet, sipariş sayısı ve net satış tutarı döndürülür. Kargo bedeli dahil değildir.")]
        public static async Task<BaseResponse<GetProductSalesAsync.Result>> GetProductSalesAsync(
            IAsyncQueryHandler<GetProductSalesAsync.Query, GetProductSalesAsync.Result> queryHandler,
            [Description("Sipariş tarihine göre filtrelenecek yıl (1753–9999). Gönderilmezse veya null ise tüm yıllar alınır.")] int? year = null,
            [Description("Döndürülecek en fazla ürün sayısı (1–100). Gönderilmezse veya null ise 10 kullanılır.")] int? topCount = null,
            CancellationToken ct = default)
        {
            try
            {
                var result = await queryHandler.HandleAsync(new GetProductSalesAsync.Query
                {
                    Year = year,
                    TopCount = topCount
                }, ct);

                return new BaseResponse<GetProductSalesAsync.Result>(result);
            }
            catch (ValidationException exception)
            {
                throw new McpException(exception.Message, exception);
            }
        }
    }
}
