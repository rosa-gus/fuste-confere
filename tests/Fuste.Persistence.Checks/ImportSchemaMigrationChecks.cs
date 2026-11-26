using Microsoft.Data.Sqlite;
using Fuste.Infrastructure.Persistence;
using Fuste.Domain.Reconciliation;

internal static class ImportSchemaMigrationChecks
{
    // Keep the v1 definition frozen: this exercises upgrade of an installed
    // database rather than creation of a database using the latest schema.
    internal const string VersionOneSchema = """
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

    public static async Task RunAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        var companyId = Guid.NewGuid();
        var reconciliationId = Guid.NewGuid();
        var createdAt = DateTimeOffset.Parse("2026-03-01T12:00:00-03:00");
        var databasePath = Path.Combine(directory, "fuste-confere.db");
        await using (var connection = new SqliteConnection($"Data Source={databasePath}"))
        {
            await connection.OpenAsync();
            using var create = connection.CreateCommand();
            create.CommandText = VersionOneSchema + "PRAGMA application_id=1179997012; PRAGMA user_version=1;";
            await create.ExecuteNonQueryAsync();
            using var insert = connection.CreateCommand();
            insert.CommandText = """
                INSERT INTO companies(id,name,name_key,created_at) VALUES($company,'Existing company','EXISTING COMPANY',$created);
                INSERT INTO reconciliations(id,company_id,created_at,created_at_ticks) VALUES($id,$company,$created,$ticks);
                INSERT INTO reconciliation_lines(reconciliation_id,position,sale_external_id,status,sale_date,gross_amount,
                    agreement_id,commission_percentage,expected_commission,reported_commission,difference)
                VALUES($id,0,'000123','Difference','2026-03-01','100.1234567890123456789',NULL,'5','5.01','5.02','-0.01');
                INSERT INTO reconciliation_lines(reconciliation_id,position,sale_external_id,status,sale_date,gross_amount,
                    agreement_id,commission_percentage,expected_commission,reported_commission,difference)
                VALUES($id,1,'unknown','UnrecognizedSale',NULL,NULL,NULL,NULL,NULL,'3',NULL);
                """;
            insert.Parameters.AddWithValue("$company", companyId.ToString("D"));
            insert.Parameters.AddWithValue("$id", reconciliationId.ToString("D"));
            insert.Parameters.AddWithValue("$created", createdAt.ToString("O"));
            insert.Parameters.AddWithValue("$ticks", createdAt.UtcTicks);
            await insert.ExecuteNonQueryAsync();
        }

        var repository = new SqliteFusteRepository(directory);
        await repository.InitializeAsync();
        var result = await repository.FindReconciliationAsync(reconciliationId, default)
            ?? throw new Exception("Migration removed an existing reconciliation.");
        Check(result.CompanyId == companyId && result.CreatedAt.EqualsExact(createdAt), "Migration changed reconciliation metadata.");
        Check(result.Lines.Count == 2 && result.Lines[0].SaleExternalId == "000123", "Migration changed ordered rows or leading-zero ID.");
        Check(result.Lines[0].GrossAmount == 100.1234567890123456789m && result.Lines[0].Difference == -0.01m,
            "Migration changed exact decimal snapshots.");
        Check(result.Lines[1].Status == ReconciliationStatus.UnrecognizedSale && result.Lines[1].GrossAmount is null && result.Lines[1].Difference is null,
            "Migration replaced missing values with zero.");
        await new SqliteFusteRepository(directory).InitializeAsync();
        await using var verify = new SqliteConnection($"Data Source={databasePath}");
        await verify.OpenAsync();
        using var version = verify.CreateCommand();
        version.CommandText = "PRAGMA user_version;";
        Check(Convert.ToInt64(await version.ExecuteScalarAsync()) == 3, "Import schema upgrade was not marked v3.");
        using var foreignKeys = verify.CreateCommand();
        foreignKeys.CommandText = "SELECT COUNT(*) FROM pragma_foreign_key_check;";
        Check(Convert.ToInt64(await foreignKeys.ExecuteScalarAsync()) == 0, "Migration left invalid foreign keys.");
        await RollbackCheckAsync(directory + "-rollback");
        Console.WriteLine("PASS import schema migration: v1 history preserved, failed upgrade fully rolled back, corrected retry succeeds.");
    }

    private static async Task RollbackCheckAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        await using var connection = new SqliteConnection($"Data Source={Path.Combine(directory, "fuste-confere.db")}");
        await connection.OpenAsync();
        using var create = connection.CreateCommand();
        create.CommandText = VersionOneSchema + """
            PRAGMA application_id=1179997012;
            PRAGMA user_version=1;
            CREATE TABLE import_profiles (sentinel TEXT);
            INSERT INTO import_profiles VALUES ('preserved');
            """;
        await create.ExecuteNonQueryAsync();
        using var setupCheck = connection.CreateCommand();
        setupCheck.CommandText = "PRAGMA user_version;";
        Check(Convert.ToInt64(await setupCheck.ExecuteScalarAsync()) == 1, "Rollback fixture did not create a version-one database.");
        setupCheck.CommandText = "SELECT sentinel FROM import_profiles;";
        Check((string?)await setupCheck.ExecuteScalarAsync() == "preserved", "Rollback fixture did not create its conflicting table.");
        var repository = new SqliteFusteRepository(directory);
        var rejected = false;
        try { await repository.InitializeAsync(); }
        catch (SqliteException) { rejected = true; }
        Check(rejected, "Conflicting profile table did not reject migration.");
        using var verify = connection.CreateCommand();
        verify.CommandText = "PRAGMA user_version;";
        Check(Convert.ToInt64(await verify.ExecuteScalarAsync()) == 1, "Failed upgrade advanced schema marker.");
        verify.CommandText = "SELECT COUNT(*) FROM pragma_table_info('reconciliations') WHERE name='import_configuration_json';";
        Check(Convert.ToInt64(await verify.ExecuteScalarAsync()) == 0, "Failed migration left a partially added snapshot column.");
        verify.CommandText = "SELECT sentinel FROM import_profiles;";
        Check((string?)await verify.ExecuteScalarAsync() == "preserved", "Failed migration changed existing data.");
        verify.CommandText = "DROP TABLE import_profiles;";
        await verify.ExecuteNonQueryAsync();
        await repository.InitializeAsync();
        verify.CommandText = "PRAGMA user_version;";
        Check(Convert.ToInt64(await verify.ExecuteScalarAsync()) == 3, "Migration could not retry after rollback.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
