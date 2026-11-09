using ProjectConferidor.Domain.Financial;

namespace ProjectConferidor.Domain.Sales;

public sealed record Sale(string ExternalId, DateOnly Date, decimal GrossAmount)
{
    public static Sale Create(string externalId, DateOnly date, decimal grossAmount)
    {
        var normalizedId = externalId.Trim();
        if (normalizedId.Length is 0 or > 120)
        {
            throw new ArgumentException("Sale ID must contain between 1 and 120 characters.", nameof(externalId));
        }

        if (grossAmount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(grossAmount), "Gross sale amount cannot be negative.");
        }

        if (grossAmount > MonetaryLimits.MaximumAmount)
        {
            throw new ArgumentOutOfRangeException(nameof(grossAmount), $"Gross sale amount must not exceed {MonetaryLimits.MaximumAmountText}.");
        }

        return new Sale(normalizedId, date, grossAmount);
    }
}
