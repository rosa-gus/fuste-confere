using ProjectConferidor.Domain.Agreements;
using ProjectConferidor.Domain.Sales;
using ProjectConferidor.Domain.Financial;

namespace ProjectConferidor.Domain.Reconciliation;

public static class CommissionReconciler
{
    public static CommissionReconciliation Reconcile(
        Guid companyId,
        IReadOnlyCollection<Sale> sales,
        IReadOnlyCollection<StatementEntry> statementEntries,
        IReadOnlyCollection<CommissionAgreement> agreements)
    {
        var reportedBySale = statementEntries
            .GroupBy(entry => entry.SaleExternalId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, SumReportedCommission, StringComparer.OrdinalIgnoreCase);
        var salesById = sales.ToDictionary(sale => sale.ExternalId, StringComparer.OrdinalIgnoreCase);
        var lines = new List<ReconciliationLine>(sales.Count + statementEntries.Count);

        foreach (var sale in sales.OrderBy(item => item.Date).ThenBy(item => item.ExternalId, StringComparer.OrdinalIgnoreCase))
        {
            reportedBySale.TryGetValue(sale.ExternalId, out var reported);
            var hasReport = reportedBySale.ContainsKey(sale.ExternalId);
            var agreement = agreements
                .Where(item => item.CompanyId == companyId)
                .Where(item => item.EffectiveFrom <= sale.Date)
                .OrderByDescending(item => item.EffectiveFrom)
                .FirstOrDefault();

            if (agreement is null)
            {
                lines.Add(new ReconciliationLine(
                    sale.ExternalId,
                    ReconciliationStatus.MissingAgreement,
                    sale.Date,
                    sale.GrossAmount,
                    null,
                    null,
                    null,
                    hasReport ? reported : null,
                    null));
                continue;
            }

            var expected = RoundMoney(sale.GrossAmount * agreement.Percentage / 100m);
            if (!hasReport)
            {
                lines.Add(new ReconciliationLine(
                    sale.ExternalId,
                    ReconciliationStatus.MissingFromStatement,
                    sale.Date,
                    sale.GrossAmount,
                    agreement.Id,
                    agreement.Percentage,
                    expected,
                    null,
                    null));
                continue;
            }

            var difference = RoundMoney(expected - reported);
            lines.Add(new ReconciliationLine(
                sale.ExternalId,
                difference == 0 ? ReconciliationStatus.Matched : ReconciliationStatus.Difference,
                sale.Date,
                sale.GrossAmount,
                agreement.Id,
                agreement.Percentage,
                expected,
                reported,
                difference));
        }

        foreach (var entry in reportedBySale
                     .Where(entry => !salesById.ContainsKey(entry.Key))
                     .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
        {
            lines.Add(new ReconciliationLine(
                entry.Key,
                ReconciliationStatus.UnrecognizedSale,
                null,
                null,
                null,
                null,
                null,
                entry.Value,
                null));
        }

        return CommissionReconciliation.Create(companyId, lines);
    }

    private static decimal RoundMoney(decimal amount) => decimal.Round(amount, 2, MidpointRounding.AwayFromZero);

    private static decimal SumReportedCommission(IGrouping<string, StatementEntry> entries)
    {
        var total = 0m;
        foreach (var entry in entries)
        {
            // Check before adding, so even a very large accumulated total is
            // rejected as validation rather than overflowing decimal.Sum.
            if (entry.CommissionAmount > MonetaryLimits.MaximumAmount - total)
            {
                throw new ArgumentException($"Reported commission total for sale ID {entries.Key} must not exceed {MonetaryLimits.MaximumAmountText}.");
            }
            total += entry.CommissionAmount;
        }
        // Statement installments are summed at full precision and rounded once.
        return RoundMoney(total);
    }
}
