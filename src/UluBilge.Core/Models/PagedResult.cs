namespace UluBilge.Core.Models
{
    public class PagedResult<T>
    {
        public IReadOnlyList<T> Data { get; init; } = [];
        public int Total { get; init; }
        public int PageNumber { get; init; }
        public int PageSize { get; init; }
    }
}
