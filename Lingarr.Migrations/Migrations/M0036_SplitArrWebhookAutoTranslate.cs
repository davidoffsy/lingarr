using FluentMigrator;

namespace Lingarr.Migrations.Migrations;

[Migration(36)]
public class M0036_SplitArrWebhookAutoTranslate : Migration
{
    public override void Up()
    {
        Insert.IntoTable("settings").Row(new { key = "radarr_webhook_auto_translate", value = "true" });
        Insert.IntoTable("settings").Row(new { key = "sonarr_webhook_auto_translate", value = "true" });

        IfDatabase("sqlite", "postgresql").Execute.Sql("""
            UPDATE settings SET "value" =
                (SELECT source."value" FROM settings source WHERE source."key" = 'arr_webhook_auto_translate')
            WHERE "key" IN ('radarr_webhook_auto_translate', 'sonarr_webhook_auto_translate')
              AND EXISTS (SELECT 1 FROM settings source WHERE source."key" = 'arr_webhook_auto_translate')
            """);

        IfDatabase("mysql").Execute.Sql("""
            UPDATE settings target
            JOIN settings source ON source.`key` = 'arr_webhook_auto_translate'
            SET target.`value` = source.`value`
            WHERE target.`key` IN ('radarr_webhook_auto_translate', 'sonarr_webhook_auto_translate')
            """);

        Delete.FromTable("settings").Row(new { key = "arr_webhook_auto_translate" });
    }

    public override void Down()
    {
        Insert.IntoTable("settings").Row(new { key = "arr_webhook_auto_translate", value = "true" });

        IfDatabase("sqlite", "postgresql").Execute.Sql("""
            UPDATE settings SET "value" =
                (SELECT source."value" FROM settings source WHERE source."key" = 'radarr_webhook_auto_translate')
            WHERE "key" = 'arr_webhook_auto_translate'
              AND EXISTS (SELECT 1 FROM settings source WHERE source."key" = 'radarr_webhook_auto_translate')
            """);

        IfDatabase("mysql").Execute.Sql("""
            UPDATE settings target
            JOIN settings source ON source.`key` = 'radarr_webhook_auto_translate'
            SET target.`value` = source.`value`
            WHERE target.`key` = 'arr_webhook_auto_translate'
            """);

        Delete.FromTable("settings").Row(new { key = "radarr_webhook_auto_translate" });
        Delete.FromTable("settings").Row(new { key = "sonarr_webhook_auto_translate" });
    }
}
