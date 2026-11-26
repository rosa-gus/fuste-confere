namespace Fuste.Domain.Imports;

public sealed record ImportConfiguration(int Version, SourceImportConfiguration Sales, SourceImportConfiguration Statement)
{
    public static IReadOnlyList<string> Validate(ImportConfiguration? configuration)
    {
        var errors = new List<string>();
        if (configuration is null) return ["Informe a configuração de importação."];
        if (configuration.Version != 1) errors.Add("A versão da configuração de importação não é compatível.");
        ValidateSource(configuration.Sales, "sales", ["externalId", "date", "grossAmount"], errors);
        ValidateSource(configuration.Statement, "statement", ["saleExternalId", "commissionAmount"], errors);
        return errors;
    }

    private static void ValidateSource(SourceImportConfiguration? source, string kind, string[] fields, List<string> errors)
    {
        var label = kind == "sales" ? "vendas" : "comissões";
        if (source is null) { errors.Add($"Informe a configuração de importação de {label}."); return; }
        if (source.Delimiter is not ("," or ";" or "auto")) errors.Add($"O separador de {label} é inválido.");
        if (source.NumberFormat is not ("pt-BR" or "invariant")) errors.Add($"O formato numérico de {label} é inválido.");
        if (source.DateFormat is not ("dd/MM/yyyy" or "yyyy-MM-dd" or "auto")) errors.Add($"O formato de data de {label} é inválido.");
        if (source.Columns is null) { errors.Add($"Configure as colunas de {label}."); return; }
        foreach (var field in fields)
            if (!source.Columns.TryGetValue(field, out var column) || string.IsNullOrWhiteSpace(column) || column.Length > 256)
                errors.Add($"Informe a coluna de {label}.{field} (máximo de 256 caracteres).");
        if (source.Columns.Keys.Any(key => !fields.Contains(key, StringComparer.Ordinal))) errors.Add($"A configuração de {label} contém um campo desconhecido.");
        var names = source.Columns.Values.Where(value => !string.IsNullOrWhiteSpace(value)).Select(NormalizeHeader).ToList();
        if (names.Distinct(StringComparer.Ordinal).Count() != names.Count) errors.Add($"Cada campo de {label} deve corresponder a uma coluna diferente.");
    }

    public static string NormalizeHeader(string value) => value.Trim().TrimStart('\uFEFF').ToLowerInvariant();

    // Copy mutable dictionaries at execution/save boundaries.
    public ImportConfiguration Snapshot() => new(Version,
        Sales with { Columns = new Dictionary<string, string>(Sales.Columns, StringComparer.Ordinal) },
        Statement with { Columns = new Dictionary<string, string>(Statement.Columns, StringComparer.Ordinal) });
}

public sealed record SourceImportConfiguration(string Delimiter, string NumberFormat, string DateFormat, Dictionary<string, string> Columns);
