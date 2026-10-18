using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SW.Bitween.PgSql.Migrations
{
    /// <inheritdoc />
    public partial class DropPromotedValuesIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // IF EXISTS: before this migration, dropping the index by hand was the way to stop long
            // values failing to save, and on a database where that was done a plain drop failed and
            // the app could not start.
            migrationBuilder.Sql("DROP INDEX IF EXISTS infolink.ix_xchange_promoted_properties_properties_raw;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_xchange_promoted_properties_properties_raw",
                schema: "infolink",
                table: "xchange_promoted_properties",
                column: "properties_raw");
        }
    }
}
