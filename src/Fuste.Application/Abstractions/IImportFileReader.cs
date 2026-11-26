using Fuste.Application.Imports;
using Fuste.Domain.Imports;

namespace Fuste.Application.Abstractions;

public interface IImportFileReader
{
    Task<ImportPreparationResult> PrepareAsync(Stream sales, Stream statement, ImportConfiguration? configuration,
        CancellationToken cancellationToken);
}
