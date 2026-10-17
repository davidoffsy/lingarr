using FluentMigrator;

namespace Lingarr.Migrations.Migrations;

/// <summary>
/// Records the model that produced each translated line, and adds the per-source line statistics that
/// the multi-model fallback chain reports.
/// </summary>
[Migration(32)]
public class M0032_TranslationSourceAttribution : Migration
{
    public override void Up()
    {
        if (!Schema.Table("translation_request_lines").Column("model").Exists())
        {
            // TEXT rather than VARCHAR(100): gateway model ids (HuggingFace repos,
            // fine-tuned OpenAI ids, Azure deployment names) routinely exceed 100 chars.
            // Installs that already applied a VARCHAR(100) column are widened by M0035.
            Alter.Table("translation_request_lines")
                .AddColumn("model")
                .AsCustom("TEXT")
                .Nullable();
        }

        if (!Schema.Table("statistics").Column("lines_by_source_json").Exists())
        {
            // Added nullable on purpose: MySQL rejects a default value on TEXT columns and SQLite
            // cannot alter a column afterwards. Existing rows are backfilled, and the entity treats a
            // missing value like an empty statistic.
            Alter.Table("statistics")
                .AddColumn("lines_by_source_json")
                .AsCustom("TEXT")
                .Nullable();

            Execute.Sql("UPDATE statistics SET lines_by_source_json = '{}' WHERE lines_by_source_json IS NULL");
        }
    }

    public override void Down()
    {
        if (Schema.Table("translation_request_lines").Column("model").Exists())
        {
            Delete.Column("model").FromTable("translation_request_lines");
        }

        if (Schema.Table("statistics").Column("lines_by_source_json").Exists())
        {
            Delete.Column("lines_by_source_json").FromTable("statistics");
        }
    }
}
