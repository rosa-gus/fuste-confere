using System.Globalization;
using Microsoft.Data.Sqlite;
using Fuste.Application.Reconciliation;
using Fuste.Domain.Reconciliation;

namespace Fuste.Infrastructure.Persistence;

public sealed partial class SqliteFusteRepository
{
    public async Task<CommissionReconciliation> AddImportAsync(CommissionReconciliation result, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(result.ImportFingerprint) || string.IsNullOrEmpty(result.RequestIntent))
            throw new ArgumentException("Import identification is required.");
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        // BeginTransaction is immediate: the lookup and write share the same writer lock,
        // including when another process or repository instance receives the same import.
        using var transaction = connection.BeginTransaction();
        if (result.RequestId is not null)
        {
            using var request = connection.CreateCommand("""
                SELECT id, import_fingerprint, request_intent FROM reconciliations
                WHERE company_id = $company AND request_id = $request;
                """, transaction, ("$company", result.CompanyId.ToString("D")), ("$request", result.RequestId.Value.ToString("D")));
            Guid? previousId = null;
            await using (var reader = await request.ExecuteReaderAsync(cancellationToken))
            {
                if (await reader.ReadAsync(cancellationToken))
                {
                    if (reader.GetString(1) != result.ImportFingerprint || reader.GetString(2) != result.RequestIntent)
                        throw Conflict("request_id_conflict", "Esta tentativa já foi usada em outra operação. Inicie uma nova conferência.");
                    previousId = Guid.Parse(reader.GetString(0));
                }
            }
            if (previousId is not null)
                return (await ReadReconciliationsAsync(connection, transaction, previousId.Value, false, cancellationToken)).Single();
        }

        if (result.ReprocessedFromId is not null)
        {
            using var origin = connection.CreateCommand("""
                SELECT COUNT(*) FROM reconciliations WHERE id = $origin AND company_id = $company AND import_fingerprint = $fingerprint;
                """, transaction, ("$origin", result.ReprocessedFromId.Value.ToString("D")),
                ("$company", result.CompanyId.ToString("D")), ("$fingerprint", result.ImportFingerprint));
            if (Convert.ToInt64(await origin.ExecuteScalarAsync(cancellationToken)) != 1)
                throw Conflict("invalid_reprocess_reference", "A conferência anterior não está mais disponível ou não corresponde aos arquivos. Envie os arquivos novamente.");
        }
        else
        {
            using var duplicate = connection.CreateCommand("""
                SELECT id, created_at FROM reconciliations
                WHERE company_id = $company AND import_fingerprint = $fingerprint
                ORDER BY created_at_ticks DESC, rowid DESC LIMIT 1;
                """, transaction, ("$company", result.CompanyId.ToString("D")), ("$fingerprint", result.ImportFingerprint));
            await using var reader = await duplicate.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
                throw new ImportConflictException("Estes arquivos já foram conferidos para esta representada.", new("duplicate_import",
                    new(Guid.Parse(reader.GetString(0)), DateTimeOffset.Parse(reader.GetString(1), CultureInfo.InvariantCulture))));
        }

        try
        {
            await SqliteRecords.InsertReconciliationAsync(connection, transaction, result, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (SqliteException exception) when (exception.SqliteExtendedErrorCode == 787)
        {
            throw new KeyNotFoundException("Representada não encontrada.", exception);
        }
    }

    private static ImportConflictException Conflict(string code, string message) => new(message, new(code));
}
