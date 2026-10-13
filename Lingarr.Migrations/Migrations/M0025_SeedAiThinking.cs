using FluentMigrator;

namespace Lingarr.Migrations.Migrations;

[Migration(25)]
public class M0025_SeedAiThinking : Migration
{
    public override void Up()
    {
        Insert.IntoTable("settings").Row(new { key = "ai_thinking", value = "off" });
    }

    public override void Down()
    {
        Delete.FromTable("settings").Row(new { key = "ai_thinking" });
    }
}
