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
            migrationBuilder.DropIndex(
                name: "ix_xchange_promoted_properties_properties_raw",
                schema: "infolink",
                table: "xchange_promoted_properties");
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
