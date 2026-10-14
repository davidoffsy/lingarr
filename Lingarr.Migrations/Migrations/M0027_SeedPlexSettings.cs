using FluentMigrator;

namespace Lingarr.Migrations.Migrations;

[Migration(27)]
public class M0027_SeedPlexSettings : Migration
{
    public override void Up()
    {
        Insert.IntoTable("settings").Row(new { key = "plex_url", value = "" });
        Insert.IntoTable("settings").Row(new { key = "plex_token", value = "" });
        Insert.IntoTable("settings").Row(new { key = "plex_client_identifier", value = "" });
        Insert.IntoTable("settings").Row(new { key = "plex_machine_id", value = "" });
        Insert.IntoTable("settings").Row(new { key = "plex_account_title", value = "" });
        Insert.IntoTable("settings").Row(new { key = "plex_webhook_secret", value = "" });
        Insert.IntoTable("settings").Row(new { key = "plex_min_rating", value = "10" });
        Insert.IntoTable("settings").Row(new { key = "plex_allowed_raters", value = "home" });
        Insert.IntoTable("settings").Row(new { key = "plex_settings_completed", value = "false" });
    }

    public override void Down()
    {
        Delete.FromTable("settings").Row(new { key = "plex_url" });
        Delete.FromTable("settings").Row(new { key = "plex_token" });
        Delete.FromTable("settings").Row(new { key = "plex_client_identifier" });
        Delete.FromTable("settings").Row(new { key = "plex_machine_id" });
        Delete.FromTable("settings").Row(new { key = "plex_account_title" });
        Delete.FromTable("settings").Row(new { key = "plex_webhook_secret" });
        Delete.FromTable("settings").Row(new { key = "plex_min_rating" });
        Delete.FromTable("settings").Row(new { key = "plex_allowed_raters" });
        Delete.FromTable("settings").Row(new { key = "plex_settings_completed" });
    }
}
