using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using ProjectConferidor.Application.Abstractions;
using ProjectConferidor.Application.Database;
using ProjectConferidor.Domain.Agreements;
using ProjectConferidor.Domain.Companies;
using ProjectConferidor.Domain.Reconciliation;
using ProjectConferidor.Domain.Sales;
using ProjectConferidor.Infrastructure.Imports;
using ProjectConferidor.Infrastructure.Persistence;

var root = AppContext.BaseDirectory;
var work = Path.Combine(Path.GetTempPath(), $"conferidor-persistence-checks-{Guid.NewGuid():N}");
try
{
  CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pt-BR");
  ReconciliationRulesChecks.Run();
  await CsvImportProfileChecks.RunAsync();
  await ImportSchemaMigrationChecks.RunAsync(Path.Combine(work, "import-v1-upgrade"));
  await ImportProfilePersistenceChecks.RunAsync(Path.Combine(work, "import-profiles"));
  await CsvAmountChecks.RunAsync(Path.Combine(work, "csv-validation"));
  await ReconciliationValidationChecks.RunAsync(Path.Combine(work, "financial-validation"));
  await ReimportChecks.RunAsync(Path.Combine(work, "reimports"));
  var json = new JsonSerializerOptions { WriteIndented = true };
  json.Converters.Add(new JsonStringEnumConverter());
  void Check(bool value, string message) { if (!value) throw new Exception(message); }
  async Task Expect<T>(Func<Task> action) where T : Exception
  {
    try { await action(); } catch (T) { return; }
    throw new Exception($"Expected {typeof(T).Name}");
  }
  string Dir(string name) { var path = Path.Combine(work, name); Directory.CreateDirectory(path); return path; }
  string Encode(object? value) => JsonSerializer.Serialize(value, json);
  async Task<(RepresentedCompany Company, CommissionAgreement[] Agreements, CommissionReconciliation Result)> Fixture()
  {
    var company = RepresentedCompany.Create("Aurora Distribuição d'Água");
    var agreements = new[] { CommissionAgreement.Create(company.Id, new DateOnly(2026, 3, 1), 5), CommissionAgreement.Create(company.Id, new DateOnly(2026, 3, 16), 5.5m) };
    var csv = new CsvSourceFileReader();
    await using var salesStream = File.OpenRead(Path.Combine(root, "example/sales.csv"));
    await using var statementStream = File.OpenRead(Path.Combine(root, "example/statement.csv"));
    var sales = await csv.ReadSalesAsync(salesStream, default);
    var statement = await csv.ReadStatementAsync(statementStream, default);
    Check(sales.Errors.Count == 0 && statement.Errors.Count == 0, "Fixture CSV errors");
    var result = CommissionReconciler.Reconcile(company.Id, sales.Rows.Select(s => Sale.Create(s.ExternalId, s.Date, s.GrossAmount)).ToList(), statement.Rows.Select(s => StatementEntry.Create(s.SaleExternalId, s.CommissionAmount)).ToList(), agreements);
    return (company, agreements, result);
  }
  async Task<long> Scalar(string directory, string sql)
  {
    await using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(directory, "project-conferidor.db"), ForeignKeys = true }.ToString());
    await db.OpenAsync();
    await using var cmd = db.CreateCommand(); cmd.CommandText = sql;
    return Convert.ToInt64(await cmd.ExecuteScalarAsync());
  }
  async Task Execute(string directory, string sql)
  {
    await using var db = new SqliteConnection($"Data Source={Path.Combine(directory, "project-conferidor.db")}");
    await db.OpenAsync(); await using var cmd = db.CreateCommand(); cmd.CommandText = sql; await cmd.ExecuteNonQueryAsync();
  }
  var (company, agreements, result) = await Fixture();
  var other = RepresentedCompany.Create("Empresa preservada");
  var empty = CommissionReconciliation.Create(other.Id, []);
  var preciseLine = new ReconciliationLine("PRECISION", ReconciliationStatus.Difference, new DateOnly(2026, 3, 1), 123456789.1234567890123456789m, Guid.NewGuid(), 5.5555555555555555555555555555m, 123.45m, 123.46m, -.01m);
  var precise = new CommissionReconciliation(Guid.NewGuid(), company.Id, new DateTimeOffset(2026, 3, 3, 12, 30, 0, TimeSpan.FromHours(-3)), [preciseLine]);
  var source = Encode(new { Companies = new[] { company, other }, Agreements = agreements, Reconciliations = new[] { result, empty, precise } });
  var migrationDir = Dir("migration");
  var legacyPath = Path.Combine(migrationDir, "project-conferidor.json");
  await File.WriteAllTextAsync(legacyPath, source);
  var repo = new SqliteConferidorRepository(migrationDir);
  await Task.WhenAll(repo.InitializeAsync(), new SqliteConferidorRepository(migrationDir).InitializeAsync());
  Check(Encode(await repo.ListAsync(default)) == Encode(new[] { company, other }), "Company migration/order");
  Check(Encode(await repo.ListAgreementsForCompanyAsync(company.Id, default)) == Encode(agreements), "Agreement migration");
  Check(Encode(await repo.FindReconciliationAsync(result.Id, default)) == Encode(result), "Snapshot migration");
  Check(Encode(await repo.FindReconciliationAsync(precise.Id, default)) == Encode(precise), "Decimal precision, offset or deleted agreement snapshot changed");
  Check((await repo.FindReconciliationAsync(empty.Id, default))!.Lines.Count == 0, "Empty reconciliation disappeared");
  Check((await repo.FindReconciliationAsync(result.Id, default))!.Lines.Count == 53, "Showcase output count");
  Check((await repo.FindReconciliationAsync(result.Id, default))!.Lines.Sum(l => l.ExpectedCommission ?? 0) == 28662.86m, "Financial total changed");
  Check((await repo.FindReconciliationAsync(result.Id, default))!.Lines.Sum(l => l.Difference ?? 0) == 307.18m, "Difference total changed");
  Check(await Scalar(migrationDir, "PRAGMA user_version;") == 3, "Schema version");
  Check(await Scalar(migrationDir, "SELECT COUNT(*) FROM reconciliation_lines WHERE typeof(gross_amount)='real' OR typeof(expected_commission)='real';") == 0, "Money stored as floating point");
  Check(await Scalar(migrationDir, "SELECT COUNT(*) FROM pragma_foreign_key_check;") == 0, "Foreign key violation");
  Check(await repo.DeleteAgreementAsync(other.Id, agreements[0].Id, default) == false, "Wrong-company agreement deletion");
  Check(await repo.DeleteReconciliationAsync(other.Id, result.Id, default) == false, "Wrong-company history deletion");
  Check(await repo.DeleteAgreementAsync(company.Id, agreements[0].Id, default), "Delete agreement");
  Check(Encode(await repo.FindReconciliationAsync(result.Id, default)) == Encode(result), "Deleted agreement rewrote snapshot");
  await repo.AddAsync(CommissionAgreement.Create(company.Id, agreements[0].EffectiveFrom, 6), default);
  Check(await repo.DeleteReconciliationAsync(company.Id, precise.Id, default), "Delete reconciliation");
  Check(await Scalar(migrationDir, $"SELECT COUNT(*) FROM reconciliation_lines WHERE reconciliation_id='{precise.Id:D}';") == 0, "Orphan deleted lines");
  Check(await repo.DeleteCompanyAsync(company.Id, default), "Cascade company deletion");
  Check(await Scalar(migrationDir, $"SELECT COUNT(*) FROM agreements WHERE company_id='{company.Id:D}';") == 0, "Orphan agreement");
  Check(await repo.FindReconciliationAsync(result.Id, default) == null, "Cascade history deletion");
  var reopened = new SqliteConferidorRepository(migrationDir);
  await reopened.InitializeAsync();
  Check((await reopened.ListAsync(default)).Count == 1, "JSON replayed after deletion");
  Check(await File.ReadAllTextAsync(legacyPath) == source, "Original JSON changed");
  Check(await reopened.FindReconciliationAsync(empty.Id, default) != null, "Unrelated empty result deleted");
  await reopened.DeleteCompanyAsync(other.Id, default);
  Check((await new SqliteConferidorRepository(migrationDir).ListAsync(default)).Count == 0, "Empty DB reimports legacy JSON");
  Console.WriteLine("PASS migration: full example, exact decimals, offsets, nulls, ordering, snapshots, cascades, original file and exactly-once initialization.");

  var failDir = Dir("failed-migration"); var failPath = Path.Combine(failDir, "project-conferidor.json");
  await File.WriteAllTextAsync(failPath, "{malformed");
  var failed = new SqliteConferidorRepository(failDir);
  await Expect<InvalidDataException>(() => failed.InitializeAsync());
  Check(await Scalar(failDir, "PRAGMA user_version;") == 0, "Failed migration marked complete");
  Check(await Scalar(failDir, "SELECT COUNT(*) FROM sqlite_schema WHERE type='table' AND name NOT LIKE 'sqlite_%';") == 0, "Failed migration left partial tables");
  Check(await File.ReadAllTextAsync(failPath) == "{malformed", "Malformed source changed");
  var invalidSource = Encode(new { Companies = new[] { company }, Agreements = new[] { CommissionAgreement.Create(Guid.NewGuid(), new DateOnly(2026, 1, 1), 5) }, Reconciliations = Array.Empty<CommissionReconciliation>() });
  await File.WriteAllTextAsync(failPath, invalidSource);
  await Expect<InvalidDataException>(() => failed.InitializeAsync());
  Check(await Scalar(failDir, "PRAGMA user_version;") == 0, "FK failure marked complete");
  Check(await File.ReadAllTextAsync(failPath) == invalidSource, "Invalid reference source changed");
  await File.WriteAllTextAsync(failPath, source);
  await failed.InitializeAsync();
  Check((await failed.ListAsync(default)).Count == 2, "Migration retry failed");
  await Execute(failDir, "PRAGMA user_version=4;");
  await Expect<InvalidDataException>(() => new SqliteConferidorRepository(failDir).InitializeAsync());
  Check(await Scalar(failDir, "PRAGMA user_version;") == 4, "Future schema overwritten");
  var unknownDir = Dir("unknown");
  await Execute(unknownDir, "CREATE TABLE unrelated (id INTEGER PRIMARY KEY);");
  await Expect<InvalidDataException>(() => new SqliteConferidorRepository(unknownDir).InitializeAsync());
  Check(await Scalar(unknownDir, "SELECT COUNT(*) FROM sqlite_schema WHERE name='unrelated';") == 1, "Unknown database changed");
  Console.WriteLine("PASS migration failures: malformed JSON, missing parent, rollback, retry, future schema and unrelated database protection.");

  var writeDir = Dir("writes"); var first = new SqliteConferidorRepository(writeDir); var second = new SqliteConferidorRepository(writeDir);
  await first.InitializeAsync();
  await first.AddAsync(company, default);
  await Expect<RepositoryConflictException>(() => second.AddAsync(RepresentedCompany.Create(company.Name.ToUpperInvariant()), default));
  await first.AddAsync(agreements[0], default);
  await Expect<RepositoryConflictException>(() => second.AddAsync(CommissionAgreement.Create(company.Id, agreements[0].EffectiveFrom, 7), default));
  var badResult = CommissionReconciliation.Create(company.Id, [result.Lines[0], result.Lines[0] with { Status = (ReconciliationStatus)999 }]);
  await Expect<SqliteException>(() => first.AddAsync(badResult, default));
  Check(await first.FindReconciliationAsync(badResult.Id, default) == null, "Partial reconciliation committed");
  Check(await Scalar(writeDir, "SELECT COUNT(*) FROM reconciliation_lines;") == 0, "Partial reconciliation lines committed");
  await first.AddAsync(result, default);
  var savedBefore = Encode(await first.FindReconciliationAsync(result.Id, default));
  using (var canceled = new CancellationTokenSource())
  {
    canceled.Cancel(); await Expect<OperationCanceledException>(() => first.DeleteCompanyAsync(company.Id, canceled.Token));
  }
  Check(Encode(await first.FindReconciliationAsync(result.Id, default)) == savedBefore, "Canceled delete changed data");
  await first.DeleteCompanyAsync(company.Id, default);
  await Expect<KeyNotFoundException>(() => second.AddAsync(agreements[0], default));
  await Expect<KeyNotFoundException>(() => second.AddAsync(result, default));
  for (var i = 0; i < 12; i++)
  {
    var target = RepresentedCompany.Create($"Concurrent {i}"); await first.AddAsync(target, default);
    var agreement = CommissionAgreement.Create(target.Id, new DateOnly(2026, 1, 1), 5);
    var reconciliation = CommissionReconciliation.Create(target.Id, result.Lines);
    async Task AddAgreement() { try { await second.AddAsync(agreement, default); } catch (KeyNotFoundException) { } }
    async Task AddResult() { try { await second.AddAsync(reconciliation, default); } catch (KeyNotFoundException) { } }
    await Task.WhenAll(Task.Run(() => first.DeleteCompanyAsync(target.Id, default)), Task.Run(AddAgreement), Task.Run(AddResult));
    Check((await first.ListAgreementsForCompanyAsync(target.Id, default)).Count == 0, "Concurrent orphan agreement");
    Check((await first.ListReconciliationsForCompanyAsync(target.Id, default)).Count == 0, "Concurrent orphan result");
  }
  Check(await Scalar(writeDir, "SELECT COUNT(*) FROM pragma_foreign_key_check;") == 0, "Concurrent FK violations");
  Check(await Scalar(writeDir, "SELECT COUNT(*) FROM reconciliation_lines;") == 0, "Concurrent orphan lines");
  Console.WriteLine("PASS write integrity: unique constraints, transaction rollback, cancellation, missing parents and concurrent writes/deletion across repository instances.");
  var resetDir = Dir("reset");
  var resetSourcePath = Path.Combine(resetDir, "project-conferidor.json");
  await File.WriteAllTextAsync(resetSourcePath, source);
  var resetRepository = new SqliteConferidorRepository(resetDir);
  await resetRepository.InitializeAsync();
  var databaseUseCases = new DatabaseUseCases(resetRepository);
  var rejectedReset = await databaseUseCases.ResetAsync("EXCLUIR", default);
  Check(!rejectedReset.IsSuccess && rejectedReset.StatusCode == 400, "Missing reset confirmation accepted");
  Check((await resetRepository.ListAsync(default)).Count == 2, "Invalid confirmation changed data");
  using (var canceled = new CancellationTokenSource())
  {
    canceled.Cancel();
    await Expect<OperationCanceledException>(() => resetRepository.ResetAsync(canceled.Token));
  }
  Check((await resetRepository.ListAsync(default)).Count == 2, "Canceled reset changed data");
  await Execute(resetDir, $"CREATE TRIGGER block_reset BEFORE DELETE ON companies WHEN OLD.id = '{other.Id:D}' BEGIN SELECT RAISE(ABORT, 'Test reset rollback'); END;");
  await Expect<SqliteException>(() => resetRepository.ResetAsync(default));
  Check((await resetRepository.ListAsync(default)).Count == 2, "Failed reset partially deleted companies");
  Check(Encode(await resetRepository.FindReconciliationAsync(result.Id, default)) == Encode(result), "Failed reset partially deleted history");
  Check((await resetRepository.ListAgreementsForCompanyAsync(company.Id, default)).Count == 2, "Failed reset partially deleted agreements");
  await Execute(resetDir, "DROP TRIGGER block_reset;");
  Check((await databaseUseCases.ResetAsync("RESET", default)).IsSuccess, "Confirmed reset rejected");
  foreach (var table in new[] { "companies", "agreements", "reconciliations", "reconciliation_lines" })
  {
    Check(await Scalar(resetDir, $"SELECT COUNT(*) FROM {table};") == 0, $"Reset left records in {table}");
  }
  Check(await Scalar(resetDir, "PRAGMA user_version;") == 3, "Reset removed migration marker");
  Check(await File.ReadAllTextAsync(resetSourcePath) == source, "Reset changed legacy snapshot");
  var afterReset = new SqliteConferidorRepository(resetDir);
  Check((await afterReset.ListAsync(default)).Count == 0, "Restart after reset reimported JSON");
  await afterReset.ResetAsync(default);
  await afterReset.AddAsync(company, default);
  await afterReset.AddAsync(agreements[0], default);
  await afterReset.AddAsync(result, default);
  Check(Encode(await afterReset.FindReconciliationAsync(result.Id, default)) == Encode(result), "Database unusable after reset");
  Console.WriteLine("PASS reset: confirmation, cancellation, atomic rollback, all tables cleared, repeat reset, retained migration marker and new writes after restart.");
  Console.WriteLine("All SQLite persistence integration checks passed.");
}
finally
{
  SqliteConnection.ClearAllPools();
  if (Directory.Exists(work)) Directory.Delete(work, recursive: true);
}
