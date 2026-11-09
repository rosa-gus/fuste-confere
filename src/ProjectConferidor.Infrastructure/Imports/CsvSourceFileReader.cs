using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ProjectConferidor.Application.Abstractions;
using ProjectConferidor.Domain.Financial;

namespace ProjectConferidor.Infrastructure.Imports;

public sealed partial class CsvSourceFileReader : ISourceFileReader, IImportFileReader
{
    private static readonly CultureInfo BrazilianCulture = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly Regex InvariantAmountFormat = new(
        @"\A[+-]?(?:[0-9]+|[0-9]{1,3}(?:,[0-9]{3})+)(?:\.[0-9]+)?\z",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly Regex BrazilianAmountFormat = new(
        @"\A[+-]?(?:[0-9]+|[0-9]{1,3}(?:\.[0-9]{3})+)(?:,[0-9]+)?\z",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    public async Task<FileReadResult<SalesFileRow>> ReadSalesAsync(Stream stream, CancellationToken cancellationToken)
    {
        var table = await ReadTableAsync(stream, "sales", cancellationToken);
        var errors = table.Errors.ToList();
        var idIndex = FindColumn(table.Headers, "sale_id", "id_venda");
        var dateIndex = FindColumn(table.Headers, "sale_date", "data_venda");
        var amountIndex = FindColumn(table.Headers, "gross_amount", "valor_bruto");
        CheckRequiredColumn(idIndex, "sale_id", errors);
        CheckRequiredColumn(dateIndex, "sale_date", errors);
        CheckRequiredColumn(amountIndex, "gross_amount", errors);
        if (errors.Count > 0)
        {
            return new FileReadResult<SalesFileRow>([], errors);
        }

        var rows = new List<SalesFileRow>();
        foreach (var row in table.Rows)
        {
            if (!TryGet(row.Cells, idIndex, out var id) || string.IsNullOrWhiteSpace(id))
            {
                errors.Add($"Sales: missing sale ID at row {row.Line}.");
                continue;
            }

            if (!TryParseDate(Get(row.Cells, dateIndex), out var date))
            {
                errors.Add($"Sales: invalid sale date at row {row.Line}.");
                continue;
            }

            if (!TryParseAmount(Get(row.Cells, amountIndex), table.Delimiter, out var amount) || amount < 0)
            {
                errors.Add($"Sales: invalid gross amount at row {row.Line}.");
                continue;
            }

            if (amount > MonetaryLimits.MaximumAmount)
            {
                errors.Add($"Sales: gross amount must not exceed {MonetaryLimits.MaximumAmountText} at row {row.Line}.");
                continue;
            }

            rows.Add(new SalesFileRow(id.Trim(), date, amount));
        }

        return new FileReadResult<SalesFileRow>(rows, errors);
    }

    public async Task<FileReadResult<StatementFileRow>> ReadStatementAsync(Stream stream, CancellationToken cancellationToken)
    {
        var table = await ReadTableAsync(stream, "statement", cancellationToken);
        var errors = table.Errors.ToList();
        var idIndex = FindColumn(table.Headers, "sale_id", "id_venda");
        var amountIndex = FindColumn(table.Headers, "commission_amount", "valor_comissao");
        CheckRequiredColumn(idIndex, "sale_id", errors);
        CheckRequiredColumn(amountIndex, "commission_amount", errors);
        if (errors.Count > 0)
        {
            return new FileReadResult<StatementFileRow>([], errors);
        }

        var rows = new List<StatementFileRow>();
        foreach (var row in table.Rows)
        {
            if (!TryGet(row.Cells, idIndex, out var id) || string.IsNullOrWhiteSpace(id))
            {
                errors.Add($"Statement: missing sale ID at row {row.Line}.");
                continue;
            }

            if (!TryParseAmount(Get(row.Cells, amountIndex), table.Delimiter, out var amount) || amount < 0)
            {
                errors.Add($"Statement: invalid commission amount at row {row.Line}.");
                continue;
            }

            if (amount > MonetaryLimits.MaximumAmount)
            {
                errors.Add($"Statement: commission amount must not exceed {MonetaryLimits.MaximumAmountText} at row {row.Line}.");
                continue;
            }

            rows.Add(new StatementFileRow(id.Trim(), amount));
        }

        return new FileReadResult<StatementFileRow>(rows, errors);
    }

    private static async Task<CsvTable> ReadTableAsync(Stream stream, string label, CancellationToken cancellationToken, char? configuredDelimiter = null)
    {
        using var reader = new StreamReader(
            stream,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 1024,
            leaveOpen: true);
        var content = await reader.ReadToEndAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(content))
        {
            return new CsvTable([], [], ',', [$"The {label} file is empty."]);
        }

        var firstLineEnd = content.IndexOfAny(['\r', '\n']);
        var firstLine = firstLineEnd < 0 ? content : content[..firstLineEnd];
        var delimiter = configuredDelimiter ?? (firstLine.Count(character => character == ';') > firstLine.Count(character => character == ',') ? ';' : ',');
        var recordLines = new List<int>();
        var parsed = ParseRecords(content, delimiter, recordLines);
        if (parsed.Error is not null)
        {
            return new CsvTable([], [], delimiter, [$"The {label} file is invalid: {parsed.Error}."]);
        }

        if (parsed.Records.Count == 0)
        {
            return new CsvTable([], [], delimiter, [$"The {label} file has no header row."]);
        }

        var headers = parsed.Records[0].Select(NormalizeHeader).ToArray();
        var rows = parsed.Records.Skip(1)
            .Select((cells, index) => new CsvRow(recordLines[index + 1], cells))
            .Where(row => row.Cells.Any(cell => !string.IsNullOrWhiteSpace(cell)))
            .ToList();
        var errors = rows
            .Where(row => row.Cells.Count != headers.Length)
            .Select(row => $"The {label} file has {row.Cells.Count} columns at row {row.Line}; expected {headers.Length}.")
            .ToList();
        return new CsvTable(headers, rows, delimiter, errors, parsed.Records[0].Select(value => value.Trim().TrimStart('\uFEFF')).ToArray());
    }

    private static (List<List<string>> Records, string? Error) ParseRecords(string content, char delimiter, List<int>? recordLines = null)
    {
        var records = new List<List<string>>();
        var record = new List<string>();
        var field = new StringBuilder();
        var insideQuotes = false;
        var physicalLine = 1;
        var recordStartLine = 1;

        for (var index = 0; index < content.Length; index++)
        {
            var character = content[index];
            if (character == '\r' || (character == '\n' && (index == 0 || content[index - 1] != '\r'))) physicalLine++;
            if (character == '"')
            {
                if (insideQuotes && index + 1 < content.Length && content[index + 1] == '"')
                {
                    field.Append('"');
                    index++;
                }
                else
                {
                    insideQuotes = !insideQuotes;
                }
            }
            else if (character == delimiter && !insideQuotes)
            {
                record.Add(field.ToString());
                field.Clear();
            }
            else if ((character is '\r' or '\n') && !insideQuotes)
            {
                record.Add(field.ToString());
                field.Clear();
                records.Add(record);
                recordLines?.Add(recordStartLine);
                recordStartLine = physicalLine;
                record = [];
                if (character == '\r' && index + 1 < content.Length && content[index + 1] == '\n')
                {
                    index++;
                }
            }
            else
            {
                field.Append(character);
            }
        }

        if (insideQuotes)
        {
            return ([], "unclosed quotation mark");
        }

        if (field.Length > 0 || record.Count > 0)
        {
            record.Add(field.ToString());
            records.Add(record);
            recordLines?.Add(recordStartLine);
        }

        return (records, null);
    }

    private static int FindColumn(IReadOnlyList<string> headers, params string[] acceptedNames)
    {
        for (var index = 0; index < headers.Count; index++)
        {
            if (acceptedNames.Contains(headers[index], StringComparer.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    private static void CheckRequiredColumn(int index, string column, ICollection<string> errors)
    {
        if (index < 0)
        {
            errors.Add($"Required column is missing: {column}.");
        }
    }

    private static string NormalizeHeader(string header) => header.Trim().TrimStart('\uFEFF').ToLowerInvariant();
    private static string Get(IReadOnlyList<string> values, int index) => TryGet(values, index, out var value) ? value.Trim() : string.Empty;
    private static bool TryGet(IReadOnlyList<string> values, int index, out string value)
    {
        if (index >= 0 && index < values.Count)
        {
            value = values[index];
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static bool TryParseDate(string value, out DateOnly date) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date)
        || DateOnly.TryParseExact(value, "dd/MM/yyyy", BrazilianCulture, DateTimeStyles.None, out date);

    private static bool TryParseAmount(string value, char delimiter, out decimal amount)
    {
        amount = default;
        value = value.Trim();
        // The delimiter determines the numeric format for the entire file.
        // Validate grouping before parsing: NumberStyles.Number alone accepts
        // misplaced group separators and can silently multiply monetary values.
        var format = delimiter == ';' ? BrazilianAmountFormat : InvariantAmountFormat;
        if (!format.IsMatch(value)) return false;
        var culture = delimiter == ';' ? BrazilianCulture : CultureInfo.InvariantCulture;
        return decimal.TryParse(value,
            NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowThousands,
            culture, out amount);
    }

    private sealed record CsvRow(int Line, List<string> Cells);
    private sealed record CsvTable(IReadOnlyList<string> Headers, IReadOnlyList<CsvRow> Rows, char Delimiter, IReadOnlyList<string> Errors, IReadOnlyList<string>? OriginalHeaders = null);
}
