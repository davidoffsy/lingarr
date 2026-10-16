using FluentMigrator;

namespace Lingarr.Migrations.Migrations;

[Migration(31)]
public class M0031_SeedCustomAiSettings : Migration
{
    public override void Up()
    {
        Execute.WithConnection((connection, transaction) =>
            GuardedSettingSeed.InsertMissing(connection, transaction, new Dictionary<string, string>
            {
                ["custom_endpoint"] = string.Empty,
                ["custom_model"] = string.Empty,
                ["custom_api_key"] = string.Empty,
                ["custom_chat_request_template"] = string.Empty
            }));
    }

    public override void Down()
    {
        Delete.FromTable("settings").Row(new { key = "custom_endpoint" });
        Delete.FromTable("settings").Row(new { key = "custom_model" });
        Delete.FromTable("settings").Row(new { key = "custom_api_key" });
        Delete.FromTable("settings").Row(new { key = "custom_chat_request_template" });
    }
}
