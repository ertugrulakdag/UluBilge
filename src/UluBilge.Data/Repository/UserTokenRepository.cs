using Dapper;
using UluBilge.Core.Entities;
using UluBilge.Core.Enums;
using UluBilge.Data.Abstract;

namespace UluBilge.Data.Repository
{
    public sealed class UserTokenRepository(IUnitOfWork unitOfWork) : IUserTokenRepository
    {
        public Task<UserToken?> GetByHashAsync(byte[] tokenHash, CancellationToken cancellationToken = default)
        {
            const string sql = """
                SELECT Id, UserId, TokenHash, FamilyId, CreatedAtUtc, ExpiresAtUtc,
                       ConsumedAtUtc, RevokedAtUtc, ReplacedByTokenId
                FROM dbo.UserTokens
                WHERE TokenHash = @TokenHash;
                """;
            return unitOfWork.Connection.QuerySingleOrDefaultAsync<UserToken>(new CommandDefinition(
                sql, new { TokenHash = tokenHash }, RequiredTransaction(), cancellationToken: cancellationToken));
        }

        public Task<long> InsertAsync(UserToken token, CancellationToken cancellationToken = default)
        {
            const string sql = """
                INSERT INTO dbo.UserTokens (UserId, TokenHash, FamilyId, CreatedAtUtc, ExpiresAtUtc)
                OUTPUT INSERTED.Id
                VALUES (@UserId, @TokenHash, @FamilyId, @CreatedAtUtc, @ExpiresAtUtc);
                """;
            return unitOfWork.Connection.QuerySingleAsync<long>(new CommandDefinition(
                sql, token, RequiredTransaction(), cancellationToken: cancellationToken));
        }

        public async Task<bool> TryConsumeAsync(long tokenId, long replacementId, DateTime now, CancellationToken cancellationToken = default)
        {
            const string sql = """
                UPDATE dbo.UserTokens
                SET ConsumedAtUtc = @Now, ReplacedByTokenId = @ReplacementId
                WHERE Id = @TokenId AND ConsumedAtUtc IS NULL AND RevokedAtUtc IS NULL AND ExpiresAtUtc > @Now;
                """;
            return await unitOfWork.Connection.ExecuteAsync(new CommandDefinition(sql,
                new { TokenId = tokenId, ReplacementId = replacementId, Now = now }, RequiredTransaction(), cancellationToken: cancellationToken)) == 1;
        }

        public async Task RevokeFamilyAsync(int userId, Guid familyId, DateTime now, CancellationToken cancellationToken = default)
        {
            const string sql = """
                UPDATE dbo.UserTokens
                SET RevokedAtUtc = @Now
                WHERE UserId = @UserId AND FamilyId = @FamilyId AND RevokedAtUtc IS NULL;
                """;
            await unitOfWork.Connection.ExecuteAsync(new CommandDefinition(sql,
                new { UserId = userId, FamilyId = familyId, Now = now }, RequiredTransaction(), cancellationToken: cancellationToken));
        }

        public Task<bool> IsSessionValidAsync(int userId, Guid securityStamp, RoleEnum roleEnum, Guid familyId, DateTime now, CancellationToken cancellationToken = default)
        {
            const string sql = """
                SELECT CAST(CASE WHEN EXISTS
                (
                    SELECT 1
                    FROM dbo.Users AS u
                    INNER JOIN dbo.UserTokens AS t ON t.UserId = u.Id
                    WHERE u.Id = @UserId AND u.IsActive = 1 AND u.SecurityStamp = @SecurityStamp
                      AND u.RoleEnum = @RoleEnum AND t.FamilyId = @FamilyId
                      AND t.ConsumedAtUtc IS NULL AND t.RevokedAtUtc IS NULL AND t.ExpiresAtUtc > @Now
                ) THEN 1 ELSE 0 END AS bit);
                """;
            return unitOfWork.Connection.ExecuteScalarAsync<bool>(new CommandDefinition(sql,
                new { UserId = userId, SecurityStamp = securityStamp, RoleEnum = (long)roleEnum, FamilyId = familyId, Now = now },
                RequiredTransaction(), cancellationToken: cancellationToken));
        }

        private System.Data.Common.DbTransaction RequiredTransaction() =>
            unitOfWork.Transaction ?? throw new InvalidOperationException("Oturum işlemleri için etkin bir komut işlemi bulunmalıdır.");
    }
}
