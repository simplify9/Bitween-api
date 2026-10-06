using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SW.Bitween.MySql.Migrations
{
    /// <inheritdoc />
    public partial class ResponseSourceValues : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SourceValues",
                table: "Xchanges",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "SourceXchangeId",
                table: "Xchanges",
                type: "varchar(50)",
                unicode: false,
                maxLength: 50,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "SourceValues",
                table: "OnHoldXchanges",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "SourceXchangeId",
                table: "OnHoldXchanges",
                type: "varchar(50)",
                unicode: false,
                maxLength: 50,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SourceValues",
                table: "Xchanges");

            migrationBuilder.DropColumn(
                name: "SourceXchangeId",
                table: "Xchanges");

            migrationBuilder.DropColumn(
                name: "SourceValues",
                table: "OnHoldXchanges");

            migrationBuilder.DropColumn(
                name: "SourceXchangeId",
                table: "OnHoldXchanges");
        }
    }
}
