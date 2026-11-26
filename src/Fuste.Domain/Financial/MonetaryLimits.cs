using System.Globalization;

namespace Fuste.Domain.Financial;

public static class MonetaryLimits
{
    // A technical input limit, not a tax or accounting rule. Keeps commission
    // products and per-sale totals bounded, and cent values safe in the web UI.
    public const decimal MaximumAmount = 999_999_999_999.99m;

    public static string MaximumAmountText => MaximumAmount.ToString(CultureInfo.InvariantCulture);
    public static string MaximumAmountDisplay => "R$ " + MaximumAmount.ToString("N2", CultureInfo.GetCultureInfo("pt-BR"));
}
