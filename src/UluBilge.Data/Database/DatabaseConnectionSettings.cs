using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace UluBilge.Data.Database;

public sealed class DatabaseConnectionSettings
{
    public string ReadConnection { get; }
    public string WriteConnection { get; }

    public DatabaseConnectionSettings(IConfiguration configuration)
    {
        ReadConnection = GetValidatedConnectionString(configuration, nameof(ReadConnection));
        WriteConnection = GetValidatedConnectionString(configuration, nameof(WriteConnection));
    }

    private static string GetValidatedConnectionString(IConfiguration configuration, string name)
    {
        var connectionString = configuration.GetConnectionString(name);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException($"ConnectionStrings:{name} bağlantı bilgisi zorunludur.");
        }

        try
        {
            var builder = new SqlConnectionStringBuilder(connectionString);
            if (string.IsNullOrWhiteSpace(builder.DataSource) || string.IsNullOrWhiteSpace(builder.InitialCatalog))
            {
                throw new InvalidOperationException($"ConnectionStrings:{name} bağlantı bilgisinde sunucu ve veritabanı belirtilmelidir.");
            }
        }
        catch (ArgumentException)
        {
            // Bağlantı bilgisini ve ayrıştırma ayrıntılarını hata mesajına eklemeyin.
            throw new InvalidOperationException($"ConnectionStrings:{name} geçerli bir SQL Server bağlantı bilgisi değildir.");
        }

        return connectionString;
    }
}
