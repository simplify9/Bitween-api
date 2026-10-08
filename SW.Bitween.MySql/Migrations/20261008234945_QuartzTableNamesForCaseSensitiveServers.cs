using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SW.Bitween.MySql.Migrations
{
    /// <summary>
    /// Gives the Quartz tables the names Quartz looks for, on a server where the case matters.
    /// </summary>
    /// <remarks>
    /// QuartzAndAutoRetry created them as SW.Scheduler's EF model names them — <c>QRTZ_job_details</c>
    /// — while its Quartz store queries <c>QRTZ_JOB_DETAILS</c>. MySQL on Linux compares table names
    /// case-sensitively by default (lower_case_table_names = 0), so the scheduler refused to start
    /// there. On a server that ignores case the names already match, and renaming a table to what
    /// it already is called would fail, so only a case-sensitive server renames, and only a table
    /// still under the old name. Bitween reaches these tables through Quartz alone, never through
    /// the EF model, so the model keeps SW.Scheduler's names.
    /// </remarks>
    public partial class QuartzTableNamesForCaseSensitiveServers : Migration
    {
        private static readonly string[] Tables =
        [
            "job_details", "triggers", "simple_triggers", "simprop_triggers", "cron_triggers",
            "blob_triggers", "calendars", "paused_trigger_grps", "fired_triggers", "scheduler_state", "locks",
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var table in Tables)
                Rename(migrationBuilder, $"QRTZ_{table}", $"QRTZ_{table.ToUpperInvariant()}");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var table in Tables)
                Rename(migrationBuilder, $"QRTZ_{table.ToUpperInvariant()}", $"QRTZ_{table}");
        }

        private static void Rename(MigrationBuilder migrationBuilder, string from, string to) =>
            migrationBuilder.Sql($"""
                SET @rename = IF(@@lower_case_table_names = 0 AND EXISTS (
                        SELECT 1 FROM information_schema.tables
                        WHERE table_schema = DATABASE() AND BINARY table_name = '{from}'),
                    'RENAME TABLE `{from}` TO `{to}`', 'DO 0');
                PREPARE rename_statement FROM @rename;
                EXECUTE rename_statement;
                DEALLOCATE PREPARE rename_statement;
                """);
    }
}
