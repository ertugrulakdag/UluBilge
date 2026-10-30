using System.ComponentModel.DataAnnotations;
using UluBilge.Core.Commands;
using UluBilge.Core.Entities;
using UluBilge.Core.Exceptions;
using UluBilge.Data.Abstract;

namespace UluBilge.App.Commands.RegionHandler
{
    public static class UpdateRegionAsync
    {
        public sealed class Command : ICommand<Result>
        {
            [Range(1, int.MaxValue, ErrorMessage = "Bölge numarası sıfırdan büyük olmalıdır.")]
            public int RegionId { get; set; }

            [Required(ErrorMessage = "Bölge açıklaması zorunludur.")]
            [StringLength(50, ErrorMessage = "Bölge açıklaması en fazla 50 karakter olabilir.")]
            public string RegionDescription { get; set; } = string.Empty;
        }

        public sealed class Result
        {
            public bool Status { get; init; }
        }

        public sealed class Handler(IRegionRepository regionRepository) : IAsyncCommandHandler<Command, Result>
        {
            public async Task<Result> HandleAsync(Command command, CancellationToken ct = default)
            {
                var affectedRows = await regionRepository.UpdateAsync(new Region
                {
                    RegionId = command.RegionId,
                    RegionDescription = command.RegionDescription.Trim()
                }, ct);

                if (affectedRows == 0)
                {
                    throw new NotFoundException($"{command.RegionId} numaralı bölge bulunamadı.");
                }

                return new Result { Status = true };
            }
        }
    }
}
