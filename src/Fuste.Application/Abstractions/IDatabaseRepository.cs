namespace Fuste.Application.Abstractions;

public interface IDatabaseRepository
{
    /// <summary>Atomically removes all workspace records, preserving the schema and completed migrations.</summary>
    Task ResetAsync(CancellationToken cancellationToken);
}
