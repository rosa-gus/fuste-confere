using ProjectConferidor.Domain.Financial;

namespace ProjectConferidor.Domain.Reconciliation;

public sealed record StatementEntry(string SaleExternalId, decimal CommissionAmount)
{
    public static StatementEntry Create(string saleExternalId, decimal commissionAmount)
    {
        var normalizedId = saleExternalId.Trim();
        if (normalizedId.Length is 0 or > 120)
        {
            throw new ArgumentException("O identificador da venda deve ter até 120 caracteres.");
        }

        if (commissionAmount < 0)
        {
            throw new ArgumentOutOfRangeException(null, "A comissão informada não pode ser negativa.");
        }

        if (commissionAmount > MonetaryLimits.MaximumAmount)
        {
            throw new ArgumentOutOfRangeException(null, $"A comissão informada deve ser de, no máximo, {MonetaryLimits.MaximumAmountDisplay}.");
        }

        return new StatementEntry(normalizedId, commissionAmount);
    }
}
