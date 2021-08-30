using Microsoft.EntityFrameworkCore.Migrations;

namespace SW.Infolink.PgSql.Migrations
{
    public partial class update6 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "correlation_id",
                schema: "infolink",
                table: "xchange",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "correlation_id",
                schema: "infolink",
                table: "xchange");
        }
    }
}
