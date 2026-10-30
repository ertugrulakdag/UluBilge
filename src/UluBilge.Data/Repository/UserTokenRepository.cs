using Dapper;
using System.Data.Common;
using UluBilge.Core.Entities;
using UluBilge.Data.Abstract;

namespace UluBilge.Data.Repository
{
    public sealed class UserTokenRepository(IUnitOfWork unitOfWork) : IUserTokenRepository
    {
        public Task<UserToken?> GetByTokenHashForUpdateAsync(byte[] tokenHash, CancellationToken ct = default)
        {
            // Kullanici kilidi once alinir ve transaction bitene kadar tutulur.
            // Ayni ailedeki farkli tokenlarla gelen yenileme/iptal istekleri de siralanir.
            const string sql = @"
                DECLARE @UserId int = (SELECT UserId FROM dbo.UserTokens WHERE TokenHash = @TokenHash);
                SELECT @UserId = Id FROM dbo.Users WITH (UPDLOCK, ROWLOCK) WHERE Id = @UserId;

                SELECT Id, UserId, TokenHash, FamilyId, CreatedAtUtc, ExpiresAtUtc,
                       ConsumedAtUtc, RevokedAtUtc, ReplacedByTokenId
                FROM dbo.UserTokens WITH (UPDLOCK, ROWLOCK)
                WHERE TokenHash = @TokenHash;";

            return unitOfWork.Connection.QuerySingleOrDefaultAsync<UserToken>(new CommandDefinition(
                sql, new { TokenHash = tokenHash }, unitOfWork.Transaction, cancellationToken: ct));
        }

        public Task<long> InsertAsync(UserToken token, CancellationToken ct = default)
        {
            const string sql = @"
                INSERT INTO dbo.UserTokens (UserId, TokenHash, FamilyId, CreatedAtUtc, ExpiresAtUtc)
                OUTPUT INSERTED.Id
                VALUES (@UserId, @TokenHash, @FamilyId, @CreatedAtUtc, @ExpiresAtUtc);";

            return unitOfWork.Connection.QuerySingleAsync<long>(new CommandDefinition(sql, token, unitOfWork.Transaction, cancellationToken: ct));
        }

        public async Task ConsumeAsync(long tokenId, long replacedByTokenId, DateTime now, CancellationToken ct = default)
        {
            const string sql = @"
                UPDATE dbo.UserTokens
                SET ConsumedAtUtc = @Now, ReplacedByTokenId = @ReplacedByTokenId
                WHERE Id = @TokenId AND ConsumedAtUtc IS NULL AND RevokedAtUtc IS NULL AND ExpiresAtUtc > @Now;";

            var affected = await unitOfWork.Connection.ExecuteAsync(new CommandDefinition(sql,
                new { TokenId = tokenId, ReplacedByTokenId = replacedByTokenId, Now = now }, unitOfWork.Transaction, cancellationToken: ct));
            if (affected != 1)
                throw new InvalidOperationException("Refresh token tuketilemedi.");
        }

        public async Task RevokeFamilyAsync(Guid familyId, DateTime now, CancellationToken ct = default)
        {
            const string sql = @"
                UPDATE dbo.UserTokens
                SET RevokedAtUtc = @Now
                WHERE FamilyId = @FamilyId AND RevokedAtUtc IS NULL;";

            await unitOfWork.Connection.ExecuteAsync(new CommandDefinition(sql,
                new { FamilyId = familyId, Now = now }, unitOfWork.Transaction, cancellationToken: ct));
        }
    }
}
