using System.Globalization;
using ProjectConferidor.Application.Abstractions;
using ProjectConferidor.Application.Imports;
using ProjectConferidor.Domain.Financial;
using ProjectConferidor.Domain.Imports;
using ProjectConferidor.Domain.Reconciliation;
using ProjectConferidor.Domain.Sales;

namespace ProjectConferidor.Infrastructure.Imports;

public sealed partial class CsvSourceFileReader
{
    private const int PreviewRows = 20;
    private const int PreviewIssues = 50;

    public async Task<ImportPreparationResult> PrepareAsync(Stream sales, Stream statement,
        ImportConfiguration? configuration, CancellationToken cancellationToken)
    {
        var collector = new IssueCollector();
        var explicitConfiguration = configuration is not null;
        var configurationErrors = explicitConfiguration ? ImportConfiguration.Validate(configuration) : [];
        foreach (var error in configurationErrors) collector.Add(new("configuration", null, null, null, "invalid_configuration", error));
        // Inspect safely even for invalid/untrusted configuration objects.
        var salesDelimiter = configuration?.Sales?.Delimiter;
        var statementDelimiter = configuration?.Statement?.Delimiter;
        var salesTable = await ReadTableAsync(sales, "sales", cancellationToken, ExplicitDelimiter(salesDelimiter));
        var statementTable = await ReadTableAsync(statement, "statement", cancellationToken, ExplicitDelimiter(statementDelimiter));
        var effective = configuration?.Sales?.Columns is not null && configuration.Statement?.Columns is not null
            ? configuration.Snapshot()
            : SuggestConfiguration(salesTable, statementTable);
        var salesRows = new List<SalesFileRow>();
        var statementRows = new List<StatementFileRow>();
        var normalizedSales = new List<Dictionary<string, string?>>();
        var normalizedStatement = new List<Dictionary<string, string?>>();
        var shouldValidate = explicitConfiguration && configurationErrors.Count == 0;
        var salesIndexes = ValidateTable(salesTable, "sales", effective.Sales, shouldValidate, collector);
        var statementIndexes = ValidateTable(statementTable, "statement", effective.Statement, shouldValidate, collector);

        if (shouldValidate)
        {
            var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in salesTable.Rows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (row.Cells.Count != salesTable.Headers.Count || salesIndexes is null) continue;
                var before = collector.Total;
                var id = ReadId(row, salesIndexes["externalId"], "sales", "externalId", effective.Sales, collector);
                var dateText = Get(row.Cells, salesIndexes["date"]);
                DateOnly date = default;
                if (!TryConfiguredDate(dateText, effective.Sales.DateFormat, out date))
                    AddCellIssue(collector, "sales", row.Line, "date", effective.Sales, "invalid_date", "A data não corresponde ao formato selecionado.");
                var amount = ReadAmount(row, salesIndexes["grossAmount"], "sales", "grossAmount", effective.Sales, collector);
                if (id is not null && !seenIds.Add(id))
                    AddCellIssue(collector, "sales", row.Line, "externalId", effective.Sales, "duplicate_id", $"O arquivo de vendas repete o identificador {id}.");
                if (before != collector.Total) continue;
                var item = Sale.Create(id!, date, amount!.Value);
                salesRows.Add(new(item.ExternalId, item.Date, item.GrossAmount));
                if (normalizedSales.Count < PreviewRows) normalizedSales.Add(new()
                {
                    ["externalId"] = item.ExternalId,
                    ["date"] = item.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    ["grossAmount"] = item.GrossAmount.ToString(CultureInfo.InvariantCulture)
                });
            }
            if (salesTable.Rows.Count == 0 && salesTable.Headers.Count > 0)
                collector.Add(new("sales", null, null, null, "no_records", "O arquivo de vendas não contém registros."));
            var totals = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in statementTable.Rows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (row.Cells.Count != statementTable.Headers.Count || statementIndexes is null) continue;
                var before = collector.Total;
                var id = ReadId(row, statementIndexes["saleExternalId"], "statement", "saleExternalId", effective.Statement, collector);
                var amount = ReadAmount(row, statementIndexes["commissionAmount"], "statement", "commissionAmount", effective.Statement, collector);
                if (before != collector.Total) continue;
                var item = StatementEntry.Create(id!, amount!.Value);
                var previous = totals.GetValueOrDefault(item.SaleExternalId);
                if (item.CommissionAmount > MonetaryLimits.MaximumAmount - previous)
                    AddCellIssue(collector, "statement", row.Line, "commissionAmount", effective.Statement, "total_exceeds_limit",
                        $"A soma das comissões informadas para a venda {item.SaleExternalId} deve ser de, no máximo, {MonetaryLimits.MaximumAmountDisplay}.");
                else totals[item.SaleExternalId] = previous + item.CommissionAmount;
                statementRows.Add(new(item.SaleExternalId, item.CommissionAmount));
                if (normalizedStatement.Count < PreviewRows) normalizedStatement.Add(new()
                {
                    ["saleExternalId"] = item.SaleExternalId,
                    ["commissionAmount"] = item.CommissionAmount.ToString(CultureInfo.InvariantCulture)
                });
            }
        }
        var preview = new ImportPreview(effective,
            [SourcePreview(salesTable, "sales", normalizedSales), SourcePreview(statementTable, "statement", normalizedStatement)],
            collector.Issues, collector.Total, collector.Total > collector.Issues.Count,
            shouldValidate && collector.Total == 0, shouldValidate);
        return new(preview, salesRows, statementRows);
    }

    private static char? ExplicitDelimiter(string? delimiter) => delimiter is "," or ";" ? delimiter[0] : null;

    private static ImportSourcePreview SourcePreview(CsvTable table, string kind, List<Dictionary<string, string?>> normalized) =>
        new(kind, table.OriginalHeaders ?? table.Headers,
            table.Rows.Take(PreviewRows).Select(row => (IReadOnlyList<string>)row.Cells).ToArray(), normalized,
            table.Rows.Count, table.Delimiter.ToString());

    private static ImportConfiguration SuggestConfiguration(CsvTable sales, CsvTable statement) => new(1,
        SuggestSource(sales, new Dictionary<string, string[]> { ["externalId"] = ["sale_id", "id_venda"], ["date"] = ["sale_date", "data_venda"], ["grossAmount"] = ["gross_amount", "valor_bruto"] }),
        SuggestSource(statement, new Dictionary<string, string[]> { ["saleExternalId"] = ["sale_id", "id_venda"], ["commissionAmount"] = ["commission_amount", "valor_comissao"] }));

    private static SourceImportConfiguration SuggestSource(CsvTable table, Dictionary<string, string[]> aliases)
    {
        var columns = new Dictionary<string, string>();
        foreach (var (field, names) in aliases)
        {
            var matches = table.Headers.Select((value, index) => (value, index)).Where(item => names.Contains(item.value, StringComparer.OrdinalIgnoreCase)).ToList();
            columns[field] = matches.Count == 1 ? (table.OriginalHeaders ?? table.Headers)[matches[0].index] : "";
        }
        return new(table.Delimiter.ToString(), table.Delimiter == ';' ? "pt-BR" : "invariant", "auto", columns);
    }

    private static Dictionary<string, int>? ValidateTable(CsvTable table, string kind, SourceImportConfiguration source,
        bool validateMapping, IssueCollector collector)
    {
        foreach (var error in table.Errors)
        {
            var rowMatch = System.Text.RegularExpressions.Regex.Match(error, @"na linha (\d+)");
            collector.Add(new(kind, rowMatch.Success ? int.Parse(rowMatch.Groups[1].Value, CultureInfo.InvariantCulture) : null,
                null, null, "invalid_csv", error));
        }
        foreach (var group in table.Headers.Select((header, index) => (header, index)).GroupBy(item => item.header, StringComparer.Ordinal))
        {
            if (group.Count() > 1) collector.Add(new(kind, 1, (table.OriginalHeaders ?? table.Headers)[group.First().index], null,
                "duplicate_header", "O arquivo contém cabeçalhos repetidos. Renomeie-os antes de configurar as colunas."));
            else if (group.Key.Length == 0) collector.Add(new(kind, 1, "", null, "empty_header", "Todas as colunas devem ter um título no cabeçalho."));
        }
        if (!validateMapping) return null;
        var indexes = new Dictionary<string, int>();
        var valid = true;
        foreach (var (field, column) in source.Columns)
        {
            var normalized = ImportConfiguration.NormalizeHeader(column);
            var matches = table.Headers.Select((header, index) => (header, index)).Where(item => item.header == normalized).ToList();
            if (matches.Count != 1)
            {
                collector.Add(new(kind, 1, column, field, matches.Count == 0 ? "missing_column" : "ambiguous_column",
                    matches.Count == 0 ? $"A coluna configurada “{column}” não foi encontrada no arquivo." : $"Há mais de uma coluna com o nome “{column}”. Use cabeçalhos distintos."));
                valid = false;
            }
            else indexes[field] = matches[0].index;
        }
        return valid ? indexes : null;
    }

    private static string? ReadId(CsvRow row, int index, string kind, string field, SourceImportConfiguration source, IssueCollector collector)
    {
        var value = Get(row.Cells, index);
        if (value.Length is 0 or > 120)
        {
            AddCellIssue(collector, kind, row.Line, field, source, "invalid_id", "O identificador da venda deve ter até 120 caracteres.");
            return null;
        }
        return value;
    }

    private static decimal? ReadAmount(CsvRow row, int index, string kind, string field, SourceImportConfiguration source, IssueCollector collector)
    {
        // Reuse the exact strict grouping/culture parser; format is independent of delimiter.
        if (!TryParseAmount(Get(row.Cells, index), source.NumberFormat == "pt-BR" ? ';' : ',', out var value) || value < 0)
        {
            AddCellIssue(collector, kind, row.Line, field, source, "invalid_amount", "O valor deve ser maior ou igual a zero e corresponder ao formato numérico selecionado.");
            return null;
        }
        if (value > MonetaryLimits.MaximumAmount)
        {
            AddCellIssue(collector, kind, row.Line, field, source, "amount_exceeds_limit", $"O valor deve ser de, no máximo, {MonetaryLimits.MaximumAmountDisplay}.");
            return null;
        }
        return value;
    }

    private static bool TryConfiguredDate(string value, string format, out DateOnly date) => format == "auto"
        ? TryParseDate(value, out date)
        : DateOnly.TryParseExact(value, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    private static void AddCellIssue(IssueCollector collector, string kind, int row, string field,
        SourceImportConfiguration source, string code, string message) =>
        collector.Add(new(kind, row, source.Columns[field], field, code, message));

    private sealed class IssueCollector
    {
        public int Total { get; private set; }
        public List<ImportIssue> Issues { get; } = [];
        public void Add(ImportIssue issue) { Total++; if (Issues.Count < PreviewIssues) Issues.Add(issue); }
    }
}
