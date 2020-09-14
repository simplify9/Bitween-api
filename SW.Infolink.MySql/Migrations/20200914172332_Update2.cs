using Microsoft.EntityFrameworkCore.Migrations;

namespace SW.Infolink.MySql.Migrations
{
    public partial class Update2 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "OutputBad",
                table: "XchangeResults",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ResponseBad",
                table: "XchangeResults",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_Xchanges_RetryFor",
                table: "Xchanges",
                column: "RetryFor");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Xchanges_RetryFor",
                table: "Xchanges");

            migrationBuilder.DropColumn(
                name: "OutputBad",
                table: "XchangeResults");

            migrationBuilder.DropColumn(
                name: "ResponseBad",
                table: "XchangeResults");
        }
    }
}
