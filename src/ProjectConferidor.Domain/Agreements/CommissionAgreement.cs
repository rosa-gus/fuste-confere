namespace ProjectConferidor.Domain.Agreements;

public sealed record CommissionAgreement(
    Guid Id,
    Guid CompanyId,
    DateOnly EffectiveFrom,
    decimal Percentage)
{
    public static CommissionAgreement Create(Guid companyId, DateOnly effectiveFrom, decimal percentage)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("Informe a representada.");
        }

        if (percentage is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(null, "O percentual deve estar entre 0 e 100.");
        }

        return new CommissionAgreement(Guid.NewGuid(), companyId, effectiveFrom, percentage);
    }
}
