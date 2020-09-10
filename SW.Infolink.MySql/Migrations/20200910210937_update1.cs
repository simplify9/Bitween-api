using Microsoft.EntityFrameworkCore.Migrations;

namespace SW.Infolink.MySql.Migrations
{
    public partial class update1 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RetryFor",
                table: "Xchanges",
                unicode: false,
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Hits",
                table: "XchangePromotedProperties",
                unicode: false,
                maxLength: 2000,
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RetryFor",
                table: "Xchanges");

            migrationBuilder.DropColumn(
                name: "Hits",
                table: "XchangePromotedProperties");
        }
    }
}
