using System.Globalization;
using Microsoft.Data.Sqlite;
using Fuste.Application.Abstractions;
using Fuste.Domain.Imports;

namespace Fuste.Infrastructure.Persistence;

public sealed partial class SqliteFusteRepository
{
    private const string ProfileColumns = "id, company_id, name, revision, configuration_json, created_at, updated_at";

    public async Task<IReadOnlyList<ImportProfile>> ListImportProfilesAsync(Guid companyId, CancellationToken cancellationToken)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        using var command = connection.CreateCommand($"SELECT {ProfileColumns} FROM import_profiles WHERE company_id = $companyId ORDER BY name_key, id;",
            null, ("$companyId", companyId.ToString("D")));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var profiles = new List<ImportProfile>();
        while (await reader.ReadAsync(cancellationToken)) profiles.Add(ReadProfile(reader));
        return profiles;
    }

    public async Task<ImportProfile?> FindImportProfileAsync(Guid companyId, Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        using var command = connection.CreateCommand($"SELECT {ProfileColumns} FROM import_profiles WHERE id = $id AND company_id = $companyId;",
            null, ("$id", id.ToString("D")), ("$companyId", companyId.ToString("D")));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadProfile(reader) : null;
    }

    public async Task AddAsync(ImportProfile profile, CancellationToken cancellationToken)
    {
        var configuration = ImportConfigurationStorage.Serialize(profile.Configuration);
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        using var command = connection.CreateCommand("""
            INSERT INTO import_profiles(id, company_id, name, name_key, revision, configuration_json, created_at, updated_at)
            VALUES ($id, $companyId, $name, $nameKey, $revision, $configuration, $createdAt, $updatedAt);
            """, null, ("$id", profile.Id.ToString("D")), ("$companyId", profile.CompanyId.ToString("D")),
            ("$name", profile.Name), ("$nameKey", profile.Name.ToUpperInvariant()), ("$revision", profile.Revision),
            ("$configuration", configuration), ("$createdAt", profile.CreatedAt.ToString("O", CultureInfo.InvariantCulture)),
            ("$updatedAt", profile.UpdatedAt.ToString("O", CultureInfo.InvariantCulture)));
        try { await command.ExecuteNonQueryAsync(cancellationToken); }
        catch (SqliteException exception) when (exception.SqliteExtendedErrorCode == 787)
        { throw new KeyNotFoundException("Representada não encontrada.", exception); }
        catch (SqliteException exception) when (exception.SqliteExtendedErrorCode == 2067)
        { throw new RepositoryConflictException("Já existe um perfil de importação com esse nome.", exception); }
    }

    public async Task<bool> UpdateAsync(ImportProfile profile, int expectedRevision, CancellationToken cancellationToken)
    {
        if (expectedRevision < 1 || expectedRevision == int.MaxValue || profile.Revision != expectedRevision + 1)
            throw new ArgumentException("The updated import profile revision is invalid.");
        var configuration = ImportConfigurationStorage.Serialize(profile.Configuration);
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        using var command = connection.CreateCommand("""
            UPDATE import_profiles SET name=$name, name_key=$nameKey, revision=$nextRevision,
                configuration_json=$configuration, updated_at=$updatedAt
            WHERE id=$id AND company_id=$companyId AND revision=$expectedRevision;
            """, null, ("$id", profile.Id.ToString("D")), ("$companyId", profile.CompanyId.ToString("D")),
            ("$name", profile.Name), ("$nameKey", profile.Name.ToUpperInvariant()), ("$nextRevision", profile.Revision),
            ("$expectedRevision", expectedRevision), ("$configuration", configuration),
            ("$updatedAt", profile.UpdatedAt.ToString("O", CultureInfo.InvariantCulture)));
        try { return await command.ExecuteNonQueryAsync(cancellationToken) > 0; }
        catch (SqliteException exception) when (exception.SqliteExtendedErrorCode == 2067)
        { throw new RepositoryConflictException("Já existe um perfil de importação com esse nome.", exception); }
    }

    public Task<bool> DeleteImportProfileAsync(Guid companyId, Guid id, CancellationToken cancellationToken) =>
        DeleteAsync("DELETE FROM import_profiles WHERE id=$id AND company_id=$companyId;", id, companyId, cancellationToken);

    private static ImportProfile ReadProfile(SqliteDataReader reader) => new(Guid.Parse(reader.GetString(0)),
        Guid.Parse(reader.GetString(1)), reader.GetString(2), reader.GetInt32(3),
        ImportConfigurationStorage.Deserialize(reader.GetString(4)),
        DateTimeOffset.Parse(reader.GetString(5), CultureInfo.InvariantCulture),
        DateTimeOffset.Parse(reader.GetString(6), CultureInfo.InvariantCulture));
}
