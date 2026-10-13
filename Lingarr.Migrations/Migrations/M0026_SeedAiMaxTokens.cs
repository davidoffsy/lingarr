using FluentMigrator;

namespace Lingarr.Migrations.Migrations;

[Migration(26)]
public class M0026_SeedAiMaxTokens : Migration
{
    public override void Up()
    {
        Insert.IntoTable("settings").Row(new { key = "ai_max_tokens", value = "8192" });
    }

    public override void Down()
    {
        Delete.FromTable("settings").Row(new { key = "ai_max_tokens" });
    }
}
