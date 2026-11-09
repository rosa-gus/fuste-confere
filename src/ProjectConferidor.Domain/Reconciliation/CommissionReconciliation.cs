using ProjectConferidor.Domain.Imports;

namespace ProjectConferidor.Domain.Reconciliation;

public sealed record CommissionReconciliation(
    Guid Id,
    Guid CompanyId,
    DateTimeOffset CreatedAt,
    List<ReconciliationLine> Lines)
{
    public ImportConfiguration? ImportConfiguration { get; init; }

    public static CommissionReconciliation Create(Guid companyId, IEnumerable<ReconciliationLine> lines) =>
        new(Guid.NewGuid(), companyId, DateTimeOffset.UtcNow, lines.ToList());
}
