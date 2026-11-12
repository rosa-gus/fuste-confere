using System.Text;
using Microsoft.Data.Sqlite;
using ProjectConferidor.Application.Reconciliation;
using ProjectConferidor.Domain.Agreements;
using ProjectConferidor.Domain.Companies;
using ProjectConferidor.Domain.Imports;
using ProjectConferidor.Domain.Reconciliation;
using ProjectConferidor.Infrastructure.Imports;
using ProjectConferidor.Infrastructure.Persistence;

internal static class ReimportChecks
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static readonly byte[] Sales = Encoding.UTF8.GetBytes("sale_id,sale_date,gross_amount\nV1,2026-03-01,100\n");
    private static readonly byte[] Statement = Encoding.UTF8.GetBytes("sale_id,commission_amount\nV1,5\n");

    public static async Task RunAsync(string directory)
    {
        var configuration = new ImportConfiguration(1,
            new(",", "invariant", "yyyy-MM-dd", new() { ["externalId"] = "sale_id", ["date"] = "sale_date", ["grossAmount"] = "gross_amount" }),
            new(",", "invariant", "auto", new() { ["saleExternalId"] = "sale_id", ["commissionAmount"] = "commission_amount" }));
        var reordered = configuration.Snapshot();
        reordered.Sales.Columns.Clear();
        reordered.Sales.Columns.Add("grossAmount", " GROSS_AMOUNT ");
        reordered.Sales.Columns.Add("date", "sale_date");
        reordered.Sales.Columns.Add("externalId", "sale_id");
        var fingerprint = ImportFingerprint.Create(Sales, Statement, configuration);
        Check(fingerprint == ImportFingerprint.Create(Sales, Statement, reordered), "Dictionary/header normalization changed identity");
        Check(fingerprint != ImportFingerprint.Create(Statement, Sales, configuration), "File roles omitted from identity");
        Check(fingerprint != ImportFingerprint.Create(Sales, Statement, null), "Legacy/configured identity must remain distinct");
        Check(fingerprint != ImportFingerprint.Create(Sales, Statement, configuration with { Sales = configuration.Sales with { DateFormat = "auto" } }), "Format omitted from identity");

        var repo = new SqliteConferidorRepository(directory);
        var company = RepresentedCompany.Create("Reimports");
        var other = RepresentedCompany.Create("Other reimports");
        await repo.AddAsync(company, default);
        await repo.AddAsync(other, default);
        var agreement = CommissionAgreement.Create(company.Id, new DateOnly(2026, 3, 1), 5);
        await repo.AddAsync(agreement, default);
        async Task<ProjectConferidor.Application.Common.OperationResult<CommissionReconciliation>> Run(
            Guid companyId, Guid? request = null, Guid? origin = null, byte[]? sales = null,
            ImportConfiguration? options = null, CancellationToken token = default)
        {
            var repository = new SqliteConferidorRepository(directory);
            var useCases = new ReconciliationUseCases(repository, repository, repository, new CsvSourceFileReader());
            using var salesStream = new MemoryStream(sales ?? Sales);
            using var statementStream = new MemoryStream(Statement);
            return await useCases.RunAsync(companyId, salesStream, statementStream, options, request, origin, token);
        }

        var request = Guid.NewGuid();
        var first = await Run(company.Id, request);
        Check(first.IsSuccess, "First import failed");
        var firstId = first.Value!.Id;
        Check((await Run(company.Id, request)).Value?.Id == firstId, "Normal request retry duplicated result");
        var duplicate = await Run(company.Id);
        Check(duplicate.StatusCode == 409 && duplicate.Conflict?.Code == "duplicate_import"
            && duplicate.Conflict.ExistingReconciliation?.Id == firstId, "Duplicate not identified");
        Check((await Run(other.Id)).IsSuccess, "Identity leaked between companies");
        Check((await Run(other.Id, origin: firstId)).Conflict?.Code == "invalid_reprocess_reference", "Foreign origin accepted");
        var changed = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(Sales).Replace(",100", ",200"));
        Check((await Run(company.Id, request, sales: changed)).Conflict?.Code == "request_id_conflict", "Request ID reused for different bytes");
        Check((await Run(company.Id, origin: firstId, sales: changed)).Conflict?.Code == "invalid_reprocess_reference", "Mismatched origin accepted");
        Check((await Run(company.Id, sales: changed)).IsSuccess, "New bytes incorrectly rejected");
        Check((await Run(company.Id, options: configuration)).IsSuccess, "Different configuration incorrectly rejected");
        Check((await Run(company.Id, options: reordered)).Conflict?.Code == "duplicate_import", "Reordered mapping bypassed detection");

        await repo.DeleteAgreementAsync(company.Id, agreement.Id, default);
        await repo.AddAsync(CommissionAgreement.Create(company.Id, agreement.EffectiveFrom, 6), default);
        Check((await Run(company.Id)).Conflict?.Code == "duplicate_import", "Changed agreements bypassed detection");
        var retryId = Guid.NewGuid();
        var reprocessed = await Run(company.Id, retryId, firstId);
        Check(reprocessed.IsSuccess && reprocessed.Value!.Id != firstId && reprocessed.Value.ReprocessedFromId == firstId,
            "Confirmed reprocessing not saved");
        Check(reprocessed.Value!.Lines[0].ExpectedCommission == 6, "Current agreements not used");
        Check((await repo.FindReconciliationAsync(firstId, default))!.Lines[0].ExpectedCommission == 5, "Previous snapshot changed");
        Check((await Run(company.Id, retryId, firstId)).Value?.Id == reprocessed.Value.Id, "Confirmed request retry duplicated result");
        Check((await Run(company.Id, retryId)).Conflict?.Code == "request_id_conflict", "Request intention not checked");
        Check((await Run(company.Id)).Conflict?.ExistingReconciliation?.Id == reprocessed.Value.Id, "Duplicate did not point to latest result");

        // The original request intention survives ON DELETE SET NULL for idempotent retries.
        await repo.DeleteReconciliationAsync(company.Id, firstId, default);
        var replay = await Run(company.Id, retryId, firstId);
        Check(replay.Value?.Id == reprocessed.Value.Id && replay.Value.ReprocessedFromId is null, "Origin deletion broke retry or removed result");
        Check((await Run(company.Id, Guid.NewGuid(), firstId)).Conflict?.Code == "invalid_reprocess_reference", "Deleted origin accepted");

        var parallelCompany = RepresentedCompany.Create("Concurrent reimports");
        await repo.AddAsync(parallelCompany, default);
        var concurrent = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Task.Run(() => Run(parallelCompany.Id))));
        Check(concurrent.Count(item => item.IsSuccess) == 1 && concurrent.Count(item => item.Conflict?.Code == "duplicate_import") == 5,
            "Concurrent normal imports wrote duplicates");
        var parallelOrigin = concurrent.Single(item => item.IsSuccess).Value!.Id;
        var parallelRequest = Guid.NewGuid();
        var retries = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Task.Run(() => Run(parallelCompany.Id, parallelRequest, parallelOrigin))));
        Check(retries.All(item => item.IsSuccess) && retries.Select(item => item.Value!.Id).Distinct().Count() == 1,
            "Concurrent confirmed retries wrote duplicates");
        Check((await repo.ListReconciliationsForCompanyAsync(parallelCompany.Id, default)).Count == 2, "Unexpected parallel history count");
        await repo.DeleteCompanyAsync(parallelCompany.Id, default);
        Check((await repo.ListReconciliationsForCompanyAsync(parallelCompany.Id, default)).Count == 0, "Company deletion failed with reprocess links");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        try { await Run(company.Id, token: cancelled.Token); throw new Exception("Cancelled import succeeded"); }
        catch (OperationCanceledException) { }

        // A failed line write must not leave a fingerprint/request header behind.
        var rollbackCompany = RepresentedCompany.Create("Import rollback");
        await repo.AddAsync(rollbackCompany, default);
        await using (var db = new SqliteConnection($"Data Source={Path.Combine(directory, "project-conferidor.db")}"))
        {
            await db.OpenAsync();
            using var command = db.CreateCommand();
            command.CommandText = "CREATE TRIGGER fail_import BEFORE INSERT ON reconciliation_lines BEGIN SELECT RAISE(ABORT, 'Simulated failure'); END;";
            await command.ExecuteNonQueryAsync();
            var rollbackRequest = Guid.NewGuid();
            try { await Run(rollbackCompany.Id, rollbackRequest); throw new Exception("Failing write succeeded"); }
            catch (SqliteException) { }
            Check((await repo.ListReconciliationsForCompanyAsync(rollbackCompany.Id, default)).Count == 0, "Failure left a partial import");
            command.CommandText = "DROP TRIGGER fail_import;";
            await command.ExecuteNonQueryAsync();
            Check((await Run(rollbackCompany.Id, rollbackRequest)).IsSuccess, "Rolled-back request could not be retried");
            var saved = (await repo.ListReconciliationsForCompanyAsync(rollbackCompany.Id, default)).Single();
            await repo.DeleteReconciliationAsync(rollbackCompany.Id, saved.Id, default);
            Check((await Run(rollbackCompany.Id)).IsSuccess, "Deleting all matching history did not allow a new import");
        }
        await repo.ResetAsync(default);
        Check((await repo.ListAsync(default)).Count == 0, "Reset failed with reprocess references");

        await CheckV2MigrationAsync(Path.Combine(directory, "v2"));
        Console.WriteLine("PASS reimports: fingerprint stability, conflict/reference ownership, agreement snapshots, origin deletion, idempotent retries, concurrent repositories, cancellation and v2 migration rollback.");
    }

    private static async Task CheckV2MigrationAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        var company = RepresentedCompany.Create("Version 2 company");
        var legacy = CommissionReconciliation.Create(company.Id, []);
        await using var db = new SqliteConnection($"Data Source={Path.Combine(directory, "project-conferidor.db")};Pooling=False");
        await db.OpenAsync();
        async Task Execute(string sql) { using var command = db.CreateCommand(); command.CommandText = sql; await command.ExecuteNonQueryAsync(); }
        await Execute(ImportSchemaMigrationChecks.VersionOneSchema);
        await Execute("ALTER TABLE reconciliations ADD COLUMN import_configuration_json TEXT;");
        await Execute("""
            CREATE TABLE import_profiles (
                id TEXT NOT NULL PRIMARY KEY, company_id TEXT NOT NULL REFERENCES companies(id) ON DELETE CASCADE,
                name TEXT NOT NULL CHECK(length(trim(name)) BETWEEN 1 AND 160), name_key TEXT NOT NULL,
                revision INTEGER NOT NULL CHECK(revision >= 1), configuration_json TEXT NOT NULL,
                created_at TEXT NOT NULL, updated_at TEXT NOT NULL, UNIQUE(company_id, name_key)
            ) STRICT;
            """);
        await Execute($"INSERT INTO companies VALUES('{company.Id:D}', 'Version 2 company', 'VERSION 2 COMPANY', '{company.CreatedAt:O}');");
        await Execute($"INSERT INTO reconciliations VALUES('{legacy.Id:D}', '{company.Id:D}', '{legacy.CreatedAt:O}', {legacy.CreatedAt.UtcTicks}, NULL);");
        await Execute("PRAGMA application_id=1129270854;");
        await Execute("PRAGMA user_version=2;");
        await Execute("CREATE INDEX ix_reconciliations_fingerprint ON companies(name);");
        try { await new SqliteConferidorRepository(directory).InitializeAsync(); throw new Exception("Conflicting v3 index accepted"); }
        catch (SqliteException) { }
        using var verify = db.CreateCommand();
        verify.CommandText = "PRAGMA user_version;";
        Check(Convert.ToInt32(await verify.ExecuteScalarAsync()) == 2, "Failed v3 migration advanced version");
        verify.CommandText = "SELECT COUNT(*) FROM pragma_table_info('reconciliations') WHERE name='import_fingerprint';";
        Check(Convert.ToInt64(await verify.ExecuteScalarAsync()) == 0, "Failed v3 migration left columns");
        await Execute("DROP INDEX ix_reconciliations_fingerprint;");
        var upgraded = new SqliteConferidorRepository(directory);
        var preserved = await upgraded.FindReconciliationAsync(legacy.Id, default);
        Check(preserved is not null && preserved.ImportFingerprint is null && preserved.RequestId is null, "Old history changed or fingerprint fabricated");
        verify.CommandText = "PRAGMA user_version;";
        Check(Convert.ToInt32(await verify.ExecuteScalarAsync()) == 3, "v2 upgrade did not complete");
    }
}
