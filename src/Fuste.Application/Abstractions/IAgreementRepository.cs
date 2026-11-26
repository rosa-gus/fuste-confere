using Fuste.Domain.Agreements;

namespace Fuste.Application.Abstractions;

public interface IAgreementRepository
{
    Task<IReadOnlyList<CommissionAgreement>> ListAgreementsForCompanyAsync(Guid companyId, CancellationToken cancellationToken);
    /// <exception cref="KeyNotFoundException">The company no longer exists at write time.</exception>
    /// <exception cref="RepositoryConflictException">An agreement with the same company and effective date already exists at write time.</exception>
    Task AddAsync(CommissionAgreement agreement, CancellationToken cancellationToken);
    /// <summary>Deletes only the matching company's agreement; saved reconciliation snapshots are preserved.</summary>
    Task<bool> DeleteAgreementAsync(Guid companyId, Guid id, CancellationToken cancellationToken);
}
