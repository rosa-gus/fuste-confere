using Fuste.Domain.Imports;

namespace Fuste.Application.Abstractions;

public interface IImportProfileRepository
{
    Task<IReadOnlyList<ImportProfile>> ListImportProfilesAsync(Guid companyId, CancellationToken cancellationToken);
    Task<ImportProfile?> FindImportProfileAsync(Guid companyId, Guid id, CancellationToken cancellationToken);
    Task AddAsync(ImportProfile profile, CancellationToken cancellationToken);
    // The update must compare revision and increment it atomically.
    Task<bool> UpdateAsync(ImportProfile profile, int expectedRevision, CancellationToken cancellationToken);
    Task<bool> DeleteImportProfileAsync(Guid companyId, Guid id, CancellationToken cancellationToken);
}
