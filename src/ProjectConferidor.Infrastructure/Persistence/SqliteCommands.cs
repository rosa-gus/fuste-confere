using System.Globalization;
using Microsoft.Data.Sqlite;

namespace ProjectConferidor.Infrastructure.Persistence;

internal static class SqliteCommands
{
    public static SqliteCommand CreateCommand(
        this SqliteConnection connection,
        string sql,
        SqliteTransaction? transaction = null,
        params (string Name, object? Value)[] parameters)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }
        return command;
    }

    // Decimal strings preserve all .NET decimal digits, including legacy gross
    // amounts with more than two fractional digits. Never persist money as REAL.
    public static object DecimalValue(decimal? value) =>
        value is null ? DBNull.Value : value.Value.ToString(CultureInfo.InvariantCulture);

    public static decimal? ReadDecimal(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : decimal.Parse(reader.GetString(ordinal), CultureInfo.InvariantCulture);
}
