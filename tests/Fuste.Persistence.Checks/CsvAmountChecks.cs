using System.Globalization;
using System.Text;
using Fuste.Application.Reconciliation;
using Fuste.Domain.Agreements;
using Fuste.Domain.Companies;
using Fuste.Infrastructure.Imports;
using Fuste.Infrastructure.Persistence;

internal static class CsvAmountChecks
{
    public static async Task RunAsync(string dataDirectory)
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            foreach (var culture in new[] { "pt-BR", "en-US" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                foreach (var (delimiter, value, expected) in new (char, string, decimal)[]
                {
                    (',', "1250.50", 1250.50m), (',', "1,250.50", 1250.50m),
                    (',', "1,250", 1250m), (',', "0", 0m), (',', "+12.50", 12.50m),
                    (',', "12.3456", 12.3456m), (';', "1250,50", 1250.50m),
                    (';', "1.250,50", 1250.50m), (';', "1.250", 1250m),
                    (';', "0", 0m), (';', "+12,50", 12.50m), (';', "12,3456", 12.3456m)
                })
                {
                    await CheckAmountAsync(delimiter, value, expected);
                }

                foreach (var (delimiter, value) in new (char, string)[]
                {
                    (';', "1250.50"), (',', "1250,50"),
                    (';', "1,250.50"), (',', "1.250,50"),
                    (';', "12.34,56"), (',', "12,34.56"),
                    (';', "1.23.456,78"), (',', "1,23,456.78"),
                    (';', "12.50"), (',', "12,50"),
                    (';', "1e3"), (',', "1e3"),
                    (';', "R$ 12,50"), (',', "$12.50"),
                    (';', "-12,50"), (',', "-12.50"),
                    (';', "12,50-"), (',', "12.50-"),
                    (';', ""), (',', ""), (';', "1 250,50"), (',', "1 250.50")
                })
                {
                    await CheckAmountAsync(delimiter, value, expected: null);
                }
            }

            // A bad row must reject the whole reconciliation, including valid
            // rows parsed before it. Neither input may cause a partial save.
            var repository = new SqliteFusteRepository(dataDirectory);
            var company = RepresentedCompany.Create("CSV validation");
            await repository.AddAsync(company, default);
            await repository.AddAsync(CommissionAgreement.Create(company.Id, new DateOnly(2026, 1, 1), 5), default);
            var useCases = new ReconciliationUseCases(repository, repository, repository, new CsvSourceFileReader());
            foreach (var invalidSales in new[] { true, false })
            {
                using var sales = Input("sale_id;sale_date;gross_amount\nV1;2026-03-01;100,00\nV2;2026-03-01;" + (invalidSales ? "1250.50" : "1250,50"));
                using var statement = Input("sale_id;commission_amount\nV1;5,00\nV2;" + (invalidSales ? "62,53" : "62.53"));
                var result = await useCases.RunAsync(company.Id, sales, statement, default);
                if (result.IsSuccess || result.StatusCode != 400 || (await repository.ListReconciliationsForCompanyAsync(company.Id, default)).Count != 0)
                    throw new Exception("An incompatible monetary format did not reject the entire reconciliation.");
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
        Console.WriteLine("PASS CSV amounts: strict delimiter-specific formats, valid grouping, exact values, incompatible format rejection in both files, culture independence and no partial saves.");
    }

    private static async Task CheckAmountAsync(char delimiter, string value, decimal? expected)
    {
        var reader = new CsvSourceFileReader();
        var quoted = '"' + value.Replace("\"", "\"\"") + '"';
        using var sales = Input($"sale_id{delimiter}sale_date{delimiter}gross_amount\nV1{delimiter}2026-03-01{delimiter}{quoted}");
        using var statement = Input($"sale_id{delimiter}commission_amount\nV1{delimiter}{quoted}");
        var salesResult = await reader.ReadSalesAsync(sales, default);
        var statementResult = await reader.ReadStatementAsync(statement, default);
        if (expected.HasValue)
        {
            if (salesResult.Errors.Count != 0 || statementResult.Errors.Count != 0 ||
                salesResult.Rows.Single().GrossAmount != expected || statementResult.Rows.Single().CommissionAmount != expected)
                throw new Exception($"Valid amount '{value}' with delimiter '{delimiter}' changed value or was rejected.");
        }
        else if (salesResult.Errors.Count != 1 || statementResult.Errors.Count != 1 || salesResult.Rows.Count != 0 || statementResult.Rows.Count != 0)
        {
            throw new Exception($"Invalid amount '{value}' with delimiter '{delimiter}' was accepted.");
        }
    }

    private static MemoryStream Input(string text) => new(Encoding.UTF8.GetBytes(text));
}
