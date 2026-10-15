using FluentMigrator;

namespace Lingarr.Migrations.Migrations;

[Migration(29)]
public class M0029_ResetAiRequestDefaults : Migration
{
    public override void Up()
    {
        Update.Table("settings")
            .Set(new { value = "auto" })
            .Where(new { key = "ai_thinking", value = "off" });

        Update.Table("settings")
            .Set(new { value = "0" })
            .Where(new { key = "ai_max_tokens", value = "8192" });
    }

    public override void Down()
    {
        Update.Table("settings")
            .Set(new { value = "off" })
            .Where(new { key = "ai_thinking", value = "auto" });

        Update.Table("settings")
            .Set(new { value = "8192" })
            .Where(new { key = "ai_max_tokens", value = "0" });
    }
}
