using ProjectConferidor.Domain.Companies;

namespace ProjectConferidor.Application.Abstractions;

public interface ICompanyRepository
{
    Task<IReadOnlyList<RepresentedCompany>> ListAsync(CancellationToken cancellationToken);
    Task<RepresentedCompany?> FindCompanyAsync(Guid id, CancellationToken cancellationToken);
    /// <exception cref="RepositoryConflictException">A company with the same case-insensitive name already exists at write time.</exception>
    Task AddAsync(RepresentedCompany company, CancellationToken cancellationToken);
    /// <summary>Atomically deletes the company and its agreements and reconciliations. Returns false if it does not exist.</summary>
    Task<bool> DeleteCompanyAsync(Guid id, CancellationToken cancellationToken);
}
