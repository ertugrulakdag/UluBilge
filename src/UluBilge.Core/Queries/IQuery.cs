namespace UluBilge.Core.Queries;

public interface IQuery<TResult>
{
}

public interface IAsyncQueryHandler<in TQuery, TResult>
    where TQuery : IQuery<TResult>
{
    Task<TResult> HandleAsync(TQuery query, CancellationToken ct = default);
}
