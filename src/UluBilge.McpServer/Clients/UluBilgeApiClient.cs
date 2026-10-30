using System.Net;
using System.Net.Http.Headers;
using UluBilge.McpServer.Authentication;
using UluBilge.McpServer.Contracts;

namespace UluBilge.McpServer.Clients;

public sealed class UluBilgeApiClient(ApiHttpClient http, ApiTokenProvider tokens)
{
    public async Task<RegionListResult> GetRegionsAsync(int pageNumber = 1, int pageSize = 20, CancellationToken cancellationToken = default)
    {
        if (pageNumber < 1 || pageSize is < 1 or > 100)
        {
            throw new ApiException("Sayfa numarası en az 1, sayfa boyutu 1 ile 100 arasında olmalıdır.", HttpStatusCode.BadRequest);
        }

        var token = await tokens.GetAccessTokenAsync(cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"v1/Region/GetRegions?pageNumber={pageNumber}&pageSize={pageSize}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        ApiResponse<RegionPage> response;
        try
        {
            response = await http.SendAsync<ApiResponse<RegionPage>>(request, cancellationToken);
        }
        catch (ApiException exception) when (exception.StatusCode == HttpStatusCode.Unauthorized)
        {
            tokens.Invalidate();
            throw;
        }

        var page = response.Data;
        if (page is null || page.Data is null || page.Total < page.Data.Count || page.PageNumber != pageNumber || page.PageSize != pageSize
            || page.Data.Count > pageSize
            || page.Data.Any(region => region is null || region.RegionId <= 0 || string.IsNullOrWhiteSpace(region.RegionDescription)))
        {
            throw new ApiException("API bölge listesi beklenen sayfalama veya kayıt sözleşmesini karşılamıyor.");
        }

        return new RegionListResult(page.Data, page.Total, page.PageNumber, page.PageSize);
    }
}
