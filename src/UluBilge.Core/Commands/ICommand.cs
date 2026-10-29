namespace UluBilge.Core.Commands
{
    public interface ICommand<TResult>
    {
    }

    public interface IAsyncCommandHandler<in TCommand, TResult>
        where TCommand : ICommand<TResult>
    {
        Task<TResult> HandleAsync(TCommand command, CancellationToken ct = default);
    }
}
