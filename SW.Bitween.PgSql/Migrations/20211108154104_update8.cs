using Microsoft.EntityFrameworkCore.Migrations;

namespace SW.Bitween.PgSql.Migrations
{
    public partial class update8 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_running",
                schema: "Bitween",
                table: "subscription",
                nullable: false,
                defaultValue: false);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "is_running",
                schema: "Bitween",
                table: "subscription");
        }
    }
}
