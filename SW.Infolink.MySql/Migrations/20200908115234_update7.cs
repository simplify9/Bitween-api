using Microsoft.EntityFrameworkCore.Migrations;

namespace SW.Infolink.MySql.Migrations
{
    public partial class update7 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ValidatorId",
                table: "Subscribers",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ValidatorProperties",
                table: "Subscribers",
                unicode: false,
                maxLength: 200,
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ValidatorId",
                table: "Subscribers");

            migrationBuilder.DropColumn(
                name: "ValidatorProperties",
                table: "Subscribers");
        }
    }
}
