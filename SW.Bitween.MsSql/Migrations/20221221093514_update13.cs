using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SW.Bitween.MsSql.Migrations
{
    public partial class update13 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "DisregardsUnfilteredMessages",
                table: "Documents",
                type: "bit",
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
