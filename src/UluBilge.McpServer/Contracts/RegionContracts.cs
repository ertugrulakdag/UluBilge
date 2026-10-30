namespace UluBilge.McpServer.Contracts;

public sealed record RegionDto(int RegionId, string RegionDescription);
public sealed record RegionListResult(IReadOnlyList<RegionDto> Items, int Total, int PageNumber, int PageSize);

internal sealed record ApiResponse<T>(T Data);
internal sealed record RegionPage(IReadOnlyList<RegionDto> Data, int Total, int PageNumber, int PageSize);
