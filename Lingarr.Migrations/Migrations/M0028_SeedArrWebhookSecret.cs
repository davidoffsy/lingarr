using FluentMigrator;

namespace Lingarr.Migrations.Migrations;

[Migration(28)]
public class M0028_SeedArrWebhookSecret : Migration
{
    public override void Up()
    {
        Insert.IntoTable("settings").Row(new { key = "arr_webhook_secret", value = "" });
    }

    public override void Down()
    {
        Delete.FromTable("settings").Row(new { key = "arr_webhook_secret" });
    }
}
