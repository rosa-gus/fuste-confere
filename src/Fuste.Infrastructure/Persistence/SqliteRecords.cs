using System.Globalization;
using Microsoft.Data.Sqlite;
using Fuste.Domain.Agreements;
using Fuste.Domain.Companies;
using Fuste.Domain.Reconciliation;

namespace Fuste.Infrastructure.Persistence;

internal static class SqliteRecords
{
    public static async Task InsertCompanyAsync(SqliteConnection connection, SqliteTransaction? transaction, RepresentedCompany company, CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand(
            "INSERT INTO companies(id, name, name_key, created_at) VALUES ($id, $name, $nameKey, $createdAt);", transaction,
            ("$id", company.Id.ToString("D")), ("$name", company.Name),
            ("$nameKey", company.Name.ToUpperInvariant()), ("$createdAt", company.CreatedAt.ToString("O", CultureInfo.InvariantCulture)));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public static async Task InsertAgreementAsync(SqliteConnection connection, SqliteTransaction? transaction, CommissionAgreement agreement, CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand(
            "INSERT INTO agreements(id, company_id, effective_from, percentage) VALUES ($id, $companyId, $effectiveFrom, $percentage);", transaction,
            ("$id", agreement.Id.ToString("D")), ("$companyId", agreement.CompanyId.ToString("D")),
            ("$effectiveFrom", agreement.EffectiveFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            ("$percentage", SqliteCommands.DecimalValue(agreement.Percentage)));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public static async Task InsertReconciliationAsync(SqliteConnection connection, SqliteTransaction transaction, CommissionReconciliation reconciliation, CancellationToken cancellationToken)
    {
        using (var header = connection.CreateCommand(
            "INSERT INTO reconciliations(id, company_id, created_at, created_at_ticks, import_configuration_json, import_fingerprint, request_id, request_intent, reprocessed_from_id) VALUES ($id, $companyId, $createdAt, $ticks, $configuration, $fingerprint, $requestId, $intent, $origin);", transaction,
            ("$id", reconciliation.Id.ToString("D")), ("$companyId", reconciliation.CompanyId.ToString("D")),
            ("$createdAt", reconciliation.CreatedAt.ToString("O", CultureInfo.InvariantCulture)), ("$ticks", reconciliation.CreatedAt.UtcTicks),
            ("$configuration", reconciliation.ImportConfiguration is null ? DBNull.Value : ImportConfigurationStorage.Serialize(reconciliation.ImportConfiguration)),
            ("$fingerprint", (object?)reconciliation.ImportFingerprint ?? DBNull.Value),
            ("$requestId", (object?)reconciliation.RequestId?.ToString("D") ?? DBNull.Value),
            ("$intent", (object?)reconciliation.RequestIntent ?? DBNull.Value),
            ("$origin", (object?)reconciliation.ReprocessedFromId?.ToString("D") ?? DBNull.Value)))
        {
            await header.ExecuteNonQueryAsync(cancellationToken);
        }
        using var command = connection.CreateCommand("""
            INSERT INTO reconciliation_lines(reconciliation_id, position, sale_external_id, status,
                sale_date, gross_amount, agreement_id, commission_percentage, expected_commission, reported_commission, difference)
            VALUES ($reconciliationId, $position, $saleId, $status, $saleDate, $grossAmount, $agreementId, $percentage, $expected, $reported, $difference);
            """, transaction);
        foreach (var name in new[] { "$reconciliationId", "$position", "$saleId", "$status", "$saleDate", "$grossAmount", "$agreementId", "$percentage", "$expected", "$reported", "$difference" })
        {
            command.Parameters.AddWithValue(name, DBNull.Value);
        }
        command.Prepare();
        for (var index = 0; index < reconciliation.Lines.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = reconciliation.Lines[index];
            command.Parameters["$reconciliationId"].Value = reconciliation.Id.ToString("D");
            command.Parameters["$position"].Value = index;
            command.Parameters["$saleId"].Value = line.SaleExternalId;
            command.Parameters["$status"].Value = line.Status.ToString();
            command.Parameters["$saleDate"].Value = (object?)line.SaleDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? DBNull.Value;
            command.Parameters["$grossAmount"].Value = SqliteCommands.DecimalValue(line.GrossAmount);
            command.Parameters["$agreementId"].Value = (object?)line.AgreementId?.ToString("D") ?? DBNull.Value;
            command.Parameters["$percentage"].Value = SqliteCommands.DecimalValue(line.CommissionPercentage);
            command.Parameters["$expected"].Value = SqliteCommands.DecimalValue(line.ExpectedCommission);
            command.Parameters["$reported"].Value = SqliteCommands.DecimalValue(line.ReportedCommission);
            command.Parameters["$difference"].Value = SqliteCommands.DecimalValue(line.Difference);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
