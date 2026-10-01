using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SW.Bitween.PgSql.Migrations
{
    /// <inheritdoc />
    public partial class PromotedPropertiesInOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "promoted_properties",
                schema: "infolink",
                table: "document",
                type: "json",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "jsonb",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "promoted_properties",
                schema: "infolink",
                table: "document",
                type: "jsonb",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "json",
                oldNullable: true);
        }
    }
}
