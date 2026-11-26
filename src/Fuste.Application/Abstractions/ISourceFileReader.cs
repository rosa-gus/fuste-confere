namespace Fuste.Application.Abstractions;

public sealed record SalesFileRow(string ExternalId, DateOnly Date, decimal GrossAmount);
public sealed record StatementFileRow(string SaleExternalId, decimal CommissionAmount);
public sealed record FileReadResult<T>(IReadOnlyList<T> Rows, IReadOnlyList<string> Errors);

public interface ISourceFileReader
{
    Task<FileReadResult<SalesFileRow>> ReadSalesAsync(Stream stream, CancellationToken cancellationToken);
    Task<FileReadResult<StatementFileRow>> ReadStatementAsync(Stream stream, CancellationToken cancellationToken);
}
