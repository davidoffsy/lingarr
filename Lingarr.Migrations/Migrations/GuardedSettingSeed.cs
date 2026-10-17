using System.Data;
using MySqlConnector;

namespace Lingarr.Migrations.Migrations;

/// <summary>
/// Inserts settings rows that do not exist yet. <c>settings.key</c> is the primary key, so a plain
/// insert aborts the whole migration (and therefore startup) as soon as one of the keys already
/// exists, which happens on databases that were edited by hand, restored from a backup or written
/// by an older build. Seeding is therefore guarded per key and never overwrites an existing value.
/// </summary>
internal static class GuardedSettingSeed
{
    public static void InsertMissing(
        IDbConnection connection,
        IDbTransaction transaction,
        IReadOnlyDictionary<string, string> defaults)
    {
        foreach (var (key, value) in defaults)
        {
            if (SettingExists(connection, transaction, key))
            {
                continue;
            }

            InsertSetting(connection, transaction, key, value);
        }
    }

    /// <summary>
    /// Deletes settings rows that still hold <paramref name="expectedValues"/>. Seed
    /// <c>Down</c> methods use this so a key that already existed, or was later edited,
    /// is not removed: only rows left at the seeded default (the ones this migration
    /// inserted and nobody changed) are deleted.
    /// </summary>
    public static void DeleteIfCurrentValue(
        IDbConnection connection,
        IDbTransaction transaction,
        IReadOnlyDictionary<string, string> expectedValues)
    {
        foreach (var (key, value) in expectedValues)
        {
            using var command = CreateCommand(
                connection,
                transaction,
                $"DELETE FROM settings WHERE {Quote(connection, "key")} = @key AND {Quote(connection, "value")} = @value");
            AddParameter(command, "@key", key);
            AddParameter(command, "@value", value);
            command.ExecuteNonQuery();
        }
    }

    private static bool SettingExists(IDbConnection connection, IDbTransaction transaction, string key)
    {
        using var command = CreateCommand(
            connection,
            transaction,
            $"SELECT COUNT(1) FROM settings WHERE {Quote(connection, "key")} = @key");
        AddParameter(command, "@key", key);
        return Convert.ToInt64(command.ExecuteScalar() ?? 0L) > 0;
    }

    private static void InsertSetting(
        IDbConnection connection,
        IDbTransaction transaction,
        string key,
        string value)
    {
        using var command = CreateCommand(
            connection,
            transaction,
            $"INSERT INTO settings ({Quote(connection, "key")}, {Quote(connection, "value")}) VALUES (@key, @value)");
        AddParameter(command, "@key", key);
        AddParameter(command, "@value", value);
        command.ExecuteNonQuery();
    }

    private static IDbCommand CreateCommand(IDbConnection connection, IDbTransaction transaction, string commandText)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = commandText;
        return command;
    }

    private static void AddParameter(IDbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    /// <summary>
    /// Quotes an identifier for the connected provider: key and value are keywords on some providers,
    /// so they cannot be used bare in raw SQL. MySQL uses backticks, SQLite and PostgreSQL double quotes.
    /// </summary>
    private static string Quote(IDbConnection connection, string identifier) =>
        connection is MySqlConnection ? $"`{identifier}`" : $"\"{identifier}\"";
}