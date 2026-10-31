using Dapper;
using UluBilge.Core.Entities;
using UluBilge.Data.Abstract;

namespace UluBilge.Data.Repository
{
    public sealed class OAuthClientRepository(IUnitOfWork unitOfWork) : IOAuthClientRepository
    {
        public async Task<OAuthClient?> GetByClientIdAsync(string clientId, CancellationToken ct = default)
        {
            const string sql = @"
                SELECT ClientId, DisplayName, IsActive, CreatedAtUtc
                FROM dbo.OAuthClients
                WHERE ClientId = @ClientId;

                SELECT RedirectUri
                FROM dbo.OAuthClientRedirectUris
                WHERE ClientId = @ClientId;";

            using var results = await unitOfWork.Connection.QueryMultipleAsync(new CommandDefinition(
                sql, new { ClientId = clientId }, unitOfWork.Transaction, cancellationToken: ct));
            var client = await results.ReadSingleOrDefaultAsync<OAuthClient>();
            var redirectUris = await results.ReadAsync<string>();

            // SQL Server, BIN2 karşılaştırmasında bile sondaki boşlukları eşit sayar.
            // OAuth istemci kimliği ise kayıtlı değerle birebir aynı olmalıdır.
            if (client is null || !string.Equals(client.ClientId, clientId, StringComparison.Ordinal))
                return null;

            client.RedirectUris = redirectUris.ToArray();
            return client;
        }

        public async Task<bool> TryInsertAsync(OAuthClient client, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(client);
            if (unitOfWork.Transaction is null)
                throw new InvalidOperationException("OAuth istemci kaydı etkin bir veritabanı transaction işlemi gerektirir.");

            const string insertClientSql = @"
                -- Kayıt sınırını farklı API instance'ları arasında da atomik uygular.
                -- Kilit, dönüş adresleri dahil tüm kayıt transaction'ı bitene kadar tutulur.
                DECLARE @ClientCount int;
                SELECT @ClientCount = COUNT(*) FROM dbo.OAuthClients WITH (TABLOCKX, HOLDLOCK) WHERE IsActive = 1;
                IF @ClientCount >= 1000 OR EXISTS (SELECT 1 FROM dbo.OAuthClients WHERE ClientId = @ClientId)
                BEGIN
                    SELECT CAST(0 AS bit);
                    RETURN;
                END;

                INSERT INTO dbo.OAuthClients (ClientId, DisplayName, IsActive, CreatedAtUtc)
                VALUES (@ClientId, @DisplayName, @IsActive, @CreatedAtUtc);
                SELECT CAST(1 AS bit);";

            var inserted = await unitOfWork.Connection.QuerySingleAsync<bool>(new CommandDefinition(
                insertClientSql, client, unitOfWork.Transaction, cancellationToken: ct));
            if (!inserted)
                return false;

            const string insertRedirectUriSql = @"
                -- RedirectUriHash üzerindeki indeksin ayarları INSERT ile aynı batch'te uygulanır.
                SET ANSI_NULLS ON;
                SET ANSI_PADDING ON;
                SET ANSI_WARNINGS ON;
                SET ARITHABORT ON;
                SET CONCAT_NULL_YIELDS_NULL ON;
                SET QUOTED_IDENTIFIER ON;
                SET NUMERIC_ROUNDABORT OFF;

                INSERT INTO dbo.OAuthClientRedirectUris (ClientId, RedirectUri)
                VALUES (@ClientId, @RedirectUri);";

            var redirectUris = client.RedirectUris.Distinct(StringComparer.Ordinal)
                .Select(redirectUri => new { client.ClientId, RedirectUri = redirectUri });
            await unitOfWork.Connection.ExecuteAsync(new CommandDefinition(
                insertRedirectUriSql, redirectUris, unitOfWork.Transaction, cancellationToken: ct));
            return true;
        }
    }
}
