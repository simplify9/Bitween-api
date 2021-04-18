using Microsoft.EntityFrameworkCore.Migrations;

namespace SW.Infolink.MySql.Migrations
{
    public partial class update6 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PropertiesRaw",
                table: "XchangePromotedProperties",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_XchangePromotedProperties_PropertiesRaw",
                table: "XchangePromotedProperties",
                column: "PropertiesRaw");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_XchangePromotedProperties_PropertiesRaw",
                table: "XchangePromotedProperties");

            migrationBuilder.DropColumn(
                name: "PropertiesRaw",
                table: "XchangePromotedProperties");
        }
    }
}
