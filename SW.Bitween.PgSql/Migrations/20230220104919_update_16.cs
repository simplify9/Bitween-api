using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SW.Bitween.PgSql.Migrations
{
    public partial class update_16 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "role",
                schema: "Bitween",
                table: "Accounts",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "role",
                schema: "Bitween",
                table: "Accounts");
        }
    }
}
