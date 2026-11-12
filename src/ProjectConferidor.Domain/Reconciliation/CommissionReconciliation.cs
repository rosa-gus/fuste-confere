using ProjectConferidor.Domain.Imports;

namespace ProjectConferidor.Domain.Reconciliation;

public sealed record CommissionReconciliation(
    Guid Id,
    Guid CompanyId,
    DateTimeOffset CreatedAt,
    List<ReconciliationLine> Lines)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public string? ImportFingerprint { get; init; }
    [System.Text.Json.Serialization.JsonIgnore]
    public Guid? RequestId { get; init; }
    [System.Text.Json.Serialization.JsonIgnore]
    public string? RequestIntent { get; init; }
    public Guid? ReprocessedFromId { get; init; }
    public ImportConfiguration? ImportConfiguration { get; init; }

    public static CommissionReconciliation Create(Guid companyId, IEnumerable<ReconciliationLine> lines) =>
        new(Guid.NewGuid(), companyId, DateTimeOffset.UtcNow, lines.ToList());
}
