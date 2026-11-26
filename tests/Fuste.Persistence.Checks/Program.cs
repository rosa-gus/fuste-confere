using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using Fuste.Application.Abstractions;
using Fuste.Application.Database;
using Fuste.Domain.Agreements;
using Fuste.Domain.Companies;
using Fuste.Domain.Reconciliation;
using Fuste.Domain.Sales;
using Fuste.Infrastructure.Imports;
using Fuste.Infrastructure.Persistence;

var root = AppContext.BaseDirectory;
var work = Path.Combine(Path.GetTempPath(), $"fuste-persistence-checks-{Guid.NewGuid():N}");
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
    await using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(directory, "fuste-confere.db"), ForeignKeys = true }.ToString());
    await db.OpenAsync();
    await using var cmd = db.CreateCommand(); cmd.CommandText = sql;
    return Convert.ToInt64(await cmd.ExecuteScalarAsync());
  }
  async Task Execute(string directory, string sql)
  {
    await using var db = new SqliteConnection($"Data Source={Path.Combine(directory, "fuste-confere.db")}");
    await db.OpenAsync(); await using var cmd = db.CreateCommand(); cmd.CommandText = sql; await cmd.ExecuteNonQueryAsync();
  }
  var (company, agreements, result) = await Fixture();
  var other = RepresentedCompany.Create("Empresa preservada");
  var empty = CommissionReconciliation.Create(other.Id, []);
  var preciseLine = new ReconciliationLine("PRECISION", ReconciliationStatus.Difference, new DateOnly(2026, 3, 1), 123456789.1234567890123456789m, Guid.NewGuid(), 5.5555555555555555555555555555m, 123.45m, 123.46m, -.01m);
  var precise = new CommissionReconciliation(Guid.NewGuid(), company.Id, new DateTimeOffset(2026, 3, 3, 12, 30, 0, TimeSpan.FromHours(-3)), [preciseLine]);
  async Task SeedAsync(SqliteFusteRepository repository)
  {
    await repository.AddAsync(company, default);
    await repository.AddAsync(other, default);
    foreach (var agreement in agreements) await repository.AddAsync(agreement, default);
    foreach (var snapshot in new[] { result, empty, precise }) await repository.AddAsync(snapshot, default);
  }

  var storageDir = Dir("storage");
  var repo = new SqliteFusteRepository(storageDir);
  await Task.WhenAll(repo.InitializeAsync(), new SqliteFusteRepository(storageDir).InitializeAsync());
  await SeedAsync(repo);
  repo = new SqliteFusteRepository(storageDir);
  Check(Encode(await repo.ListAsync(default)) == Encode(new[] { company, other }), "Company persistence/order");
  Check(Encode(await repo.ListAgreementsForCompanyAsync(company.Id, default)) == Encode(agreements), "Agreement persistence");
  Check(Encode(await repo.FindReconciliationAsync(result.Id, default)) == Encode(result), "Snapshot persistence");
  Check(Encode(await repo.FindReconciliationAsync(precise.Id, default)) == Encode(precise), "Decimal precision, offset or deleted agreement snapshot changed");
  Check((await repo.FindReconciliationAsync(empty.Id, default))!.Lines.Count == 0, "Empty reconciliation disappeared");
  Check((await repo.FindReconciliationAsync(result.Id, default))!.Lines.Count == 53, "Showcase output count");
  Check((await repo.FindReconciliationAsync(result.Id, default))!.Lines.Sum(l => l.ExpectedCommission ?? 0) == 28662.86m, "Financial total changed");
  Check((await repo.FindReconciliationAsync(result.Id, default))!.Lines.Sum(l => l.Difference ?? 0) == 307.18m, "Difference total changed");
  Check(await Scalar(storageDir, "PRAGMA user_version;") == 3, "Schema version");
  Check(await Scalar(storageDir, "SELECT COUNT(*) FROM reconciliation_lines WHERE typeof(gross_amount)='real' OR typeof(expected_commission)='real';") == 0, "Money stored as floating point");
  Check(await Scalar(storageDir, "SELECT COUNT(*) FROM pragma_foreign_key_check;") == 0, "Foreign key violation");
  Check(await repo.DeleteAgreementAsync(other.Id, agreements[0].Id, default) == false, "Wrong-company agreement deletion");
  Check(await repo.DeleteReconciliationAsync(other.Id, result.Id, default) == false, "Wrong-company history deletion");
  Check(await repo.DeleteAgreementAsync(company.Id, agreements[0].Id, default), "Delete agreement");
  Check(Encode(await repo.FindReconciliationAsync(result.Id, default)) == Encode(result), "Deleted agreement rewrote snapshot");
  await repo.AddAsync(CommissionAgreement.Create(company.Id, agreements[0].EffectiveFrom, 6), default);
  Check(await repo.DeleteReconciliationAsync(company.Id, precise.Id, default), "Delete reconciliation");
  Check(await Scalar(storageDir, $"SELECT COUNT(*) FROM reconciliation_lines WHERE reconciliation_id='{precise.Id:D}';") == 0, "Orphan deleted lines");
  Check(await repo.DeleteCompanyAsync(company.Id, default), "Cascade company deletion");
  Check(await Scalar(storageDir, $"SELECT COUNT(*) FROM agreements WHERE company_id='{company.Id:D}';") == 0, "Orphan agreement");
  Check(await repo.FindReconciliationAsync(result.Id, default) == null, "Cascade history deletion");
  var reopened = new SqliteFusteRepository(storageDir);
  await reopened.InitializeAsync();
  Check((await reopened.ListAsync(default)).Count == 1, "Company deletion did not survive reopening");
  Check(await reopened.FindReconciliationAsync(empty.Id, default) != null, "Unrelated empty result deleted");
  await reopened.DeleteCompanyAsync(other.Id, default);
  Check((await new SqliteFusteRepository(storageDir).ListAsync(default)).Count == 0, "Empty database did not survive reopening");
  Console.WriteLine("PASS storage: full example, exact decimals, offsets, nulls, ordering, snapshots, cascades and concurrent initialization.");

  var futureDir = Dir("future-schema");
  await new SqliteFusteRepository(futureDir).InitializeAsync();
  await Execute(futureDir, "PRAGMA user_version=4;");
  await Expect<InvalidDataException>(() => new SqliteFusteRepository(futureDir).InitializeAsync());
  Check(await Scalar(futureDir, "PRAGMA user_version;") == 4, "Future schema overwritten");
  var unknownDir = Dir("unknown");
  await Execute(unknownDir, "CREATE TABLE unrelated (id INTEGER PRIMARY KEY);");
  await Expect<InvalidDataException>(() => new SqliteFusteRepository(unknownDir).InitializeAsync());
  Check(await Scalar(unknownDir, "SELECT COUNT(*) FROM sqlite_schema WHERE name='unrelated';") == 1, "Unknown database changed");
  Console.WriteLine("PASS schema checks: future schema and unrelated database protection.");

  var writeDir = Dir("writes"); var first = new SqliteFusteRepository(writeDir); var second = new SqliteFusteRepository(writeDir);
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
  var resetRepository = new SqliteFusteRepository(resetDir);
  await SeedAsync(resetRepository);
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
  var afterReset = new SqliteFusteRepository(resetDir);
  Check((await afterReset.ListAsync(default)).Count == 0, "Database was not empty after restarting from reset");
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
