using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SW.Bitween.PgSql.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentValidationSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "validation_schema",
                schema: "infolink",
                table: "document",
                type: "text",
                nullable: true);

            migrationBuilder.UpdateData(
                schema: "infolink",
                table: "document",
                keyColumn: "id",
                keyValue: 10001,
                column: "validation_schema",
                value: null);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "validation_schema",
                schema: "infolink",
                table: "document");
        }
    }
}
