using System.Security.Cryptography;
using System.Text.Json;
using Fuste.Domain.Imports;

namespace Fuste.Application.Reconciliation;

public static class ImportFingerprint
{
    public static string Create(ReadOnlySpan<byte> sales, ReadOnlySpan<byte> statement, ImportConfiguration? configuration)
    {
        // Serialize ordered fields, never profile identity or dictionary insertion order.
        object Source(SourceImportConfiguration source) => new
        {
            source.Delimiter, source.NumberFormat, source.DateFormat,
            Columns = source.Columns.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new { pair.Key, Value = ImportConfiguration.NormalizeHeader(pair.Value) }).ToArray()
        };
        var settings = configuration is null ? "legacy-csv-v1" : JsonSerializer.Serialize(new
        {
            configuration.Version, Sales = Source(configuration.Sales), Statement = Source(configuration.Statement)
        });
        return "sha256-v1:" + Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            Sales = Convert.ToHexString(SHA256.HashData(sales)),
            Statement = Convert.ToHexString(SHA256.HashData(statement)), Settings = settings
        })));
    }
}
