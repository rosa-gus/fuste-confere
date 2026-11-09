using ProjectConferidor.Application.Abstractions;
using ProjectConferidor.Application.Common;
using ProjectConferidor.Domain.Imports;

namespace ProjectConferidor.Application.Imports;

public sealed class ImportUseCases(ICompanyRepository companies, IImportFileReader reader)
{
    public async Task<OperationResult<ImportPreview>> PreviewAsync(Guid companyId, Stream sales, Stream statement,
        ImportConfiguration? configuration, CancellationToken cancellationToken)
    {
        if (await companies.FindCompanyAsync(companyId, cancellationToken) is null)
            return OperationResult<ImportPreview>.Failure("Company not found.", 404);
        // Missing/invalid mapping is returned as structured preview issues, never persisted.
        var prepared = await reader.PrepareAsync(sales, statement, configuration, cancellationToken);
        return OperationResult<ImportPreview>.Success(prepared.Preview);
    }
}
