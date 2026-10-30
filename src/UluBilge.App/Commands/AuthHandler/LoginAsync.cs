using System.ComponentModel.DataAnnotations;
using UluBilge.Core.Commands;
using UluBilge.Data.Abstract;

namespace UluBilge.App.Commands.AuthHandler
{
    public static class LoginAsync
    {
        public sealed class Command : ICommand<Result>
        {
            [Required, StringLength(64)]
            public string UserName { get; init; } = string.Empty;
            [Required, StringLength(128)]
            public string Password { get; init; } = string.Empty;
        }

        public sealed class Result
        {
            public string? AccessToken { get; set; }
            public string? TokenType { get; set; }
            public int? ExpiresIn { get; set; }
            public DateTime? DateTimeOffset { get; set; }
            public DateTime? ExpiresAtUtc { get; set; }
        }

        public sealed class HandlerAsync(IUserRepository userRepository) : IAsyncCommandHandler<Command, Result>
        {
            public async Task<Result> HandleAsync(Command command, CancellationToken ct = default)
            {
                
                //TODO:
                return new Result { AccessToken = "test" };
            }
        }
    }
}
