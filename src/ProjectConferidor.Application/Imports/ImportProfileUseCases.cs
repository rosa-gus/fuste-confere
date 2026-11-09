using ProjectConferidor.Application.Abstractions;
using ProjectConferidor.Application.Common;
using ProjectConferidor.Domain.Imports;

namespace ProjectConferidor.Application.Imports;

public sealed class ImportProfileUseCases(ICompanyRepository companies, IImportProfileRepository profiles)
{
    public async Task<OperationResult<IReadOnlyList<ImportProfile>>> ListAsync(Guid companyId, CancellationToken cancellationToken) =>
        await companies.FindCompanyAsync(companyId, cancellationToken) is null
            ? OperationResult<IReadOnlyList<ImportProfile>>.Failure("Company not found.", 404)
            : OperationResult<IReadOnlyList<ImportProfile>>.Success(await profiles.ListImportProfilesAsync(companyId, cancellationToken));

    public async Task<OperationResult<ImportProfile>> CreateAsync(Guid companyId, string name,
        ImportConfiguration configuration, CancellationToken cancellationToken)
    {
        if (await companies.FindCompanyAsync(companyId, cancellationToken) is null)
            return OperationResult<ImportProfile>.Failure("Company not found.", 404);
        var error = Validate(name, configuration);
        if (error is not null) return OperationResult<ImportProfile>.Failure(error);
        var now = DateTimeOffset.UtcNow;
        var profile = new ImportProfile(Guid.NewGuid(), companyId, name.Trim(), 1, configuration.Snapshot(), now, now);
        try
        {
            await profiles.AddAsync(profile, cancellationToken);
            return OperationResult<ImportProfile>.Success(profile);
        }
        catch (RepositoryConflictException exception) { return OperationResult<ImportProfile>.Failure(exception.Message, 409); }
        catch (KeyNotFoundException) { return OperationResult<ImportProfile>.Failure("Company not found.", 404); }
    }

    public async Task<OperationResult<ImportProfile>> UpdateAsync(Guid companyId, Guid id, string name,
        int expectedRevision, ImportConfiguration configuration, CancellationToken cancellationToken)
    {
        var existing = await profiles.FindImportProfileAsync(companyId, id, cancellationToken);
        if (existing is null) return OperationResult<ImportProfile>.Failure("Import profile not found.", 404);
        if (expectedRevision < 1 || existing.Revision != expectedRevision)
            return OperationResult<ImportProfile>.Failure("The import profile was changed. Reload it before saving.", 409);
        if (expectedRevision == int.MaxValue) return OperationResult<ImportProfile>.Failure("Import profile revision limit reached.", 409);
        var error = Validate(name, configuration);
        if (error is not null) return OperationResult<ImportProfile>.Failure(error);
        var updated = existing with
        {
            Name = name.Trim(),
            Revision = expectedRevision + 1,
            Configuration = configuration.Snapshot(),
            UpdatedAt = DateTimeOffset.UtcNow
        };
        try
        {
            return await profiles.UpdateAsync(updated, expectedRevision, cancellationToken)
                ? OperationResult<ImportProfile>.Success(updated)
                : OperationResult<ImportProfile>.Failure("The import profile was changed or deleted. Reload it before saving.", 409);
        }
        catch (RepositoryConflictException exception) { return OperationResult<ImportProfile>.Failure(exception.Message, 409); }
    }

    public async Task<OperationResult<bool>> DeleteAsync(Guid companyId, Guid id, CancellationToken cancellationToken) =>
        await profiles.DeleteImportProfileAsync(companyId, id, cancellationToken)
            ? OperationResult<bool>.Success(true)
            : OperationResult<bool>.Failure("Import profile not found.", 404);

    private static string? Validate(string name, ImportConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 160) return "Import profile name must contain between 1 and 160 characters.";
        var errors = ImportConfiguration.Validate(configuration);
        return errors.Count > 0 ? string.Join("\n", errors) : null;
    }
}
