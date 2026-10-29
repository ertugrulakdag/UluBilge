using System.ComponentModel.DataAnnotations;
using UluBilge.Core.Queries;
using UluBilge.Data.Abstract;

namespace UluBilge.App.Cqrs;

internal sealed class QueryHandlerDecorator<TQuery, TResult>(IAsyncQueryHandler<TQuery, TResult> handler, IUnitOfWork unitOfWork) : IAsyncQueryHandler<TQuery, TResult> where TQuery : class, IQuery<TResult>
{
    public Task<TResult> HandleAsync(TQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        Validator.ValidateObject(query, new ValidationContext(query), validateAllProperties: true);

        return unitOfWork.ExecuteQueryAsync(token => handler.HandleAsync(query, token), ct);
    }
}
