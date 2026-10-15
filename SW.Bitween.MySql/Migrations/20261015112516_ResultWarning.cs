using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SW.Bitween.MySql.Migrations
{
    /// <inheritdoc />
    public partial class ResultWarning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Warning",
                table: "XchangeResults",
                type: "varchar(2000)",
                maxLength: 2000,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Warning",
                table: "XchangeResults");
        }
    }
}
