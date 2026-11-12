namespace ProjectConferidor.Application.Reconciliation;

public sealed record ExistingReconciliation(Guid Id, DateTimeOffset CreatedAt);
public sealed record ImportConflict(string Code, ExistingReconciliation? ExistingReconciliation = null);

public sealed class ImportConflictException(string message, ImportConflict conflict) : Exception(message)
{
    public ImportConflict Conflict { get; } = conflict;
}
