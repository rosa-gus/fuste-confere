using ProjectConferidor.Domain.Agreements;
using ProjectConferidor.Domain.Financial;
using ProjectConferidor.Domain.Reconciliation;
using ProjectConferidor.Domain.Sales;

internal static class ReconciliationRulesChecks
{
    public static void Run()
    {
        var company = Guid.NewGuid();
        CommissionReconciliation Reconcile(Sale[] sales, StatementEntry[] statement, CommissionAgreement[] agreements) =>
            CommissionReconciler.Reconcile(company, sales, statement, agreements);
        var start = new DateOnly(2026, 3, 1);
        var first = CommissionAgreement.Create(company, start, 5);
        var second = CommissionAgreement.Create(company, start.AddDays(15), 7);
        var future = CommissionAgreement.Create(company, start.AddMonths(1), 20);
        var unrelated = CommissionAgreement.Create(Guid.NewGuid(), start, 99);

        foreach (var (gross, expected) in new[] { (100.10m, 5.01m), (0.09m, 0m), (0.10m, 0.01m), (0.11m, 0.01m) })
        {
            var line = Reconcile([Sale.Create("V1", start, gross)], [StatementEntry.Create("V1", expected)], [first]).Lines.Single();
            Equal(expected, line.ExpectedCommission, "Commission must round midpoint away from zero to cents");
            Equal(ReconciliationStatus.Matched, line.Status, "Rounded commission must match");
        }

        var datedSales = new[]
        {
            Sale.Create("before-first", start.AddDays(-1), 100),
            Sale.Create("on-first", start, 100),
            Sale.Create("before-second", start.AddDays(14), 100),
            Sale.Create("on-second", start.AddDays(15), 100)
        };
        var dated = Reconcile(datedSales, [], [future, second, unrelated, first]).Lines.ToDictionary(line => line.SaleExternalId);
        Equal(ReconciliationStatus.MissingAgreement, dated["before-first"].Status, "Sale before first agreement");
        Equal(null, dated["before-first"].ExpectedCommission, "Missing agreement must not imply zero commission");
        Equal(first.Id, dated["on-first"].AgreementId, "First effective date must be inclusive");
        Equal(5m, dated["before-second"].ExpectedCommission, "Previous agreement must apply before the new date");
        Equal(second.Id, dated["on-second"].AgreementId, "New effective date must be inclusive");
        Equal(7m, dated["on-second"].ExpectedCommission, "Ignore future and unrelated-company agreements");

        foreach (var percentage in new[] { 0m, 100m })
        {
            var line = Reconcile([Sale.Create("V1", start, 100)], [StatementEntry.Create("V1", percentage)],
                [CommissionAgreement.Create(company, start, percentage)]).Lines.Single();
            Equal(percentage, line.ExpectedCommission, "Zero and 100% must be supported");
            Equal(ReconciliationStatus.Matched, line.Status, "Boundary percentage match");
        }
        Throws<ArgumentOutOfRangeException>(() => CommissionAgreement.Create(company, start, -0.01m));
        Throws<ArgumentOutOfRangeException>(() => CommissionAgreement.Create(company, start, 100.01m));

        var summed = Reconcile([Sale.Create(" v1 ", start, 0.10m)],
            [StatementEntry.Create("V1", 0.004m), StatementEntry.Create("v1 ", 0.004m),
             StatementEntry.Create(" UNKNOWN ", 0.004m), StatementEntry.Create("unknown", 0.004m)], [first]);
        Equal(2, summed.Lines.Count, "IDs must be trimmed and grouped without case sensitivity");
        Equal(0.01m, summed.Lines[0].ReportedCommission, "Sum installments before rounding");
        Equal(ReconciliationStatus.Matched, summed.Lines[0].Status, "Summed commission match");
        Equal(ReconciliationStatus.UnrecognizedSale, summed.Lines[1].Status, "Unknown statement sale");
        Equal(0.01m, summed.Lines[1].ReportedCommission, "Unknown installments must also round once");
        Equal(null, summed.Lines[1].GrossAmount, "Unknown sale must not imply zero gross amount");
        Equal(null, summed.Lines[1].Difference, "Unknown sale has no comparable difference");

        var missing = Reconcile([Sale.Create("missing", start, 100), Sale.Create("zero", start, 100)],
            [StatementEntry.Create("zero", 0)], [first]).Lines.ToDictionary(line => line.SaleExternalId);
        Equal(ReconciliationStatus.MissingFromStatement, missing["missing"].Status, "Absent statement entry");
        Equal(null, missing["missing"].ReportedCommission, "Absent commission is null");
        Equal(ReconciliationStatus.Difference, missing["zero"].Status, "Explicit zero is not an absent entry");
        Equal(0m, missing["zero"].ReportedCommission, "Keep explicit zero");
        Equal(5m, missing["zero"].Difference, "Expected minus zero commission");
        var noAgreement = Reconcile([Sale.Create("missing", start.AddDays(-1), 100)], [], [first]).Lines.Single();
        Equal(ReconciliationStatus.MissingAgreement, noAgreement.Status, "Missing agreement takes precedence over missing statement");
        Equal(null, noAgreement.AgreementId, "No applied agreement");

        var differences = Reconcile([Sale.Create("less", start, 100), Sale.Create("more", start, 100)],
            [StatementEntry.Create("less", 4), StatementEntry.Create("more", 6)], [first]).Lines.ToDictionary(line => line.SaleExternalId);
        Equal(1m, differences["less"].Difference, "Underpayment is a positive difference");
        Equal(-1m, differences["more"].Difference, "Overpayment is a negative difference");

        var ordered = Reconcile([Sale.Create("Z", start.AddDays(1), 100), Sale.Create("B", start, 100), Sale.Create("a", start, 100)],
            [StatementEntry.Create("unknown-z", 1), StatementEntry.Create("unknown-a", 1)], [first]);
        Equal("a,B,Z,unknown-a,unknown-z", string.Join(',', ordered.Lines.Select(line => line.SaleExternalId)), "Stable sale/date and unknown-ID ordering");

        var maximum = MonetaryLimits.MaximumAmount;
        var boundary = Reconcile([Sale.Create("MAX", start, maximum)],
            [StatementEntry.Create("max", maximum - 0.01m), StatementEntry.Create("MAX", 0.01m)],
            [CommissionAgreement.Create(company, start, 100)]).Lines.Single();
        Equal(maximum, boundary.ExpectedCommission, "Maximum sale at 100% must calculate without overflow");
        Equal(maximum, boundary.ReportedCommission, "Maximum summed commission must be accepted");
        Equal(ReconciliationStatus.Matched, boundary.Status, "Maximum amounts match");
        Throws<ArgumentOutOfRangeException>(() => Sale.Create("V1", start, maximum + 0.01m));
        Throws<ArgumentOutOfRangeException>(() => StatementEntry.Create("V1", maximum + 0.01m));
        Throws<ArgumentOutOfRangeException>(() => Sale.Create("V1", start, decimal.MaxValue));
        Throws<ArgumentOutOfRangeException>(() => StatementEntry.Create("V1", decimal.MaxValue));
        foreach (var id in new[] { "V1", "unknown" })
        {
            Throws<ArgumentException>(() => Reconcile([Sale.Create("V1", start, 100)],
                [StatementEntry.Create(id, maximum), StatementEntry.Create(id.ToUpperInvariant(), 0.01m)], [first]));
        }

        Console.WriteLine("PASS financial rules: rounding boundaries, agreement effective dates and ownership, percentages, installment sums, null/zero semantics, discrepancy signs, ordering and monetary limits.");
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new Exception($"{message}: expected {expected}, got {actual}.");
    }

    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new Exception($"Expected {typeof(T).Name}.");
    }
}
