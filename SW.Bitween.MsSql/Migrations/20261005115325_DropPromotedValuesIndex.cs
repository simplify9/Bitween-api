using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SW.Bitween.MsSql.Migrations
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
            migrationBuilder.Sql(
                "DROP INDEX IF EXISTS [IX_XchangePromotedProperties_PropertiesRaw] ON [XchangePromotedProperties];");

            migrationBuilder.AlterColumn<string>(
                name: "PropertiesRaw",
                table: "XchangePromotedProperties",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "PropertiesRaw",
                table: "XchangePromotedProperties",
                type: "nvarchar(450)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_XchangePromotedProperties_PropertiesRaw",
                table: "XchangePromotedProperties",
                column: "PropertiesRaw");
        }
    }
}
