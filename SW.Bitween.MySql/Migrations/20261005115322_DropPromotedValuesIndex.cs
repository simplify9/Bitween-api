using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SW.Bitween.MySql.Migrations
{
    /// <inheritdoc />
    public partial class DropPromotedValuesIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Only if it is there: before this migration, dropping the index by hand was the way to
            // stop long values failing to save, and on a database where that was done a plain drop
            // failed and the app could not start. MySQL has no DROP INDEX IF EXISTS.
            migrationBuilder.Sql("""
                SET @drop = IF(EXISTS (
                        SELECT 1 FROM information_schema.statistics
                        WHERE table_schema = DATABASE() AND table_name = 'XchangePromotedProperties'
                            AND index_name = 'IX_XchangePromotedProperties_PropertiesRaw'),
                    'ALTER TABLE `XchangePromotedProperties` DROP INDEX `IX_XchangePromotedProperties_PropertiesRaw`', 'DO 0');
                PREPARE drop_statement FROM @drop;
                EXECUTE drop_statement;
                DEALLOCATE PREPARE drop_statement;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "PropertiesRaw",
                table: "XchangePromotedProperties",
                type: "longtext",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "varchar(255)",
                oldNullable: true)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "PropertiesRaw",
                table: "XchangePromotedProperties",
                type: "varchar(255)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "longtext",
                oldNullable: true)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_XchangePromotedProperties_PropertiesRaw",
                table: "XchangePromotedProperties",
                column: "PropertiesRaw");
        }
    }
}
