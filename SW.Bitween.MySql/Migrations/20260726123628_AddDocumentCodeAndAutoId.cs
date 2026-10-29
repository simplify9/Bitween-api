using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SW.Bitween.MySql.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentCodeAndAutoId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // MySQL 8 refuses to modify a column other tables' foreign keys reference ("Cannot
            // change column 'Id': used in a foreign key constraint") while the checks are on, so
            // this stopped every MySQL install, new or upgrading. Making the key auto-increment
            // changes no value any foreign key relies on; the checks are back on straight after.
            migrationBuilder.Sql("SET FOREIGN_KEY_CHECKS = 0;");
            migrationBuilder.AlterColumn<int>(
                name: "Id",
                table: "Documents",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int")
                .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn);
            migrationBuilder.Sql("SET FOREIGN_KEY_CHECKS = 1;");

            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "Documents",
                type: "varchar(50)",
                unicode: false,
                maxLength: 50,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.UpdateData(
                table: "Documents",
                keyColumn: "Id",
                keyValue: 10001,
                column: "Code",
                value: null);

            migrationBuilder.CreateIndex(
                name: "IX_Documents_Code",
                table: "Documents",
                column: "Code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Documents_Code",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "Code",
                table: "Documents");

            migrationBuilder.AlterColumn<int>(
                name: "Id",
                table: "Documents",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int")
                .OldAnnotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn);
        }
    }
}
