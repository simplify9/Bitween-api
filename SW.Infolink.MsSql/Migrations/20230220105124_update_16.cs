using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SW.Infolink.MsSql.Migrations
{
    public partial class update_16 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Role",
                table: "Accounts",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Role",
                table: "Accounts");
        }
    }
}
