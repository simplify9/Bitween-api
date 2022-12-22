using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SW.Infolink.MySql.Migrations
{
    public partial class update16 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "DisregardsUnfilteredMessages",
                table: "Documents",
                type: "tinyint(1)",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DisregardsUnfilteredMessages",
                table: "Documents");
        }
    }
}
