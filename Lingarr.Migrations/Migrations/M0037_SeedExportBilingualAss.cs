using FluentMigrator;

namespace Lingarr.Migrations.Migrations;

/// <summary>
/// Seeds the optional bilingual ASS sidecar toggle. Off by default so existing
/// installs keep writing only the monolingual target file.
/// </summary>
[Migration(37)]
public class M0037_SeedExportBilingualAss : Migration
{
    public override void Up()
    {
        Execute.WithConnection((connection, transaction) =>
            GuardedSettingSeed.InsertMissing(connection, transaction, new Dictionary<string, string>
            {
                ["export_bilingual_ass"] = "false"
            }));
    }

    public override void Down()
    {
        Execute.WithConnection((connection, transaction) =>
            GuardedSettingSeed.DeleteIfCurrentValue(connection, transaction, new Dictionary<string, string>
            {
                ["export_bilingual_ass"] = "false"
            }));
    }
}
