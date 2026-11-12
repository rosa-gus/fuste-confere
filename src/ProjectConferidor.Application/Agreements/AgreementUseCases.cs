using ProjectConferidor.Application.Abstractions;
using ProjectConferidor.Application.Common;
using ProjectConferidor.Domain.Agreements;

namespace ProjectConferidor.Application.Agreements;

public sealed class AgreementUseCases(ICompanyRepository companies, IAgreementRepository agreements)
{
    public Task<IReadOnlyList<CommissionAgreement>> ListAsync(Guid companyId, CancellationToken cancellationToken) =>
        agreements.ListAgreementsForCompanyAsync(companyId, cancellationToken);

    public async Task<OperationResult<bool>> DeleteAsync(Guid companyId, Guid id, CancellationToken cancellationToken) =>
        await agreements.DeleteAgreementAsync(companyId, id, cancellationToken)
            ? OperationResult<bool>.Success(true)
            : OperationResult<bool>.Failure("Acordo não encontrado. Ele pode ter sido excluído em outra sessão.", 404);

    public async Task<OperationResult<CommissionAgreement>> CreateAsync(
        Guid companyId,
        DateOnly effectiveFrom,
        decimal percentage,
        CancellationToken cancellationToken)
    {
        if (effectiveFrom == default)
        {
            return OperationResult<CommissionAgreement>.Failure("Informe a data de início do acordo.");
        }

        if (await companies.FindCompanyAsync(companyId, cancellationToken) is null)
        {
            return OperationResult<CommissionAgreement>.Failure("Representada não encontrada.", 404);
        }

        var current = await agreements.ListAgreementsForCompanyAsync(companyId, cancellationToken);
        if (current.Any(item => item.EffectiveFrom == effectiveFrom))
        {
            return OperationResult<CommissionAgreement>.Failure("Já existe um acordo com essa data de início.", 409);
        }

        try
        {
            var agreement = CommissionAgreement.Create(companyId, effectiveFrom, percentage);
            await agreements.AddAsync(agreement, cancellationToken);
            return OperationResult<CommissionAgreement>.Success(agreement);
        }
        catch (RepositoryConflictException exception)
        {
            return OperationResult<CommissionAgreement>.Failure(exception.Message, 409);
        }
        catch (KeyNotFoundException)
        {
            return OperationResult<CommissionAgreement>.Failure("Representada não encontrada.", 404);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            return OperationResult<CommissionAgreement>.Failure(exception.Message);
        }
    }
}
