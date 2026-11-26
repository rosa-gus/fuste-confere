using System.Text.Json;
using Fuste.Domain.Imports;

namespace Fuste.Infrastructure.Persistence;

internal static class ImportConfigurationStorage
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Serialize(ImportConfiguration configuration)
    {
        var errors = ImportConfiguration.Validate(configuration);
        if (errors.Count > 0) throw new ArgumentException(string.Join("\n", errors));
        return JsonSerializer.Serialize(configuration, Options);
    }

    public static ImportConfiguration Deserialize(string value)
    {
        try
        {
            var configuration = JsonSerializer.Deserialize<ImportConfiguration>(value, Options);
            if (configuration is null || ImportConfiguration.Validate(configuration).Count > 0)
                throw new InvalidDataException("Stored import configuration is invalid or unsupported.");
            return configuration;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Stored import configuration is invalid.", exception);
        }
    }
}
