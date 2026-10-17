using FluentMigrator;

namespace Lingarr.Migrations.Migrations;

/// <summary>
/// Seeds the optional trailing ideographic-period strip. Off by default so existing
/// installs keep writing translated cues unchanged.
/// </summary>
[Migration(38)]
public class M0038_SeedStripTrailingIdeographicFullStop : Migration
{
    public override void Up()
    {
        Execute.WithConnection((connection, transaction) =>
            GuardedSettingSeed.InsertMissing(connection, transaction, new Dictionary<string, string>
            {
                ["strip_trailing_ideographic_full_stop"] = "false"
            }));
    }

    public override void Down()
    {
        Execute.WithConnection((connection, transaction) =>
            GuardedSettingSeed.DeleteIfCurrentValue(connection, transaction, new Dictionary<string, string>
            {
                ["strip_trailing_ideographic_full_stop"] = "false"
            }));
    }
}
