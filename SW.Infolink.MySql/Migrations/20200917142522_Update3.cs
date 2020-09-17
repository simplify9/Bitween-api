using Microsoft.EntityFrameworkCore.Migrations;

namespace SW.Infolink.MySql.Migrations
{
    public partial class Update3 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "InputContentType",
                table: "Xchanges",
                unicode: false,
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OutputContentType",
                table: "XchangeResults",
                unicode: false,
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResponseContentType",
                table: "XchangeResults",
                unicode: false,
                maxLength: 200,
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InputContentType",
                table: "Xchanges");

            migrationBuilder.DropColumn(
                name: "OutputContentType",
                table: "XchangeResults");

            migrationBuilder.DropColumn(
                name: "ResponseContentType",
                table: "XchangeResults");
        }
    }
}
