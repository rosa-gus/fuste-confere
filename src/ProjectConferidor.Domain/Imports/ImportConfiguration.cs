namespace ProjectConferidor.Domain.Imports;

public sealed record ImportConfiguration(int Version, SourceImportConfiguration Sales, SourceImportConfiguration Statement)
{
    public static IReadOnlyList<string> Validate(ImportConfiguration? configuration)
    {
        var errors = new List<string>();
        if (configuration is null) return ["Import configuration is required."];
        if (configuration.Version != 1) errors.Add("Unsupported import configuration version.");
        ValidateSource(configuration.Sales, "sales", ["externalId", "date", "grossAmount"], errors);
        ValidateSource(configuration.Statement, "statement", ["saleExternalId", "commissionAmount"], errors);
        return errors;
    }

    private static void ValidateSource(SourceImportConfiguration? source, string kind, string[] fields, List<string> errors)
    {
        if (source is null) { errors.Add($"The {kind} import configuration is required."); return; }
        if (source.Delimiter is not ("," or ";" or "auto")) errors.Add($"Invalid delimiter for {kind}.");
        if (source.NumberFormat is not ("pt-BR" or "invariant")) errors.Add($"Invalid number format for {kind}.");
        if (source.DateFormat is not ("dd/MM/yyyy" or "yyyy-MM-dd" or "auto")) errors.Add($"Invalid date format for {kind}.");
        if (source.Columns is null) { errors.Add($"Column mappings are required for {kind}."); return; }
        foreach (var field in fields)
            if (!source.Columns.TryGetValue(field, out var column) || string.IsNullOrWhiteSpace(column) || column.Length > 256)
                errors.Add($"A column mapping is required for {kind}.{field} (maximum 256 characters).");
        if (source.Columns.Keys.Any(key => !fields.Contains(key, StringComparer.Ordinal))) errors.Add($"Unknown mapped field for {kind}.");
        var names = source.Columns.Values.Where(value => !string.IsNullOrWhiteSpace(value)).Select(NormalizeHeader).ToList();
        if (names.Distinct(StringComparer.Ordinal).Count() != names.Count) errors.Add($"Each {kind} field must map to a different column.");
    }

    public static string NormalizeHeader(string value) => value.Trim().TrimStart('\uFEFF').ToLowerInvariant();

    // Copy mutable dictionaries at execution/save boundaries.
    public ImportConfiguration Snapshot() => new(Version,
        Sales with { Columns = new Dictionary<string, string>(Sales.Columns, StringComparer.Ordinal) },
        Statement with { Columns = new Dictionary<string, string>(Statement.Columns, StringComparer.Ordinal) });
}

public sealed record SourceImportConfiguration(string Delimiter, string NumberFormat, string DateFormat, Dictionary<string, string> Columns);
