using ProjectConferidor.Domain.Financial;

namespace ProjectConferidor.Domain.Sales;

public sealed record Sale(string ExternalId, DateOnly Date, decimal GrossAmount)
{
    public static Sale Create(string externalId, DateOnly date, decimal grossAmount)
    {
        var normalizedId = externalId.Trim();
        if (normalizedId.Length is 0 or > 120)
        {
            throw new ArgumentException("O identificador da venda deve ter até 120 caracteres.");
        }

        if (grossAmount < 0)
        {
            throw new ArgumentOutOfRangeException(null, "O valor bruto da venda não pode ser negativo.");
        }

        if (grossAmount > MonetaryLimits.MaximumAmount)
        {
            throw new ArgumentOutOfRangeException(null, $"O valor bruto da venda deve ser de, no máximo, {MonetaryLimits.MaximumAmountDisplay}.");
        }

        return new Sale(normalizedId, date, grossAmount);
    }
}
