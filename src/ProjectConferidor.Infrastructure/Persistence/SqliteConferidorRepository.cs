using System.Globalization;
using Microsoft.Data.Sqlite;
using ProjectConferidor.Application.Abstractions;
using ProjectConferidor.Domain.Agreements;
using ProjectConferidor.Domain.Companies;
using ProjectConferidor.Domain.Reconciliation;
using ProjectConferidor.Application.Reconciliation;

namespace ProjectConferidor.Infrastructure.Persistence;

public sealed partial class SqliteConferidorRepository : ICompanyRepository, IAgreementRepository, IReconciliationRepository, IDatabaseRepository, IImportProfileRepository
{
    private readonly ConferidorDatabase _database;

    public SqliteConferidorRepository(string? dataDirectory = null) => _database = new ConferidorDatabase(dataDirectory);

    public Task InitializeAsync(CancellationToken cancellationToken = default) => _database.InitializeAsync(cancellationToken);

    public async Task ResetAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        // One atomic statement cascades to agreements, reconciliations and lines.
        // Keep the schema marker so the retained legacy JSON is never reimported.
        using var command = connection.CreateCommand("DELETE FROM companies;");
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<RepresentedCompany>> ListAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        using var command = connection.CreateCommand("SELECT id, name, created_at FROM companies ORDER BY rowid;");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var companies = new List<RepresentedCompany>();
        while (await reader.ReadAsync(cancellationToken)) companies.Add(ReadCompany(reader));
        return companies;
    }

    public async Task<RepresentedCompany?> FindCompanyAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        using var command = connection.CreateCommand("SELECT id, name, created_at FROM companies WHERE id = $id;", null, ("$id", id.ToString("D")));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadCompany(reader) : null;
    }

    public async Task AddAsync(RepresentedCompany company, CancellationToken cancellationToken)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        try
        {
            await SqliteRecords.InsertCompanyAsync(connection, null, company, cancellationToken);
        }
        catch (SqliteException exception) when (exception.SqliteExtendedErrorCode == 2067)
        {
            throw new RepositoryConflictException("Já existe uma representada com esse nome.", exception);
        }
    }

    public Task<bool> DeleteCompanyAsync(Guid id, CancellationToken cancellationToken) =>
        DeleteAsync("DELETE FROM companies WHERE id = $id;", id, null, cancellationToken);

    public async Task<IReadOnlyList<CommissionAgreement>> ListAgreementsForCompanyAsync(Guid companyId, CancellationToken cancellationToken)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        using var command = connection.CreateCommand(
            "SELECT id, company_id, effective_from, percentage FROM agreements WHERE company_id = $id ORDER BY effective_from, rowid;", null, ("$id", companyId.ToString("D")));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var agreements = new List<CommissionAgreement>();
        while (await reader.ReadAsync(cancellationToken))
        {
            agreements.Add(new CommissionAgreement(Guid.Parse(reader.GetString(0)), Guid.Parse(reader.GetString(1)),
                DateOnly.ParseExact(reader.GetString(2), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                decimal.Parse(reader.GetString(3), CultureInfo.InvariantCulture)));
        }
        return agreements;
    }

    public async Task AddAsync(CommissionAgreement agreement, CancellationToken cancellationToken)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        try
        {
            await SqliteRecords.InsertAgreementAsync(connection, null, agreement, cancellationToken);
        }
        catch (SqliteException exception) when (exception.SqliteExtendedErrorCode == 787)
        {
            throw new KeyNotFoundException("Representada não encontrada.", exception);
        }
        catch (SqliteException exception) when (exception.SqliteExtendedErrorCode == 2067)
        {
            throw new RepositoryConflictException("Já existe um acordo com essa data de início.", exception);
        }
    }

    public Task<bool> DeleteAgreementAsync(Guid companyId, Guid id, CancellationToken cancellationToken) =>
        DeleteAsync("DELETE FROM agreements WHERE id = $id AND company_id = $companyId;", id, companyId, cancellationToken);

    public Task<IReadOnlyList<CommissionReconciliation>> ListReconciliationsForCompanyAsync(Guid companyId, CancellationToken cancellationToken) =>
        ReadReconciliationsAsync(companyId, byCompany: true, cancellationToken);

    public async Task<CommissionReconciliation?> FindReconciliationAsync(Guid id, CancellationToken cancellationToken) =>
        (await ReadReconciliationsAsync(id, byCompany: false, cancellationToken)).FirstOrDefault();

    public async Task AddAsync(CommissionReconciliation reconciliation, CancellationToken cancellationToken)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();
        try
        {
            await SqliteRecords.InsertReconciliationAsync(connection, transaction, reconciliation, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            await transaction.CommitAsync(cancellationToken);
        }
        catch (SqliteException exception) when (exception.SqliteExtendedErrorCode == 787)
        {
            throw new KeyNotFoundException("Representada não encontrada.", exception);
        }
    }

    public Task<bool> DeleteReconciliationAsync(Guid companyId, Guid id, CancellationToken cancellationToken) =>
        DeleteAsync("DELETE FROM reconciliations WHERE id = $id AND company_id = $companyId;", id, companyId, cancellationToken);

    private async Task<bool> DeleteAsync(string sql, Guid id, Guid? companyId, CancellationToken cancellationToken)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        using var command = connection.CreateCommand(sql, null, ("$id", id.ToString("D")));
        if (companyId.HasValue) command.Parameters.AddWithValue("$companyId", companyId.Value.ToString("D"));
        // A single DELETE is atomic; SQLite cascades dependent rows in that statement.
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    private async Task<IReadOnlyList<CommissionReconciliation>> ReadReconciliationsAsync(Guid id, bool byCompany, CancellationToken cancellationToken)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        return await ReadReconciliationsAsync(connection, null, id, byCompany, cancellationToken);
    }

    private static async Task<IReadOnlyList<CommissionReconciliation>> ReadReconciliationsAsync(
        SqliteConnection connection, SqliteTransaction? transaction, Guid id, bool byCompany, CancellationToken cancellationToken)
    {
        // Both predicates are fixed SQL. Only the UUID is supplied by the caller.
        var predicate = byCompany ? "r.company_id = $id" : "r.id = $id";
        using var command = connection.CreateCommand($"""
            SELECT r.id, r.company_id, r.created_at, l.position, l.sale_external_id, l.status,
                l.sale_date, l.gross_amount, l.agreement_id, l.commission_percentage,
                l.expected_commission, l.reported_commission, l.difference, r.import_configuration_json, r.import_fingerprint, r.request_id, r.request_intent, r.reprocessed_from_id
            FROM reconciliations r
            LEFT JOIN reconciliation_lines l ON l.reconciliation_id = r.id
            WHERE {predicate}
            ORDER BY r.created_at_ticks DESC, r.rowid, l.position;
            """, transaction, ("$id", id.ToString("D")));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var reconciliations = new List<CommissionReconciliation>();
        CommissionReconciliation? current = null;
        while (await reader.ReadAsync(cancellationToken))
        {
            var reconciliationId = Guid.Parse(reader.GetString(0));
            if (current?.Id != reconciliationId)
            {
                current = new CommissionReconciliation(reconciliationId, Guid.Parse(reader.GetString(1)),
                    DateTimeOffset.Parse(reader.GetString(2), CultureInfo.InvariantCulture), [])
                    {
                        ImportConfiguration = reader.IsDBNull(13) ? null : ImportConfigurationStorage.Deserialize(reader.GetString(13)),
                        ImportFingerprint = reader.IsDBNull(14) ? null : reader.GetString(14),
                        RequestId = reader.IsDBNull(15) ? null : Guid.Parse(reader.GetString(15)),
                        RequestIntent = reader.IsDBNull(16) ? null : reader.GetString(16),
                        ReprocessedFromId = reader.IsDBNull(17) ? null : Guid.Parse(reader.GetString(17))
                    };
                reconciliations.Add(current);
            }
            if (!reader.IsDBNull(3))
            {
                current!.Lines.Add(new ReconciliationLine(
                    reader.GetString(4), Enum.Parse<ReconciliationStatus>(reader.GetString(5)),
                    reader.IsDBNull(6) ? null : DateOnly.ParseExact(reader.GetString(6), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                    SqliteCommands.ReadDecimal(reader, 7), reader.IsDBNull(8) ? null : Guid.Parse(reader.GetString(8)),
                    SqliteCommands.ReadDecimal(reader, 9), SqliteCommands.ReadDecimal(reader, 10),
                    SqliteCommands.ReadDecimal(reader, 11), SqliteCommands.ReadDecimal(reader, 12)));
            }
        }
        return reconciliations;
    }

    private static RepresentedCompany ReadCompany(SqliteDataReader reader) => new(
        Guid.Parse(reader.GetString(0)), reader.GetString(1), DateTimeOffset.Parse(reader.GetString(2), CultureInfo.InvariantCulture));
}
