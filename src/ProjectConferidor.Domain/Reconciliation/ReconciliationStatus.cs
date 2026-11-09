namespace ProjectConferidor.Domain.Reconciliation;

public enum ReconciliationStatus
{
    Matched,
    Difference,
    MissingFromStatement,
    UnrecognizedSale,
    MissingAgreement
}
