using System.ComponentModel.DataAnnotations;
using UluBilge.Core.Commands;
using UluBilge.Core.Entities;
using UluBilge.Data.Abstract;

namespace UluBilge.App.Commands.RegionHandler
{
    public static class AddRegionAsync
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
            public int RegionId { get; init; }
        }

        public sealed class Handler(IRegionRepository regionRepository) : IAsyncCommandHandler<Command, Result>
        {
            public async Task<Result> HandleAsync(Command command, CancellationToken ct = default)
            {
                var region = new Region
                {
                    RegionId = command.RegionId,
                    RegionDescription = command.RegionDescription.Trim()
                };

                var affectedRows = await regionRepository.InsertAsync(region, ct);
                if (affectedRows <= 0)
                {
                    throw new InvalidOperationException("Bölge oluşturulamadı.");
                }

                return new Result { Status = true, RegionId = region.RegionId };
            }
        }
    }
}
