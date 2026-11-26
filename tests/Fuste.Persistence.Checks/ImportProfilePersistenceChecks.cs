using System.Text.Json;
using Fuste.Application.Imports;
using Fuste.Application.Reconciliation;
using Fuste.Domain.Agreements;
using Fuste.Domain.Companies;
using Fuste.Domain.Reconciliation;
using Fuste.Infrastructure.Imports;
using Fuste.Infrastructure.Persistence;

internal static class ImportProfilePersistenceChecks
{
    public static async Task RunAsync(string directory)
    {
        var repository = new SqliteFusteRepository(directory);
        var firstCompany = RepresentedCompany.Create("CSV profile A");
        var secondCompany = RepresentedCompany.Create("CSV profile B");
        await repository.AddAsync(firstCompany, default);
        await repository.AddAsync(secondCompany, default);
        await repository.AddAsync(CommissionAgreement.Create(firstCompany.Id, new DateOnly(2026, 1, 1), 5), default);
        var profiles = new ImportProfileUseCases(repository, repository);
        var configuration = CsvImportProfileChecks.Configuration();
        var created = await profiles.CreateAsync(firstCompany.Id, "  ERP export  ", configuration, default);
        Check(created.IsSuccess && created.Value is not null, "Could not create import profile.");
        var profile = created.Value!;
        Check(profile.Revision == 1 && profile.Name == "ERP export", "New profile name/revision is incorrect.");
        configuration.Sales.Columns["externalId"] = "MUTATED";
        var reloaded = new SqliteFusteRepository(directory);
        var persisted = await reloaded.FindImportProfileAsync(firstCompany.Id, profile.Id, default);
        Check(persisted is not null && persisted.Configuration.Sales.Columns["externalId"] == "Documento",
            "Persisted profile changed after its caller mutated the input dictionary.");
        Check(await reloaded.FindImportProfileAsync(secondCompany.Id, profile.Id, default) is null,
            "Profile lookup crossed company ownership.");
        var foreignUpdate = await profiles.UpdateAsync(secondCompany.Id, profile.Id, "Hacked", 1, CsvImportProfileChecks.Configuration(), default);
        var foreignDelete = await profiles.DeleteAsync(secondCompany.Id, profile.Id, default);
        Check(!foreignUpdate.IsSuccess && foreignUpdate.StatusCode == 404 && !foreignDelete.IsSuccess && foreignDelete.StatusCode == 404,
            "Profile update/delete crossed company ownership.");
        Check((await profiles.ListAsync(secondCompany.Id, default)).Value!.Count == 0, "Company profile list leaked another company's profile.");
        var foreignCreate = await profiles.CreateAsync(Guid.NewGuid(), "Unknown owner", CsvImportProfileChecks.Configuration(), default);
        Check(!foreignCreate.IsSuccess && foreignCreate.StatusCode == 404, "Profile creation accepted an unknown company.");

        var updated = await profiles.UpdateAsync(firstCompany.Id, profile.Id, "ERP export v2", profile.Revision, CsvImportProfileChecks.Configuration(), default);
        Check(updated.IsSuccess && updated.Value!.Revision == 2, "Profile update did not advance revision.");
        var stale = await profiles.UpdateAsync(firstCompany.Id, profile.Id, "Stale overwrite", profile.Revision, CsvImportProfileChecks.Configuration(), default);
        Check(!stale.IsSuccess && stale.StatusCode == 409, "Stale profile update overwrote a newer revision.");
        Check((await repository.FindImportProfileAsync(firstCompany.Id, profile.Id, default))!.Name == "ERP export v2",
            "Rejected stale update changed the persisted profile.");

        var run = new ReconciliationUseCases(repository, repository, repository, new CsvSourceFileReader());
        var previewUseCases = new ImportUseCases(repository, new CsvSourceFileReader());
        using (var sales = CsvImportProfileChecks.Input(CsvImportProfileChecks.ExternalSales))
        using (var statement = CsvImportProfileChecks.Input(CsvImportProfileChecks.ExternalStatement))
        {
            var preview = await previewUseCases.PreviewAsync(firstCompany.Id, sales, statement, updated.Value!.Configuration, default);
            Check(preview.IsSuccess && preview.Value!.IsValid, "Valid mapped files did not produce a valid preview.");
            Check((await repository.ListReconciliationsForCompanyAsync(firstCompany.Id, default)).Count == 0,
                "Preview saved a reconciliation.");
        }
        Guid reconciliationId;
        string snapshot;
        using (var sales = CsvImportProfileChecks.Input(CsvImportProfileChecks.ExternalSales))
        using (var statement = CsvImportProfileChecks.Input(CsvImportProfileChecks.ExternalStatement))
        {
            var result = await run.RunAsync(firstCompany.Id, sales, statement, updated.Value!.Configuration, default);
            Check(result.IsSuccess && result.Value is not null, "Configured import did not reconcile.");
            var line = result.Value!.Lines.Single();
            Check(line.Status == ReconciliationStatus.Matched && line.ExpectedCommission == 62.53m && line.SaleExternalId == "000123",
                "Configured import changed the established financial calculation.");
            Check(result.Value.ImportConfiguration is not null, "Reconciliation did not snapshot its import configuration.");
            reconciliationId = result.Value.Id;
            snapshot = JsonSerializer.Serialize(result.Value.ImportConfiguration);
        }
        Check((await profiles.DeleteAsync(firstCompany.Id, profile.Id, default)).IsSuccess, "Profile could not be deleted.");
        var historical = await new SqliteFusteRepository(directory).FindReconciliationAsync(reconciliationId, default);
        Check(historical is not null && JsonSerializer.Serialize(historical.ImportConfiguration) == snapshot,
            "Deleting profile changed or removed the reconciliation's import snapshot.");

        using (var sales = CsvImportProfileChecks.Input(CsvImportProfileChecks.ExternalSales.Replace("1250.50", "invalid")))
        using (var statement = CsvImportProfileChecks.Input(CsvImportProfileChecks.ExternalStatement))
        {
            var failure = await run.RunAsync(firstCompany.Id, sales, statement, CsvImportProfileChecks.Configuration(), default);
            Check(!failure.IsSuccess && (await repository.ListReconciliationsForCompanyAsync(firstCompany.Id, default)).Count == 1,
                "Failed configured import saved a partial reconciliation.");
        }
        var cascading = await profiles.CreateAsync(firstCompany.Id, "Cascade", CsvImportProfileChecks.Configuration(), default);
        await repository.DeleteCompanyAsync(firstCompany.Id, default);
        Check((await repository.ListImportProfilesAsync(firstCompany.Id, default)).Count == 0, "Company deletion left orphan profiles.");
        var resetProfile = await profiles.CreateAsync(secondCompany.Id, "Reset", CsvImportProfileChecks.Configuration(), default);
        Check(cascading.IsSuccess && resetProfile.IsSuccess, "Cascade/reset fixture profiles could not be created.");
        await repository.ResetAsync(default);
        Check((await repository.ListImportProfilesAsync(secondCompany.Id, default)).Count == 0, "Database reset left import profiles.");
        Console.WriteLine("PASS import profile persistence: revisions, ownership, immutable input, preview without writes, configured calculation, snapshots, no partial saves, cascades and reset.");
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
