using Microsoft.EntityFrameworkCore.Migrations;

namespace SW.Infolink.MsSql.Migrations
{
    public partial class update7 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "BadData",
                table: "OnHoldXchanges",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Data",
                table: "OnHoldXchanges",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FileName",
                table: "OnHoldXchanges",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BadData",
                table: "OnHoldXchanges");

            migrationBuilder.DropColumn(
                name: "Data",
                table: "OnHoldXchanges");

            migrationBuilder.DropColumn(
                name: "FileName",
                table: "OnHoldXchanges");
        }
    }
}
