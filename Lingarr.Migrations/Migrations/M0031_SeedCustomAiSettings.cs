using System.Data;
using FluentMigrator;
using MySqlConnector;

namespace Lingarr.Migrations.Migrations;

[Migration(31)]
public class M0031_SeedCustomAiSettings : Migration
{
    /// <summary>Keys of the custom AI provider settings seeded by this migration.</summary>
    private static readonly string[] Keys =
    [
        "custom_endpoint",
        "custom_model",
        "custom_api_key",
        "custom_chat_request_template"
    ];

    public override void Up()
    {
        // settings.key is the primary key, so a plain insert aborts the whole migration (and therefore
        // startup) as soon as one of the keys already exists, which happens on databases that were
        // edited by hand, restored from a backup or written by an older build. Seeding is therefore
        // guarded per key and never overwrites an existing value.
        Execute.WithConnection((connection, transaction) =>
        {
            foreach (var key in Keys)
            {
                if (SettingExists(connection, transaction, key))
                {
                    continue;
                }

                InsertSetting(connection, transaction, key);
            }
        });
    }

    public override void Down()
    {
        Delete.FromTable("settings").Row(new { key = "custom_endpoint" });
        Delete.FromTable("settings").Row(new { key = "custom_model" });
        Delete.FromTable("settings").Row(new { key = "custom_api_key" });
        Delete.FromTable("settings").Row(new { key = "custom_chat_request_template" });
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

    private static void InsertSetting(IDbConnection connection, IDbTransaction transaction, string key)
    {
        using var command = CreateCommand(
            connection,
            transaction,
            $"INSERT INTO settings ({Quote(connection, "key")}, {Quote(connection, "value")}) VALUES (@key, @value)");
        AddParameter(command, "@key", key);
        AddParameter(command, "@value", string.Empty);
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
