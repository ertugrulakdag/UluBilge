using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using UluBilge.Data.Abstract;

namespace UluBilge.Data.Database;

public sealed class UnitOfWork(DatabaseConnectionSettings settings, ILogger<UnitOfWork> logger) : IUnitOfWork
{
    private DbConnection? _connection;
    private DbTransaction? _transaction;
    private int _operationActive;

    public DbConnection Connection => _connection ?? throw new InvalidOperationException("Veritabanı bağlantısına yalnızca etkin bir iş birimi işlemi içinden erişilebilir.");

    public DbTransaction? Transaction => _transaction;

    public Task<TResult> ExecuteQueryAsync<TResult>(Func<CancellationToken, Task<TResult>> action, CancellationToken cancellationToken = default) => ExecuteAsync(action, isCommand: false, cancellationToken);

    public Task<TResult> ExecuteCommandAsync<TResult>(Func<CancellationToken, Task<TResult>> action, CancellationToken cancellationToken = default) => ExecuteAsync(action, isCommand: true, cancellationToken);

    private async Task<TResult> ExecuteAsync<TResult>(Func<CancellationToken, Task<TResult>> action, bool isCommand, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (Interlocked.CompareExchange(ref _operationActive, 1, 0) != 0)
        {
            throw new InvalidOperationException("Aynı iş birimi üzerinde iç içe veya eş zamanlı işlemler çalıştırılamaz.");
        }

        try
        {
            await using var connection = new SqlConnection(isCommand ? settings.WriteConnection : settings.ReadConnection);
            await connection.OpenAsync(cancellationToken);
            _connection = connection;

            if (!isCommand)
            {
                return await action(cancellationToken);
            }

            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            _transaction = transaction;
            try
            {
                var result = await action(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return result;
            }
            catch
            {
                try
                {
                    await transaction.RollbackAsync(CancellationToken.None);
                }
                catch (Exception rollbackException)
                {
                    logger.LogError(rollbackException, "Veritabanı işlemi geri alınamadı.");
                }

                throw;
            }
            finally
            {
                _transaction = null;
            }
        }
        finally
        {
            _transaction = null;
            _connection = null;
            Volatile.Write(ref _operationActive, 0);
        }
    }
}
