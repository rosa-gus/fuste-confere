using Fuste.Application.Imports;

namespace Fuste.Application.Common;

public sealed record OperationResult<T>(bool IsSuccess, T? Value, string? Error, int StatusCode, IReadOnlyList<ImportIssue>? Issues = null)
{
    public Fuste.Application.Reconciliation.ImportConflict? Conflict { get; init; }
    public static OperationResult<T> Success(T value) => new(true, value, null, 200);
    public static OperationResult<T> Failure(string error, int statusCode = 400, IReadOnlyList<ImportIssue>? issues = null) => new(false, default, error, statusCode, issues);
}
