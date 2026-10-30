using Dapper;
using UluBilge.Core.Entities;
using UluBilge.Data.Abstract;

namespace UluBilge.Data.Repository
{
    public sealed class UserRepository(IUnitOfWork unitOfWork) : IUserRepository
    {
        private const string SelectUser = @"
            SELECT Id, UserName, NormalizedUserName, PasswordHash, Email, EmailConfirmed,
                   AccessFailedCount, LockoutEndUtc, SecurityStamp, RoleEnum,
                   CreatedAtUtc, UpdatedAtUtc, LastLoginAtUtc, IsActive
            FROM dbo.Users WITH (UPDLOCK, ROWLOCK)";

        public Task<User?> GetByNormalizedUserNameForUpdateAsync(string normalizedUserName, CancellationToken cancellationToken = default) =>
            unitOfWork.Connection.QuerySingleOrDefaultAsync<User>(new CommandDefinition(
                SelectUser + " WHERE NormalizedUserName = @NormalizedUserName;",
                new { NormalizedUserName = normalizedUserName }, unitOfWork.Transaction, cancellationToken: cancellationToken));

        public Task<User?> GetByUserIdAsync(int userId, CancellationToken cancellationToken = default)
        {
            const string sql = @"
                SELECT Id, UserName, NormalizedUserName, PasswordHash, Email, EmailConfirmed,
                       AccessFailedCount, LockoutEndUtc, SecurityStamp, RoleEnum,
                       CreatedAtUtc, UpdatedAtUtc, LastLoginAtUtc, IsActive
                FROM dbo.Users
                WHERE Id = @UserId;";
            return unitOfWork.Connection.QuerySingleOrDefaultAsync<User>(new CommandDefinition(sql, new { UserId = userId }, unitOfWork.Transaction, cancellationToken: cancellationToken));
        }

        public async Task RecordFailedLoginAsync(int userId, int accessFailedCount, DateTime? lockoutEndUtc, DateTime now, CancellationToken cancellationToken = default)
        {
            const string sql = @"
                UPDATE dbo.Users
                SET AccessFailedCount = @AccessFailedCount, LockoutEndUtc = @LockoutEndUtc, UpdatedAtUtc = @Now
                WHERE Id = @UserId;";
            await unitOfWork.Connection.ExecuteAsync(new CommandDefinition(sql,
                new { UserId = userId, AccessFailedCount = accessFailedCount, LockoutEndUtc = lockoutEndUtc, Now = now },
                 unitOfWork.Transaction, cancellationToken: cancellationToken));
        }

        public async Task RecordSuccessfulLoginAsync(User user, DateTime now, CancellationToken cancellationToken = default)
        {
            const string sql = @"
                UPDATE dbo.Users
                SET AccessFailedCount = 0, LockoutEndUtc = NULL, PasswordHash = @PasswordHash,
                    LastLoginAtUtc = @Now, UpdatedAtUtc = @Now
                WHERE Id = @Id;";
            await unitOfWork.Connection.ExecuteAsync(new CommandDefinition(sql,
                new { user.Id, user.PasswordHash, Now = now }, unitOfWork.Transaction, cancellationToken: cancellationToken));
        }
    }
}
