using FluentMigrator;

namespace Lingarr.Migrations.Migrations;

/// <summary>
/// Widens <c>translation_request_lines.model</c> from VARCHAR(100) to TEXT on MySQL and
/// PostgreSQL. SQLite already uses TEXT affinity for string columns and cannot ALTER a
/// column in place. Fresh installs get TEXT from <see cref="M0032_TranslationSourceAttribution"/>.
/// </summary>
[Migration(35)]
public class M0035_WidenTranslationRequestLineModel : Migration
{
    public override void Up()
    {
        if (!Schema.Table("translation_request_lines").Column("model").Exists())
        {
            return;
        }

        IfDatabase("mysql", "postgres", "postgresql")
            .Alter.Column("model")
            .OnTable("translation_request_lines")
            .AsCustom("TEXT")
            .Nullable();
    }

    public override void Down()
    {
        // Widening is not reversed: shrinking back to VARCHAR(100) would truncate stored ids.
    }
}
