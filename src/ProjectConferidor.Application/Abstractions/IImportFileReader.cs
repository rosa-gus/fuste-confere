using ProjectConferidor.Application.Imports;
using ProjectConferidor.Domain.Imports;

namespace ProjectConferidor.Application.Abstractions;

public interface IImportFileReader
{
    Task<ImportPreparationResult> PrepareAsync(Stream sales, Stream statement, ImportConfiguration? configuration,
        CancellationToken cancellationToken);
}
