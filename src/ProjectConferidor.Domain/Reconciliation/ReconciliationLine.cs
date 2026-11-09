namespace ProjectConferidor.Domain.Reconciliation;

public sealed record ReconciliationLine(
    string SaleExternalId,
    ReconciliationStatus Status,
    DateOnly? SaleDate,
    decimal? GrossAmount,
    Guid? AgreementId,
    decimal? CommissionPercentage,
    decimal? ExpectedCommission,
    decimal? ReportedCommission,
    decimal? Difference);
