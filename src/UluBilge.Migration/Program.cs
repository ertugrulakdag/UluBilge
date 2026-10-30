using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace UluBilge.Migration
{
    internal static class Program
    {
        private static async Task<int> Main()
        {
            int exitCode;
            try
            {
                exitCode = await RunAsync();
            }
            catch (Exception ex)
            {
                WriteLine("Beklenmeyen bir hata oluştu.", ConsoleColor.Red);
                WriteLine($"Neden: {ex.Message}", ConsoleColor.Red);
                exitCode = 1;
            }

            WaitForUserAcknowledgement();
            return exitCode;
        }

        private static void WaitForUserAcknowledgement()
        {
            Console.WriteLine();
            WriteLine("Kapatmak için bir tuşa basın...", ConsoleColor.DarkGray);
            try
            {
                Console.ReadKey(intercept: true);
            }
            catch (InvalidOperationException ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        private static async Task<int> RunAsync()
        {
            var baseDir = AppContext.BaseDirectory;

            var configuration = new ConfigurationBuilder()
                .SetBasePath(baseDir)
                .AddJsonFile("appsettings.json", optional: false)
                .Build();

            var projectName = configuration["ProjectName"] ?? "Migration";
            var connectionString = configuration.GetConnectionString("WriteConnection");
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                WriteLine("Yapılandırmada 'ConnectionStrings:WriteConnection' bulunamadı.", ConsoleColor.Red);
                return 1;
            }

            var connectionInfo = new SqlConnectionStringBuilder(connectionString);

            Console.WriteLine();
            PrintBanner($"ÇALIŞILAN PROJE: {projectName}", ConsoleColor.Green);
            Console.WriteLine();
            WriteLine($"Proje       : {projectName}", ConsoleColor.Cyan);
            WriteLine($"Sunucu      : {connectionInfo.DataSource}", ConsoleColor.Cyan);
            WriteLine($"Veritabanı  : {connectionInfo.InitialCatalog}", ConsoleColor.Cyan);
            WriteLine($"İşleme Başlanıyor...", ConsoleColor.Green);
            Console.WriteLine();

            var migrationsDir = Path.Combine(baseDir, "Migrations");
            if (!Directory.Exists(migrationsDir))
            {
                WriteLine($"Veritabanı geçiş dosyalarının bulunduğu klasör yok: {migrationsDir}", ConsoleColor.Red);
                return 1;
            }

            var files = Directory.GetFiles(migrationsDir, "*.sql")
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (files.Count == 0)
            {
                WriteLine("Çalıştırılacak veritabanı geçiş dosyası bulunamadı.", ConsoleColor.Yellow);
                return 0;
            }

            var productVersion = typeof(Program).Assembly.GetName().Version?.ToString() ?? "1.0.0.0";

            await using var connection = new SqlConnection(connectionString);
            try
            {
                await connection.OpenAsync();
            }
            catch (Exception ex)
            {
                WriteLine("Veritabanına bağlanılamadı.", ConsoleColor.Red);
                WriteLine($"Neden: {ex.Message}", ConsoleColor.Red);
                return 1;
            }

            try
            {
                await EnsureHistoryTableSchemaAsync(connection);
            }
            catch (Exception ex)
            {
                WriteLine("Veritabanı geçiş geçmişi tablosu hazırlanamadı.", ConsoleColor.Red);
                WriteLine($"Neden: {ex.Message}", ConsoleColor.Red);
                return 1;
            }

            HashSet<string> applied;
            try
            {
                applied = await GetAppliedMigrationsAsync(connection);
            }
            catch (Exception ex)
            {
                WriteLine("Veritabanı geçiş geçmişi okunamadı.", ConsoleColor.Red);
                WriteLine($"Neden: {ex.Message}", ConsoleColor.Red);
                return 1;
            }

            var appliedCount = 0;
            var skippedCount = 0;
            var totalStopwatch = Stopwatch.StartNew();

            foreach (var file in files)
            {
                var migrationId = Path.GetFileNameWithoutExtension(file);

                if (applied.Contains(migrationId))
                {
                    WriteLine($"[ATLANDI]  {migrationId}  (zaten uygulanmış)", ConsoleColor.Yellow);
                    skippedCount++;
                    continue;
                }

                var sql = await File.ReadAllTextAsync(file);
                var batches = SplitBatches(sql);

                if (batches.Count == 0)
                {
                    WriteLine($"[UYARI]    {migrationId}  içinde çalıştırılacak SQL bulunamadı, atlanıyor.", ConsoleColor.Yellow);
                    continue;
                }

                var stopwatch = Stopwatch.StartNew();
                var batchIndex = 0;
                try
                {
                    foreach (var batch in batches)
                    {
                        batchIndex++;
                        using var command = connection.CreateCommand();
                        command.CommandText = batch;
                        command.CommandTimeout = 120;
                        await command.ExecuteNonQueryAsync();
                    }

                    var createDateUtc = DateTime.UtcNow;
                    using (var historyCommand = connection.CreateCommand())
                    {
                        historyCommand.CommandText =
                            "INSERT INTO dbo.__MigrationsHistory (MigrationId, ProductVersion, CreateDate) VALUES (@MigrationId, @ProductVersion, @CreateDate);";
                        historyCommand.Parameters.AddWithValue("@MigrationId", migrationId);
                        historyCommand.Parameters.AddWithValue("@ProductVersion", productVersion);
                        historyCommand.Parameters.Add("@CreateDate", System.Data.SqlDbType.DateTime2).Value = createDateUtc;
                        await historyCommand.ExecuteNonQueryAsync();
                    }

                    stopwatch.Stop();
                    WriteLine(
                        $"[BAŞARILI] {migrationId}  ({batches.Count} komut grubu, {stopwatch.ElapsedMilliseconds} ms, UTC: {createDateUtc:yyyy-MM-dd HH:mm:ss})",
                        ConsoleColor.Green);
                    appliedCount++;
                }
                catch (Exception ex)
                {
                    stopwatch.Stop();
                    WriteLine(
                        $"[HATA]     {migrationId}  {batchIndex}/{batches.Count} komut grubunda başarısız oldu ({stopwatch.ElapsedMilliseconds} ms)",
                        ConsoleColor.Red);
                    WriteLine($"           Neden: {ex.Message}", ConsoleColor.Red);
                    totalStopwatch.Stop();
                    PrintSummary(appliedCount, skippedCount, files.Count, totalStopwatch.ElapsedMilliseconds, failed: true);
                    return 1;
                }
            }

            totalStopwatch.Stop();
            PrintSummary(appliedCount, skippedCount, files.Count, totalStopwatch.ElapsedMilliseconds, failed: false);
            return 0;
        }

        private static async Task EnsureHistoryTableSchemaAsync(SqlConnection connection)
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
            IF OBJECT_ID('dbo.__MigrationsHistory', 'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[__MigrationsHistory] (
                    [MigrationId] [nvarchar](150) NOT NULL,
                    [ProductVersion] [nvarchar](32) NOT NULL,
                    [CreateDate] [datetime2](3) NOT NULL DEFAULT (SYSUTCDATETIME()),
                    CONSTRAINT [PK___MigrationsHistory] PRIMARY KEY CLUSTERED ([MigrationId] ASC)
                );
            END;

            IF OBJECT_ID('dbo.__MigrationsHistory', 'U') IS NOT NULL
               AND COL_LENGTH('dbo.__MigrationsHistory', 'CreateDate') IS NULL
            BEGIN
                ALTER TABLE dbo.__MigrationsHistory ADD CreateDate DATETIME2(3) NOT NULL
                    CONSTRAINT DF___MigrationsHistory_CreateDate DEFAULT (SYSUTCDATETIME());
            END;
            """;
            await command.ExecuteNonQueryAsync();
        }

        private static async Task<HashSet<string>> GetAppliedMigrationsAsync(SqlConnection connection)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            using (var checkCommand = connection.CreateCommand())
            {
                checkCommand.CommandText = "SELECT OBJECT_ID('dbo.__MigrationsHistory', 'U');";
                var tableId = await checkCommand.ExecuteScalarAsync();
                if (tableId is null or DBNull)
                    return result;
            }

            using var selectCommand = connection.CreateCommand();
            selectCommand.CommandText = "SELECT MigrationId FROM dbo.__MigrationsHistory;";
            using var reader = await selectCommand.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                result.Add(reader.GetString(0));

            return result;
        }

        private static List<string> SplitBatches(string sql)
        {
            return Regex.Split(sql, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)
                .Select(b => b.Trim())
                .Where(b => b.Length > 0)
                .ToList();
        } 

        private static void PrintBanner(string text, ConsoleColor color)
        {
            var border = new string('#', text.Length + 4);
            WriteLine(border, color);
            WriteLine($"# {text} #", color);
            WriteLine(border, color);
        }

        private static void PrintSummary(int applied, int skipped, int total, long elapsedMs, bool failed)
        {
            Console.WriteLine();
            WriteLine("--------------------------------------------------", ConsoleColor.DarkGray);
            WriteLine($"Toplam dosya : {total}", ConsoleColor.Cyan);
            WriteLine($"Uygulanan    : {applied}", ConsoleColor.Green);
            WriteLine($"Atlanan      : {skipped}", ConsoleColor.Yellow);
            WriteLine($"Geçen süre   : {elapsedMs} ms", ConsoleColor.Cyan);
            WriteLine(failed ? "Sonuç        : HATA İLE DURDU" : "Sonuç        : TAMAMLANDI", failed ? ConsoleColor.Red : ConsoleColor.Green);
            WriteLine("--------------------------------------------------", ConsoleColor.DarkGray);
        }

        private static void WriteLine(string text, ConsoleColor color)
        {
            Console.ForegroundColor = color;
            Console.WriteLine(text);
            Console.ResetColor();
        }
    }

}
