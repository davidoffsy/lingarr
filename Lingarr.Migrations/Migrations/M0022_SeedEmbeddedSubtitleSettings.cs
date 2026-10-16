using FluentMigrator;

namespace Lingarr.Migrations.Migrations;

[Migration(22)]
public class M0022_SeedEmbeddedSubtitleSettings : Migration
{
    public override void Up()
    {
        Insert.IntoTable("settings").Row(new { key = "scan_embedded_subtitles", value = "true" });
        Insert.IntoTable("settings").Row(new { key = "extract_embedded_source", value = "true" });
    }

    public override void Down()
    {
        Delete.FromTable("settings").Row(new { key = "scan_embedded_subtitles" });
        Delete.FromTable("settings").Row(new { key = "extract_embedded_source" });
    }
}
