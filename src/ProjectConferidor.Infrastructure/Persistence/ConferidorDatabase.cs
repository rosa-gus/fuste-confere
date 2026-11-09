using Microsoft.Data.Sqlite;

namespace ProjectConferidor.Infrastructure.Persistence;

internal sealed class ConferidorDatabase
{
    private const int SchemaVersion = 2;
    private const int ApplicationId = 0x434F4E46; // CONF
    private readonly SemaphoreSlim _initializationGate = new(1, 1);
    private readonly string _directory;
    private readonly string _connectionString;
    private volatile bool _initialized;

    public ConferidorDatabase(string? dataDirectory)
    {
        var directory = dataDirectory ?? Environment.GetEnvironmentVariable("PROJECT_CONFERIDOR_DATA_DIR");
        if (string.IsNullOrWhiteSpace(directory))
        {
            directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ProjectConferidor");
        }
        _directory = Path.GetFullPath(directory);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(_directory, "project-conferidor.db"),
            Mode = SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true,
            DefaultTimeout = 30
        }.ToString();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (_initialized) return;
        await _initializationGate.WaitAsync(cancellationToken);
        try
        {
            if (_initialized) return;
            Directory.CreateDirectory(_directory);
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            using (var journal = connection.CreateCommand("PRAGMA journal_mode = WAL;"))
            {
                await journal.ExecuteScalarAsync(cancellationToken);
            }

            // The immediate transaction serializes initialization across processes
            // as well as making schema creation and JSON import a single commit.
            using var transaction = connection.BeginTransaction();
            using var versionCommand = connection.CreateCommand("PRAGMA user_version;", transaction);
            var version = Convert.ToInt32(await versionCommand.ExecuteScalarAsync(cancellationToken));
            using var idCommand = connection.CreateCommand("PRAGMA application_id;", transaction);
            var applicationId = Convert.ToInt32(await idCommand.ExecuteScalarAsync(cancellationToken));
            if (version != 0 && (version < 1 || version > SchemaVersion || applicationId != ApplicationId))
            {
                throw new InvalidDataException("The SQLite database has an unsupported schema or belongs to another application.");
            }
            if (version == 0)
            {
                using var tablesCommand = connection.CreateCommand("SELECT COUNT(*) FROM sqlite_schema WHERE type = 'table' AND name NOT LIKE 'sqlite_%';", transaction);
                if (applicationId != 0 || Convert.ToInt64(await tablesCommand.ExecuteScalarAsync(cancellationToken)) != 0)
                {
                    throw new InvalidDataException("Refusing to initialize an unrecognized SQLite database.");
                }
                using var schema = connection.CreateCommand(Schema, transaction);
                await schema.ExecuteNonQueryAsync(cancellationToken);
            }
            if (version < 2)
            {
                // Execute each DDL separately so a later failure cannot be lost
                // while advancing a multi-statement command after schema changes.
                using (var snapshotColumn = connection.CreateCommand(MigrationV2SnapshotColumn, transaction))
                    await snapshotColumn.ExecuteNonQueryAsync(cancellationToken);
                using (var profiles = connection.CreateCommand(MigrationV2Profiles, transaction))
                    await profiles.ExecuteNonQueryAsync(cancellationToken);
            }
            if (version == 0)
                await LegacyJsonMigration.ImportAsync(connection, transaction, Path.Combine(_directory, "project-conferidor.json"), cancellationToken);
            if (version < SchemaVersion)
            {
                using var mark = connection.CreateCommand($"PRAGMA application_id = {ApplicationId}; PRAGMA user_version = {SchemaVersion};", transaction);
                await mark.ExecuteNonQueryAsync(cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            await transaction.CommitAsync(cancellationToken);
            _initialized = true;
        }
        finally
        {
            _initializationGate.Release();
        }
    }

    public async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);
        var connection = new SqliteConnection(_connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private const string MigrationV2SnapshotColumn = "ALTER TABLE reconciliations ADD COLUMN import_configuration_json TEXT;";

    private const string MigrationV2Profiles = """
        CREATE TABLE import_profiles (
            id TEXT NOT NULL PRIMARY KEY,
            company_id TEXT NOT NULL REFERENCES companies(id) ON DELETE CASCADE,
            name TEXT NOT NULL CHECK(length(trim(name)) BETWEEN 1 AND 160),
            name_key TEXT NOT NULL,
            revision INTEGER NOT NULL CHECK(revision >= 1),
            configuration_json TEXT NOT NULL,
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL,
            UNIQUE(company_id, name_key)
        ) STRICT;
        """;

    private const string Schema = """
        CREATE TABLE companies (
            id TEXT NOT NULL PRIMARY KEY,
            name TEXT NOT NULL CHECK(length(trim(name)) BETWEEN 1 AND 160),
            name_key TEXT NOT NULL UNIQUE,
            created_at TEXT NOT NULL
        ) STRICT;
        CREATE TABLE agreements (
            id TEXT NOT NULL PRIMARY KEY,
            company_id TEXT NOT NULL REFERENCES companies(id) ON DELETE CASCADE,
            effective_from TEXT NOT NULL,
            percentage TEXT NOT NULL,
            UNIQUE(company_id, effective_from)
        ) STRICT;
        CREATE TABLE reconciliations (
            id TEXT NOT NULL PRIMARY KEY,
            company_id TEXT NOT NULL REFERENCES companies(id) ON DELETE CASCADE,
            created_at TEXT NOT NULL,
            created_at_ticks INTEGER NOT NULL
        ) STRICT;
        CREATE INDEX ix_reconciliations_company_created ON reconciliations(company_id, created_at_ticks DESC);
        CREATE TABLE reconciliation_lines (
            reconciliation_id TEXT NOT NULL REFERENCES reconciliations(id) ON DELETE CASCADE,
            position INTEGER NOT NULL CHECK(position >= 0),
            sale_external_id TEXT NOT NULL,
            status TEXT NOT NULL CHECK(status IN ('Matched', 'Difference', 'MissingFromStatement', 'UnrecognizedSale', 'MissingAgreement')),
            sale_date TEXT,
            gross_amount TEXT,
            agreement_id TEXT,
            commission_percentage TEXT,
            expected_commission TEXT,
            reported_commission TEXT,
            difference TEXT,
            PRIMARY KEY(reconciliation_id, position)
        ) STRICT;
        """;
    // agreement_id is deliberately a snapshot identifier, not a foreign key:
    // deleting an agreement must not erase or rewrite previously saved results.
}
