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
            throw new ArgumentException("Company is required.", nameof(companyId));
        }

        if (percentage is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(percentage), "Percentage must be between 0 and 100.");
        }

        return new CommissionAgreement(Guid.NewGuid(), companyId, effectiveFrom, percentage);
    }
}
