using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SW.Infolink.MsSql.Migrations
{
    public partial class XMLSupport : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DocumentFormat",
                table: "Documents",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DocumentFormat",
                table: "Documents");
        }
    }
}
