using ProjectConferidor.Domain.Financial;

namespace ProjectConferidor.Domain.Reconciliation;

public sealed record StatementEntry(string SaleExternalId, decimal CommissionAmount)
{
    public static StatementEntry Create(string saleExternalId, decimal commissionAmount)
    {
        var normalizedId = saleExternalId.Trim();
        if (normalizedId.Length is 0 or > 120)
        {
            throw new ArgumentException("Sale ID must contain between 1 and 120 characters.", nameof(saleExternalId));
        }

        if (commissionAmount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(commissionAmount), "Reported commission cannot be negative.");
        }

        if (commissionAmount > MonetaryLimits.MaximumAmount)
        {
            throw new ArgumentOutOfRangeException(nameof(commissionAmount), $"Reported commission must not exceed {MonetaryLimits.MaximumAmountText}.");
        }

        return new StatementEntry(normalizedId, commissionAmount);
    }
}
