using System.Text;
using Fuste.Application.Reconciliation;
using Fuste.Domain.Agreements;
using Fuste.Domain.Companies;
using Fuste.Domain.Financial;
using Fuste.Infrastructure.Imports;
using Fuste.Infrastructure.Persistence;

internal static class ReconciliationValidationChecks
{
    public static async Task RunAsync(string directory)
    {
        var repository = new SqliteFusteRepository(directory);
        var company = RepresentedCompany.Create("Financial validation");
        await repository.AddAsync(company, default);
        await repository.AddAsync(CommissionAgreement.Create(company.Id, new DateOnly(2026, 1, 1), 5), default);
        var reader = new CsvSourceFileReader();
        var useCases = new ReconciliationUseCases(repository, repository, repository, reader);
        const string salesHeader = "sale_id,sale_date,gross_amount\n";
        const string statementHeader = "sale_id,commission_amount\n";
        const string validSales = salesHeader + "V1,2026-03-01,100\n";
        const string validStatement = statementHeader + "V1,5\n";

        foreach (var (sales, statement, detail) in new[]
        {
            (salesHeader + "V1,2026-03-01,1000000000000", validStatement, "Vendas: o valor bruto deve ser de, no máximo, R$ 999.999.999.999,99 na linha 2."),
            (validSales, statementHeader + "V1,1000000000000", "Comissões: a comissão deve ser de, no máximo, R$ 999.999.999.999,99 na linha 2."),
            (salesHeader + "V1,2026-03-01,79228162514264337593543950335", validStatement, "Vendas: o valor bruto deve ser de, no máximo, R$ 999.999.999.999,99 na linha 2."),
            (validSales, statementHeader + "V1,79228162514264337593543950335", "Comissões: a comissão deve ser de, no máximo, R$ 999.999.999.999,99 na linha 2."),
            (validSales, statementHeader + "V1,999999999999.99\nv1,0.01", "A soma das comissões informadas para a venda V1 deve ser de, no máximo, R$ 999.999.999.999,99."),
            (validSales, statementHeader + "other,999999999999.99\nOTHER,0.01", "A soma das comissões informadas para a venda other deve ser de, no máximo, R$ 999.999.999.999,99."),
            (validSales + "v1,2026-03-02,200", validStatement, "O arquivo de vendas repete o identificador V1."),
            (salesHeader + "V1,2026-03-01,99999999999999999999999999999999999999999", validStatement, "Vendas: o valor bruto é inválido na linha 2.")
        })
        {
            using var salesStream = Input(sales);
            using var statementStream = Input(statement);
            var result = await useCases.RunAsync(company.Id, salesStream, statementStream, default);
            if (result.IsSuccess || result.StatusCode != 400 || result.Error != detail)
                throw new Exception($"Expected Portuguese validation error '{detail}', got '{result.Error}'.");
            if ((await repository.ListReconciliationsForCompanyAsync(company.Id, default)).Count != 0)
                throw new Exception("Failed validation saved a reconciliation.");
        }

        foreach (var (content, expected) in new[]
        {
            ("", "O arquivo de comissões está vazio."),
            ("sale_id,commission_amount\nV1,1,2", "O arquivo de comissões tem 3 colunas na linha 2; eram esperadas 2."),
            ("sale_id,commission_amount\nV1,\"1", "O arquivo de comissões tem aspas sem fechamento.")
        })
        {
            using var stream = Input(content);
            var result = await reader.ReadStatementAsync(stream, default);
            if (!result.Errors.Contains(expected))
                throw new Exception("Statement CSV errors must use Portuguese labels.");
        }

        // A defensive fallback also covers arithmetic failures from inconsistent
        // stored data that bypassed the agreement-creation use case.
        var inconsistent = RepresentedCompany.Create("Overflow fallback fixture");
        await repository.AddAsync(inconsistent, default);
        await repository.AddAsync(new CommissionAgreement(Guid.NewGuid(), inconsistent.Id, new DateOnly(2026, 1, 1), decimal.MaxValue), default);
        using (var salesStream = Input(validSales))
        using (var statementStream = Input(validStatement))
        {
            var result = await useCases.RunAsync(inconsistent.Id, salesStream, statementStream, default);
            if (result.IsSuccess || result.StatusCode != 400 || result.Error != "Não foi possível calcular a conferência: os valores excedem o limite numérico suportado.")
                throw new Exception("Arithmetic overflow was not converted to a safe validation error.");
            if ((await repository.ListReconciliationsForCompanyAsync(inconsistent.Id, default)).Count != 0)
                throw new Exception("Arithmetic overflow saved a reconciliation.");
        }

        // Valid boundary input must still complete after the failed requests.
        using (var salesStream = Input(salesHeader + $"V1,2026-03-01,{MonetaryLimits.MaximumAmountText}"))
        using (var statementStream = Input(statementHeader))
        {
            var result = await useCases.RunAsync(company.Id, salesStream, statementStream, default);
            if (!result.IsSuccess) throw new Exception("Valid boundary sale failed after validation errors.");
        }
        Console.WriteLine("PASS reconciliation validation: extreme amounts, per-sale totals, duplicates, Portuguese statement errors, safe overflow response and no partial saves.");
    }

    private static MemoryStream Input(string text) => new(Encoding.UTF8.GetBytes(text));
}
