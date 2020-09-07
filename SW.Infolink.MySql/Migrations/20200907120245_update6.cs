using Microsoft.EntityFrameworkCore.Migrations;

namespace SW.Infolink.MySql.Migrations
{
    public partial class update6 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ReceiveConsecutiveFailures",
                table: "Subscribers",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ReceiveLastException",
                table: "Subscribers",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReceiveConsecutiveFailures",
                table: "Subscribers");

            migrationBuilder.DropColumn(
                name: "ReceiveLastException",
                table: "Subscribers");
        }
    }
}
