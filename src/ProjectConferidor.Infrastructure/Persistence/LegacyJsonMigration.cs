using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using ProjectConferidor.Domain.Agreements;
using ProjectConferidor.Domain.Companies;
using ProjectConferidor.Domain.Reconciliation;

namespace ProjectConferidor.Infrastructure.Persistence;

internal static class LegacyJsonMigration
{
    public static async Task ImportAsync(SqliteConnection connection, SqliteTransaction transaction, string filePath, CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath)) return;
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };
        options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        try
        {
            await using var stream = File.OpenRead(filePath);
            var data = await JsonSerializer.DeserializeAsync<LegacyWorkspace>(stream, options, cancellationToken)
                ?? throw new InvalidDataException("The legacy JSON workspace is null.");
            if (data.Companies is null || data.Agreements is null || data.Reconciliations is null)
            {
                throw new InvalidDataException("The legacy JSON workspace contains null collections.");
            }
            foreach (var company in data.Companies)
            {
                if (company is null || company.Id == Guid.Empty) throw new InvalidDataException("The legacy JSON contains an invalid company.");
                RepresentedCompany.Create(company.Name);
                await SqliteRecords.InsertCompanyAsync(connection, transaction, company, cancellationToken);
            }
            foreach (var agreement in data.Agreements)
            {
                if (agreement is null || agreement.Id == Guid.Empty || agreement.EffectiveFrom == default)
                    throw new InvalidDataException("The legacy JSON contains an invalid agreement.");
                CommissionAgreement.Create(agreement.CompanyId, agreement.EffectiveFrom, agreement.Percentage);
                await SqliteRecords.InsertAgreementAsync(connection, transaction, agreement, cancellationToken);
            }
            foreach (var reconciliation in data.Reconciliations)
            {
                if (reconciliation is null || reconciliation.Id == Guid.Empty || reconciliation.Lines is null || reconciliation.Lines.Any(line => line is null))
                    throw new InvalidDataException("The legacy JSON contains an invalid reconciliation.");
                await SqliteRecords.InsertReconciliationAsync(connection, transaction, reconciliation, cancellationToken);
            }
        }
        catch (Exception exception) when (exception is JsonException or SqliteException or ArgumentException)
        {
            throw new InvalidDataException("Legacy JSON migration failed. No imported data was committed and the original JSON file was preserved.", exception);
        }
        // Never rename, overwrite, or delete the original JSON. The schema version
        // is committed with this import and prevents replay, even after deletions.
    }

    private sealed class LegacyWorkspace
    {
        public required List<RepresentedCompany> Companies { get; init; }
        public required List<CommissionAgreement> Agreements { get; init; }
        public required List<CommissionReconciliation> Reconciliations { get; init; }
    }
}
