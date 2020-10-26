using Microsoft.EntityFrameworkCore.Migrations;

namespace SW.Infolink.MsSql.Migrations
{
    public partial class Update1 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ResponseMessageTypeName",
                table: "Xchanges",
                unicode: false,
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResponseMessageTypeName",
                table: "Subscriptions",
                unicode: false,
                maxLength: 500,
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ResponseMessageTypeName",
                table: "Xchanges");

            migrationBuilder.DropColumn(
                name: "ResponseMessageTypeName",
                table: "Subscriptions");
        }
    }
}
