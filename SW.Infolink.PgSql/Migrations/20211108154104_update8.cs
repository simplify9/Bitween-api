using Microsoft.EntityFrameworkCore.Migrations;

namespace SW.Infolink.PgSql.Migrations
{
    public partial class update8 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_running",
                schema: "infolink",
                table: "subscription",
                nullable: false,
                defaultValue: false);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "is_running",
                schema: "infolink",
                table: "subscription");
        }
    }
}
