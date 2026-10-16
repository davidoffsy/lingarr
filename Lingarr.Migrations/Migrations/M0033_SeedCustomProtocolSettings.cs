using FluentMigrator;

namespace Lingarr.Migrations.Migrations;

/// <summary>
/// Seeds the protocol selection and the Responses request template of the custom provider. Existing
/// databases keep whatever <c>custom_chat_request_template</c> they already edited: this migration
/// only adds the new keys, never rewrites old ones.
/// </summary>
[Migration(33)]
public class M0033_SeedCustomProtocolSettings : Migration
{
    public override void Up()
    {
        Execute.WithConnection((connection, transaction) =>
            GuardedSettingSeed.InsertMissing(connection, transaction, new Dictionary<string, string>
            {
                // The explicit default keeps the pre-Responses behaviour: existing installations keep
                // talking Chat Completions even though the protocol is now a stored, visible setting.
                ["custom_protocol"] = "openai_chat",
                ["custom_responses_request_template"] = string.Empty
            }));
    }

    public override void Down()
    {
        Delete.FromTable("settings").Row(new { key = "custom_protocol" });
        Delete.FromTable("settings").Row(new { key = "custom_responses_request_template" });
    }
}