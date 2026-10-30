using System.Data.Common;

namespace UluBilge.Data.Abstract
{
    public interface IUnitOfWork
    {
        DbConnection Connection { get; }
        DbTransaction? Transaction { get; }

        Task<TResult> ExecuteQueryAsync<TResult>(Func<CancellationToken, Task<TResult>> action, CancellationToken ct = default);

        Task<TResult> ExecuteCommandAsync<TResult>(Func<CancellationToken, Task<TResult>> action, CancellationToken ct = default);
    }
}
