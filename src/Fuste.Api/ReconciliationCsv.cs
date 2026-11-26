using System.Globalization;
using System.Text;
using Fuste.Domain.Reconciliation;

internal static class ReconciliationCsv
{
    private static readonly CultureInfo BrazilianCulture = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly UTF8Encoding EncodingWithBom = new(encoderShouldEmitUTF8Identifier: true);

    public static async Task WriteAsync(Stream stream, CommissionReconciliation reconciliation, CancellationToken cancellationToken)
    {
        await using var writer = new StreamWriter(stream, EncodingWithBom, leaveOpen: true) { NewLine = "\r\n" };
        const string header = "ID da venda;Data da venda;Valor bruto;Comissão (%);Comissão esperada;Comissão informada;Diferença;Situação";
        await writer.WriteLineAsync(header.AsMemory(), cancellationToken);

        foreach (var line in reconciliation.Lines)
        {
            var row = string.Join(";",
                EncodeSaleId(line.SaleExternalId),
                line.SaleDate?.ToString("dd/MM/yyyy", BrazilianCulture),
                FormatMoney(line.GrossAmount),
                line.CommissionPercentage?.ToString("0.############################", BrazilianCulture),
                FormatMoney(line.ExpectedCommission),
                FormatMoney(line.ReportedCommission),
                FormatMoney(line.Difference),
                FormatStatus(line.Status));
            await writer.WriteLineAsync(row.AsMemory(), cancellationToken);
        }
    }

    private static string? FormatMoney(decimal? amount) => amount?.ToString("0.00", BrazilianCulture);

    private static string EncodeSaleId(string id)
    {
        // CSV quoting preserves separators and quotes. The apostrophe keeps formula-like IDs as text.
        var trimmed = id.AsSpan().TrimStart();
        if ((id.Length > 0 && id[0] is '\t' or '\r' or '\n') ||
            (!trimmed.IsEmpty && trimmed[0] is '=' or '+' or '-' or '@' or '＝' or '＋' or '－' or '＠'))
        {
            id = "'" + id;
        }
        return "\"" + id.Replace("\"", "\"\"") + "\"";
    }

    private static string FormatStatus(ReconciliationStatus status) => status switch
    {
        ReconciliationStatus.Matched => "Conferido",
        ReconciliationStatus.Difference => "Divergência",
        ReconciliationStatus.MissingFromStatement => "Sem registro no demonstrativo",
        ReconciliationStatus.UnrecognizedSale => "Venda não localizada",
        ReconciliationStatus.MissingAgreement => "Sem acordo vigente",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
    };
}
