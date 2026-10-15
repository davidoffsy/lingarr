using FluentMigrator;

namespace Lingarr.Migrations.Migrations;

[Migration(30)]
public class M0030_SeedArrWebhookAutoTranslate : Migration
{
    public override void Up()
    {
        Insert.IntoTable("settings").Row(new { key = "arr_webhook_auto_translate", value = "true" });
    }

    public override void Down()
    {
        Delete.FromTable("settings").Row(new { key = "arr_webhook_auto_translate" });
    }
}