using Fuste.Application.Abstractions;
using Fuste.Domain.Imports;

namespace Fuste.Application.Imports;

public sealed record ImportIssue(string SourceKind, int? Row, string? Column, string? Field, string Code, string Message);
public sealed record ImportSourcePreview(string SourceKind, IReadOnlyList<string> Headers,
    IReadOnlyList<IReadOnlyList<string>> SampleRows, IReadOnlyList<Dictionary<string, string?>> NormalizedRows,
    int TotalRows, string Delimiter);
public sealed record ImportPreview(ImportConfiguration Configuration, IReadOnlyList<ImportSourcePreview> Sources,
    IReadOnlyList<ImportIssue> Issues, int TotalIssues, bool IssuesTruncated, bool IsValid, bool ValidationComplete);
public sealed record ImportPreparationResult(ImportPreview Preview,
    IReadOnlyList<SalesFileRow> Sales, IReadOnlyList<StatementFileRow> Statement);
