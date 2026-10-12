using FluentMigrator;

namespace Lingarr.Migrations.Migrations;

[Migration(23)]
public class M0023_SeedExtractTimeoutMinutes : Migration
{
    public override void Up()
    {
        Insert.IntoTable("settings").Row(new { key = "extract_timeout_minutes", value = "60" });
    }

    public override void Down()
    {
        Delete.FromTable("settings").Row(new { key = "extract_timeout_minutes" });
    }
}
