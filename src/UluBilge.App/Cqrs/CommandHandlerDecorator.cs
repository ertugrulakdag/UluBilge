using System.ComponentModel.DataAnnotations;
using UluBilge.Core.Commands;
using UluBilge.Data.Abstract;

namespace UluBilge.App.Cqrs
{
    public sealed class CommandHandlerDecorator<TCommand, TResult>(IAsyncCommandHandler<TCommand, TResult> handler, IUnitOfWork unitOfWork) : IAsyncCommandHandler<TCommand, TResult> where TCommand : class, ICommand<TResult>
    {
        public Task<TResult> HandleAsync(TCommand command, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(command);
            Validator.ValidateObject(command, new ValidationContext(command), validateAllProperties: true);

            return unitOfWork.ExecuteCommandAsync(token => handler.HandleAsync(command, token), ct);
        }
    }
}
