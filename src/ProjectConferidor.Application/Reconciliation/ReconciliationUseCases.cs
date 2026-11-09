using ProjectConferidor.Application.Abstractions;
using ProjectConferidor.Application.Common;
using ProjectConferidor.Domain.Reconciliation;
using ProjectConferidor.Domain.Imports;
using ProjectConferidor.Domain.Sales;

namespace ProjectConferidor.Application.Reconciliation;

public sealed class ReconciliationUseCases(
    ICompanyRepository companies,
    IAgreementRepository agreements,
    IReconciliationRepository reconciliations,
    ISourceFileReader fileReader)
{
    public Task<IReadOnlyList<CommissionReconciliation>> ListAsync(Guid companyId, CancellationToken cancellationToken) =>
        reconciliations.ListReconciliationsForCompanyAsync(companyId, cancellationToken);

    public Task<CommissionReconciliation?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        reconciliations.FindReconciliationAsync(id, cancellationToken);

    public async Task<OperationResult<bool>> DeleteAsync(Guid companyId, Guid id, CancellationToken cancellationToken) =>
        await reconciliations.DeleteReconciliationAsync(companyId, id, cancellationToken)
            ? OperationResult<bool>.Success(true)
            : OperationResult<bool>.Failure("Reconciliation not found.", 404);

    public Task<OperationResult<CommissionReconciliation>> RunAsync(
        Guid companyId, Stream salesFile, Stream statementFile, CancellationToken cancellationToken) =>
        RunCoreAsync(companyId, salesFile, statementFile, null, cancellationToken);

    public Task<OperationResult<CommissionReconciliation>> RunAsync(
        Guid companyId, Stream salesFile, Stream statementFile, ImportConfiguration configuration, CancellationToken cancellationToken) =>
        RunCoreAsync(companyId, salesFile, statementFile, configuration, cancellationToken);

    private async Task<OperationResult<CommissionReconciliation>> RunCoreAsync(
        Guid companyId,
        Stream salesFile,
        Stream statementFile,
        ImportConfiguration? configuration,
        CancellationToken cancellationToken)
    {
        if (await companies.FindCompanyAsync(companyId, cancellationToken) is null)
        {
            return OperationResult<CommissionReconciliation>.Failure("Company not found.", 404);
        }

        FileReadResult<SalesFileRow> salesRead;
        FileReadResult<StatementFileRow> statementRead;
        ImportConfiguration? configurationSnapshot = null;
        if (configuration is null)
        {
            // Requests without options retain their original aliases/culture behavior.
            salesRead = await fileReader.ReadSalesAsync(salesFile, cancellationToken);
            statementRead = await fileReader.ReadStatementAsync(statementFile, cancellationToken);
            var errors = salesRead.Errors.Concat(statementRead.Errors).ToList();
            if (errors.Count > 0)
                return OperationResult<CommissionReconciliation>.Failure(string.Join("\n", errors));
        }
        else
        {
            if (fileReader is not IImportFileReader configuredReader)
                return OperationResult<CommissionReconciliation>.Failure("Configured CSV imports are not supported by this reader.");
            var prepared = await configuredReader.PrepareAsync(salesFile, statementFile, configuration, cancellationToken);
            if (!prepared.Preview.IsValid)
                return OperationResult<CommissionReconciliation>.Failure(string.Join("\n", prepared.Preview.Issues.Select(issue =>
                    $"{issue.SourceKind}{(issue.Row is not null ? $" at row {issue.Row}" : "")}: {issue.Message}")), issues: prepared.Preview.Issues);
            configurationSnapshot = prepared.Preview.Configuration.Snapshot();
            configurationSnapshot = configurationSnapshot with {
                Sales = configurationSnapshot.Sales with { Delimiter = prepared.Preview.Sources.First(source => source.SourceKind == "sales").Delimiter },
                Statement = configurationSnapshot.Statement with { Delimiter = prepared.Preview.Sources.First(source => source.SourceKind == "statement").Delimiter }
            };
            salesRead = new(prepared.Sales, []);
            statementRead = new(prepared.Statement, []);
        }

        if (salesRead.Rows.Count == 0)
        {
            return OperationResult<CommissionReconciliation>.Failure("The sales file contains no records.");
        }

        var duplicateSale = salesRead.Rows
            .GroupBy(row => row.ExternalId.Trim(), StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateSale is not null)
        {
            return OperationResult<CommissionReconciliation>.Failure(
                $"The sales file contains a duplicate sale ID: {duplicateSale.Key}.");
        }

        try
        {
            var sales = salesRead.Rows.Select(row => Sale.Create(row.ExternalId, row.Date, row.GrossAmount)).ToList();
            var statement = statementRead.Rows
                .Select(row => StatementEntry.Create(row.SaleExternalId, row.CommissionAmount))
                .ToList();
            var companyAgreements = await agreements.ListAgreementsForCompanyAsync(companyId, cancellationToken);
            var result = CommissionReconciler.Reconcile(companyId, sales, statement, companyAgreements)
                with { ImportConfiguration = configurationSnapshot };
            await reconciliations.AddAsync(result, cancellationToken);
            return OperationResult<CommissionReconciliation>.Success(result);
        }
        catch (KeyNotFoundException)
        {
            return OperationResult<CommissionReconciliation>.Failure("Company not found.", 404);
        }
        catch (ArgumentException exception)
        {
            return OperationResult<CommissionReconciliation>.Failure(exception.Message);
        }
        catch (OverflowException)
        {
            return OperationResult<CommissionReconciliation>.Failure("The reconciliation exceeds the supported numeric range.");
        }
    }
}
