using System.Security.Cryptography;
using System.Text;
using Npgsql;

var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Mono");
if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine("ConnectionStrings__Mono is required to run database migrations.");
    return 2;
}

var migrationsDirectory = Environment.GetEnvironmentVariable("MONO_MIGRATIONS_DIRECTORY");
migrationsDirectory = string.IsNullOrWhiteSpace(migrationsDirectory)
    ? Path.Combine(AppContext.BaseDirectory, "db", "migrations")
    : Path.GetFullPath(migrationsDirectory);

if (!Directory.Exists(migrationsDirectory))
{
    Console.Error.WriteLine($"Migrations directory does not exist: {migrationsDirectory}");
    return 2;
}

var migrations = Directory.GetFiles(migrationsDirectory, "*.sql")
    .OrderBy(Path.GetFileName, StringComparer.Ordinal)
    .Select(path => new Migration(
        Path.GetFileName(path)!,
        File.ReadAllText(path, Encoding.UTF8),
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant()))
    .ToArray();

if (migrations.Length == 0)
{
    Console.Error.WriteLine("No SQL migrations were found.");
    return 2;
}

await using var dataSource = NpgsqlDataSource.Create(connectionString);
await using var connection = await dataSource.OpenConnectionAsync();
await using (var createLedger = new NpgsqlCommand("""
    CREATE TABLE IF NOT EXISTS mono_schema_migrations (
        migration_id text PRIMARY KEY,
        checksum_sha256 text NOT NULL CHECK (checksum_sha256 ~ '^[0-9a-f]{64}$'),
        applied_at timestamp with time zone NOT NULL
    );
    """, connection))
{
    await createLedger.ExecuteNonQueryAsync();
}

foreach (var migration in migrations)
{
    await using var transaction = await connection.BeginTransactionAsync();
    await using (var migrationLock = new NpgsqlCommand(
        "SELECT pg_advisory_xact_lock(hashtextextended('mono-schema-migrations', 0));",
        connection, transaction))
    {
        await migrationLock.ExecuteNonQueryAsync();
    }

    string? recordedChecksum;
    await using (var find = new NpgsqlCommand(
        "SELECT checksum_sha256 FROM mono_schema_migrations WHERE migration_id = @id;",
        connection, transaction))
    {
        find.Parameters.AddWithValue("id", migration.Id);
        recordedChecksum = (string?)await find.ExecuteScalarAsync();
    }

    if (recordedChecksum is not null)
    {
        if (!StringComparer.Ordinal.Equals(recordedChecksum, migration.Checksum))
        {
            Console.Error.WriteLine($"Applied migration changed on disk: {migration.Id}");
            return 3;
        }

        await transaction.CommitAsync();
        Console.WriteLine($"Already applied: {migration.Id}");
        continue;
    }

    try
    {
        await using (var apply = new NpgsqlCommand(migration.Sql, connection, transaction))
            await apply.ExecuteNonQueryAsync();

        await using (var record = new NpgsqlCommand("""
            INSERT INTO mono_schema_migrations (migration_id, checksum_sha256, applied_at)
            VALUES (@id, @checksum, now());
            """, connection, transaction))
        {
            record.Parameters.AddWithValue("id", migration.Id);
            record.Parameters.AddWithValue("checksum", migration.Checksum);
            await record.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
        Console.WriteLine($"Applied: {migration.Id}");
    }
    catch (Exception exception)
    {
        await transaction.RollbackAsync();
        Console.Error.WriteLine($"Migration failed ({migration.Id}): {exception.Message}");
        return 1;
    }
}

Console.WriteLine($"Database migrations complete ({migrations.Length} files checked).");
return 0;

internal sealed record Migration(string Id, string Sql, string Checksum);
