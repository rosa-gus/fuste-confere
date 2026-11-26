using Fuste.Application.Abstractions;
using Fuste.Application.Common;
using Fuste.Domain.Companies;

namespace Fuste.Application.Companies;

public sealed class CompanyUseCases(ICompanyRepository companies)
{
    public Task<IReadOnlyList<RepresentedCompany>> ListAsync(CancellationToken cancellationToken) =>
        companies.ListAsync(cancellationToken);

    public async Task<OperationResult<bool>> DeleteAsync(Guid id, CancellationToken cancellationToken) =>
        await companies.DeleteCompanyAsync(id, cancellationToken)
            ? OperationResult<bool>.Success(true)
            : OperationResult<bool>.Failure("Representada não encontrada.", 404);

    public async Task<OperationResult<RepresentedCompany>> CreateAsync(string name, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return OperationResult<RepresentedCompany>.Failure("Informe o nome da representada.");
        }

        if (name.Trim().Length > 160)
        {
            return OperationResult<RepresentedCompany>.Failure("O nome pode ter no máximo 160 caracteres.");
        }

        var existing = await companies.ListAsync(cancellationToken);
        if (existing.Any(item => string.Equals(item.Name, name.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            return OperationResult<RepresentedCompany>.Failure("Já existe uma representada com esse nome.", 409);
        }

        var company = RepresentedCompany.Create(name);
        try
        {
            await companies.AddAsync(company, cancellationToken);
            return OperationResult<RepresentedCompany>.Success(company);
        }
        catch (RepositoryConflictException exception)
        {
            return OperationResult<RepresentedCompany>.Failure(exception.Message, 409);
        }
    }
}
