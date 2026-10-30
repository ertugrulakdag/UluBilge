using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using UluBilge.McpServer.Clients;
using UluBilge.McpServer.Contracts;

namespace UluBilge.McpServer.Tools;

[McpServerToolType]
public sealed class RegionTools(UluBilgeApiClient api)
{
    [McpServerTool(Name = "get_regions", Title = "Bölgeleri listele", ReadOnly = true,
        UseStructuredContent = true, OutputSchemaType = typeof(RegionListResult))]
    [Description("UluBilge API'deki bölgeleri sayfalı olarak listeler. Yapılandırılmış API kullanıcısının Admin yetkisi gerekir.")]
    public async Task<CallToolResult> GetRegionsAsync(
        [Description("Sayfa numarası; en az 1."), Range(1, int.MaxValue)] int pageNumber = 1,
        [Description("Sayfa boyutu; 1 ile 100 arasında."), Range(1, 100)] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await api.GetRegionsAsync(pageNumber, pageSize, cancellationToken);
            var text = new StringBuilder()
                .AppendLine($"Toplam kayıt: {result.Total} | Sayfa: {result.PageNumber} | Sayfa boyutu: {result.PageSize}");
            if (result.Items.Count == 0)
            {
                text.AppendLine("Bu sayfada bölge kaydı bulunamadı.");
            }
            else
            {
                text.AppendLine().AppendLine("| Bölge numarası | Açıklama |").AppendLine("| --- | --- |");
                foreach (var region in result.Items)
                {
                    var description = region.RegionDescription.Replace("\\", "\\\\").Replace("|", "\\|")
                        .Replace("\r", " ").Replace("\n", " ");
                    text.AppendLine($"| {region.RegionId} | {description} |");
                }
            }

            return new CallToolResult
            {
                Content = [new TextContentBlock { Text = text.ToString() }],
                StructuredContent = JsonSerializer.SerializeToElement(result, ApiHttpClient.JsonOptions)
            };
        }
        catch (ApiException exception)
        {
            return new CallToolResult
            {
                IsError = true,
                Content = [new TextContentBlock { Text = exception.Message }]
            };
        }
    }
}
