using ProjectConferidor.Application.Abstractions;
using ProjectConferidor.Application.Common;
using ProjectConferidor.Domain.Companies;

namespace ProjectConferidor.Application.Companies;

public sealed class CompanyUseCases(ICompanyRepository companies)
{
    public Task<IReadOnlyList<RepresentedCompany>> ListAsync(CancellationToken cancellationToken) =>
        companies.ListAsync(cancellationToken);

    public async Task<OperationResult<bool>> DeleteAsync(Guid id, CancellationToken cancellationToken) =>
        await companies.DeleteCompanyAsync(id, cancellationToken)
            ? OperationResult<bool>.Success(true)
            : OperationResult<bool>.Failure("Company not found.", 404);

    public async Task<OperationResult<RepresentedCompany>> CreateAsync(string name, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return OperationResult<RepresentedCompany>.Failure("Company name is required.");
        }

        if (name.Trim().Length > 160)
        {
            return OperationResult<RepresentedCompany>.Failure("Company name must not exceed 160 characters.");
        }

        var existing = await companies.ListAsync(cancellationToken);
        if (existing.Any(item => string.Equals(item.Name, name.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            return OperationResult<RepresentedCompany>.Failure("A company with this name already exists.", 409);
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
