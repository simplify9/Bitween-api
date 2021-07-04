using Microsoft.EntityFrameworkCore.Migrations;

namespace SW.Infolink.PgSql.Migrations
{
    public partial class update5 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "bad_data",
                schema: "infolink",
                table: "on_hold_xchange",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "data",
                schema: "infolink",
                table: "on_hold_xchange",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "file_name",
                schema: "infolink",
                table: "on_hold_xchange",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "bad_data",
                schema: "infolink",
                table: "on_hold_xchange");

            migrationBuilder.DropColumn(
                name: "data",
                schema: "infolink",
                table: "on_hold_xchange");

            migrationBuilder.DropColumn(
                name: "file_name",
                schema: "infolink",
                table: "on_hold_xchange");
        }
    }
}
