using System.ComponentModel.DataAnnotations;
using UluBilge.Core.Commands;
using UluBilge.Core.Exceptions;
using UluBilge.Data.Abstract;

namespace UluBilge.App.Commands.RegionHandler;

public static class DeleteRegionAsync
{
    public sealed class Command : ICommand<Result>
    {
        [Range(1, int.MaxValue, ErrorMessage = "Bölge numarası sıfırdan büyük olmalıdır.")]
        public int RegionId { get; set; }
    }

    public sealed class Result
    {
        public bool Status { get; init; }
    }

    public sealed class HandlerAsync(IRegionRepository regionRepository) : IAsyncCommandHandler<Command, Result>
    {
        public async Task<Result> HandleAsync(Command command, CancellationToken ct = default)
        {
            var affectedRows = await regionRepository.DeleteAsync(command.RegionId, ct);
            if (affectedRows == 0)
            {
                throw new NotFoundException($"{command.RegionId} numaralı bölge bulunamadı.");
            }

            return new Result { Status = true };
        }
    }
}
