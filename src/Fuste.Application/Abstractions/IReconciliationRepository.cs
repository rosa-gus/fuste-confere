using Fuste.Domain.Reconciliation;

namespace Fuste.Application.Abstractions;

public interface IReconciliationRepository
{
    Task<IReadOnlyList<CommissionReconciliation>> ListReconciliationsForCompanyAsync(Guid companyId, CancellationToken cancellationToken);
    Task<CommissionReconciliation?> FindReconciliationAsync(Guid id, CancellationToken cancellationToken);
    /// <exception cref="KeyNotFoundException">The company no longer exists at write time.</exception>
    Task AddAsync(CommissionReconciliation reconciliation, CancellationToken cancellationToken);
    Task<CommissionReconciliation> AddImportAsync(CommissionReconciliation reconciliation, CancellationToken cancellationToken);
    /// <summary>Deletes only the matching company's reconciliation, including its embedded lines.</summary>
    Task<bool> DeleteReconciliationAsync(Guid companyId, Guid id, CancellationToken cancellationToken);
}
