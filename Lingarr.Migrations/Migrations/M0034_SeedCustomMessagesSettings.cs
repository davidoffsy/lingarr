using FluentMigrator;

namespace Lingarr.Migrations.Migrations;

/// <summary>
/// Seeds the Anthropic Messages request template and version header of the custom provider.
/// Existing chat and Responses templates are left untouched, and an already stored version is
/// never overwritten.
/// </summary>
[Migration(34)]
public class M0034_SeedCustomMessagesSettings : Migration
{
    public override void Up()
    {
        Execute.WithConnection((connection, transaction) =>
            GuardedSettingSeed.InsertMissing(connection, transaction, new Dictionary<string, string>
            {
                ["custom_messages_request_template"] = string.Empty,
                // The standard Messages version header. Only the anthropic_messages protocol sends it.
                ["custom_anthropic_version"] = "2023-06-01"
            }));
    }

    public override void Down()
    {
        Execute.WithConnection((connection, transaction) =>
            GuardedSettingSeed.DeleteIfCurrentValue(connection, transaction, new Dictionary<string, string>
            {
                ["custom_messages_request_template"] = string.Empty,
                ["custom_anthropic_version"] = "2023-06-01"
            }));
    }
}
