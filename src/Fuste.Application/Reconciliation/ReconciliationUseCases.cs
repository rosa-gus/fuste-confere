using Fuste.Application.Abstractions;
using Fuste.Application.Common;
using Fuste.Domain.Reconciliation;
using Fuste.Domain.Imports;
using Fuste.Domain.Sales;

namespace Fuste.Application.Reconciliation;

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
            : OperationResult<bool>.Failure("Conferência não encontrada. Ela pode ter sido excluída em outra sessão.", 404);

    public Task<OperationResult<CommissionReconciliation>> RunAsync(
        Guid companyId, Stream salesFile, Stream statementFile, CancellationToken cancellationToken) =>
        RunCoreAsync(companyId, salesFile, statementFile, null, cancellationToken);

    public Task<OperationResult<CommissionReconciliation>> RunAsync(
        Guid companyId, Stream salesFile, Stream statementFile, ImportConfiguration configuration, CancellationToken cancellationToken) =>
        RunCoreAsync(companyId, salesFile, statementFile, configuration, cancellationToken);

    public Task<OperationResult<CommissionReconciliation>> RunAsync(
        Guid companyId, Stream salesFile, Stream statementFile, ImportConfiguration? configuration,
        Guid? requestId, Guid? reprocessOf, CancellationToken cancellationToken) =>
        RunCoreAsync(companyId, salesFile, statementFile, configuration, cancellationToken, requestId, reprocessOf);

    private async Task<OperationResult<CommissionReconciliation>> RunCoreAsync(
        Guid companyId,
        Stream salesFile,
        Stream statementFile,
        ImportConfiguration? configuration,
        CancellationToken cancellationToken, Guid? requestId = null, Guid? reprocessOf = null)
    {
        if (await companies.FindCompanyAsync(companyId, cancellationToken) is null)
        {
            return OperationResult<CommissionReconciliation>.Failure("Representada não encontrada.", 404);
        }

        // Keep the exact received bytes for hashing and support non-seekable readers.
        using var salesBytes = new MemoryStream();
        using var statementBytes = new MemoryStream();
        const int maximumBytes = 9 * 1024 * 1024;
        async Task<bool> CopyBoundedAsync(Stream source, MemoryStream target, long remaining)
        {
            var buffer = new byte[81920];
            int count;
            while ((count = await source.ReadAsync(buffer, cancellationToken)) > 0)
            {
                if (target.Length + count > remaining) return false;
                await target.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
            }
            target.Position = 0;
            return true;
        }
        if (!await CopyBoundedAsync(salesFile, salesBytes, maximumBytes)
            || !await CopyBoundedAsync(statementFile, statementBytes, maximumBytes - salesBytes.Length))
            return OperationResult<CommissionReconciliation>.Failure("Os dois arquivos devem ter, juntos, no máximo 9 MB.", 413);
        salesFile = salesBytes;
        statementFile = statementBytes;

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
                return OperationResult<CommissionReconciliation>.Failure("Este leitor não oferece suporte à importação de CSV com configuração personalizada.");
            var prepared = await configuredReader.PrepareAsync(salesFile, statementFile, configuration, cancellationToken);
            if (!prepared.Preview.IsValid)
                return OperationResult<CommissionReconciliation>.Failure(string.Join("\n", prepared.Preview.Issues.Select(issue =>
                    $"{(issue.SourceKind == "sales" ? "Vendas" : issue.SourceKind == "statement" ? "Comissões" : "Configuração")}{(issue.Row is not null ? $", linha {issue.Row}" : "")}: {issue.Message}")), issues: prepared.Preview.Issues);
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
            return OperationResult<CommissionReconciliation>.Failure("O arquivo de vendas não contém registros.");
        }

        var duplicateSale = salesRead.Rows
            .GroupBy(row => row.ExternalId.Trim(), StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateSale is not null)
        {
            return OperationResult<CommissionReconciliation>.Failure(
                $"O arquivo de vendas repete o identificador {duplicateSale.Key}.");
        }

        try
        {
            var sales = salesRead.Rows.Select(row => Sale.Create(row.ExternalId, row.Date, row.GrossAmount)).ToList();
            var statement = statementRead.Rows
                .Select(row => StatementEntry.Create(row.SaleExternalId, row.CommissionAmount))
                .ToList();
            var companyAgreements = await agreements.ListAgreementsForCompanyAsync(companyId, cancellationToken);
            var result = CommissionReconciler.Reconcile(companyId, sales, statement, companyAgreements)
                with {
                    ImportConfiguration = configurationSnapshot,
                    ImportFingerprint = ImportFingerprint.Create(salesBytes.GetBuffer().AsSpan(0, (int)salesBytes.Length),
                        statementBytes.GetBuffer().AsSpan(0, (int)statementBytes.Length), configurationSnapshot),
                    RequestId = requestId, ReprocessedFromId = reprocessOf,
                    RequestIntent = reprocessOf?.ToString("D") ?? "initial"
                };
            return OperationResult<CommissionReconciliation>.Success(await reconciliations.AddImportAsync(result, cancellationToken));
        }
        catch (ImportConflictException exception)
        {
            return OperationResult<CommissionReconciliation>.Failure(exception.Message, 409) with { Conflict = exception.Conflict };
        }
        catch (KeyNotFoundException)
        {
            return OperationResult<CommissionReconciliation>.Failure("Representada não encontrada.", 404);
        }
        catch (ArgumentException exception)
        {
            return OperationResult<CommissionReconciliation>.Failure(exception.Message);
        }
        catch (OverflowException)
        {
            return OperationResult<CommissionReconciliation>.Failure("Não foi possível calcular a conferência: os valores excedem o limite numérico suportado.");
        }
    }
}
