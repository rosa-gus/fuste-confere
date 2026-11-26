using System.Globalization;
using System.Text;
using Fuste.Domain.Imports;
using Fuste.Infrastructure.Imports;

internal static class CsvImportProfileChecks
{
    public const string ExternalSales = "Cliente;Total faturado;Emissão;Documento\nEmpresa;1250.50;01/03/2026;000123\n";
    public const string ExternalStatement = "Pedido,Comissão\n000123,\"62,53\"\n";

    public static ImportConfiguration Configuration() => new(1,
        new(";", "invariant", "dd/MM/yyyy", new() { ["externalId"] = "Documento", ["date"] = "Emissão", ["grossAmount"] = "Total faturado" }),
        new(",", "pt-BR", "yyyy-MM-dd", new() { ["saleExternalId"] = "Pedido", ["commissionAmount"] = "Comissão" }));

    public static async Task RunAsync()
    {
        var reader = new CsvSourceFileReader();
        var culture = CultureInfo.CurrentCulture;
        try
        {
            foreach (var currentCulture in new[] { "pt-BR", "en-US" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(currentCulture);
                using var sales = Input(ExternalSales);
                using var statement = Input(ExternalStatement);
                var prepared = await reader.PrepareAsync(sales, statement, Configuration(), default);
                Check(prepared.Preview.IsValid && prepared.Preview.ValidationComplete && prepared.Preview.TotalIssues == 0,
                    "Mapped CSV with independent delimiter/number format must validate completely.");
                var sale = prepared.Sales.Single();
                Check(sale.ExternalId == "000123" && sale.GrossAmount == 1250.50m && sale.Date == new DateOnly(2026, 3, 1),
                    "Mapping changed leading-zero ID, amount or date.");
                Check(prepared.Statement.Single().CommissionAmount == 62.53m && prepared.Statement.Single().SaleExternalId == "000123",
                    "Statement number format must be independent of comma delimiter.");
                Check(prepared.Preview.Sources.Count == 2 && prepared.Preview.Sources.All(source => source.TotalRows == 1),
                    "Preview omitted a source or miscounted records.");
            }

            using (var sales = Input(" ID_VENDA ;DATA_VENDA;VALOR_BRUTO\n000123;01/03/2026;1.250,50"))
            using (var statement = Input("id_venda;valor_comissao\n000123;62,53"))
            {
                var prepared = await reader.PrepareAsync(sales, statement, null, default);
                Check(!prepared.Preview.IsValid && !prepared.Preview.ValidationComplete && prepared.Preview.Sources.Count == 2,
                    "Unconfigured inspection must not claim complete validation.");
                Check(prepared.Preview.Configuration.Sales.Columns["externalId"].Trim().Equals("id_venda", StringComparison.OrdinalIgnoreCase),
                    "Unconfigured inspection must suggest the recognized Portuguese ID alias.");
            }

            using (var sales = Input(" ID_VENDA ;DATA_VENDA;VALOR_BRUTO\n000123;01/03/2026;1.250,50"))
            using (var statement = Input("id_venda;valor_comissao\n000123;62,53"))
            {
                var salesRead = await reader.ReadSalesAsync(sales, default);
                var statementRead = await reader.ReadStatementAsync(statement, default);
                Check(salesRead.Errors.Count == 0 && statementRead.Errors.Count == 0 && salesRead.Rows.Single().GrossAmount == 1250.50m,
                    "Legacy parser must preserve Portuguese aliases and delimiter-specific numeric formats.");
                Check(salesRead.Rows.Single().ExternalId == "000123" && statementRead.Rows.Single().CommissionAmount == 62.53m,
                    "Legacy parser changed the ID or reported amount.");
            }

            foreach (var badSales in new[]
            {
                ExternalSales.Replace("Total faturado", "Valor desconhecido"),
                "Documento; documento ;Emissão;Total faturado\n000123;000123;01/03/2026;1250.50",
                ExternalSales.Replace("1250.50", "1.250,50"),
                ExternalSales.Replace("01/03/2026", "2026-03-01")
            })
            {
                using var sales = Input(badSales);
                using var statement = Input(ExternalStatement);
                var prepared = await reader.PrepareAsync(sales, statement, Configuration(), default);
                Check(!prepared.Preview.IsValid && prepared.Preview.TotalIssues > 0 && prepared.Preview.Issues.Any(issue => issue.SourceKind == "sales"),
                    "Missing/ambiguous mapping or incompatible explicit format was accepted.");
            }

            var sameOrigin = Configuration();
            sameOrigin.Sales.Columns["date"] = "Documento";
            Check(ImportConfiguration.Validate(sameOrigin).Count > 0, "Two destination fields may not silently share an origin column.");
            var missing = Configuration();
            missing.Sales.Columns.Remove("grossAmount");
            Check(ImportConfiguration.Validate(missing).Count > 0, "Missing required field mapping was accepted.");
            Check(ImportConfiguration.Validate(Configuration() with { Version = 999 }).Count > 0, "Unknown configuration version was accepted.");

            var manySales = "Cliente;Total faturado;Emissão;Documento\n" + string.Join('\n',
                Enumerable.Range(1, 21).Select(index => $"Empresa;{(index == 21 ? "not-an-amount" : "1250.50")};01/03/2026;{index:D6}"));
            using (var sales = Input(manySales))
            using (var statement = Input(ExternalStatement))
            {
                var prepared = await reader.PrepareAsync(sales, statement, Configuration(), default);
                var preview = prepared.Preview;
                Check(!preview.IsValid && preview.ValidationComplete, "An invalid row outside the displayed sample escaped complete validation.");
                Check(preview.Sources.Single(source => source.SourceKind == "sales").TotalRows == 21,
                    "Preview sample size must not limit processed input rows.");
                Check(preview.Sources.All(source => source.SampleRows.Count <= 20 && source.NormalizedRows.Count <= 20),
                    "Preview returned more than the agreed 20 displayed rows per source.");
                Check(preview.Issues.Any(issue => issue.SourceKind == "sales" && issue.Row == 22),
                    "Error beyond the sample must retain its original row number.");
            }
            using (var sales = Input("Cliente;Total faturado;Emissão;Documento\n\"Empresa\nFilial\";1250.50;01/03/2026;000123\nOutra;invalid;01/03/2026;000124"))
            using (var statement = Input(ExternalStatement))
            {
                var preview = (await reader.PrepareAsync(sales, statement, Configuration(), default)).Preview;
                Check(preview.Issues.Any(issue => issue.SourceKind == "sales" && issue.Row == 4),
                    "Quoted multiline records must retain physical source line numbers for later diagnostics.");
            }

            using (var sales = Input("Cliente;Total faturado;Emissão;Documento\n" + string.Join('\n',
                Enumerable.Range(1, 60).Select(index => $"Empresa;invalid;01/03/2026;{index:D6}"))))
            using (var statement = Input(ExternalStatement))
            {
                var preview = (await reader.PrepareAsync(sales, statement, Configuration(), default)).Preview;
                Check(!preview.IsValid && preview.ValidationComplete && preview.TotalIssues >= 60 && preview.IssuesTruncated,
                    "A bounded diagnostic list must still count and validate errors beyond its display limit.");
                Check(preview.Issues.Count < preview.TotalIssues, "Truncated issue count must describe the omitted issues.");
            }
        }
        finally { CultureInfo.CurrentCulture = culture; }
        Console.WriteLine("PASS import CSV profiles: legacy compatibility, custom mappings, explicit formats, leading-zero IDs, ambiguity rejection and validation beyond the preview sample.");
    }

    internal static MemoryStream Input(string text) => new(Encoding.UTF8.GetBytes(text));
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
