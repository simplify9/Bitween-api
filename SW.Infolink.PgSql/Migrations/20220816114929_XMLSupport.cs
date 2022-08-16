using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SW.Infolink.PgSql.Migrations
{
    public partial class XMLSupport : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "document_format",
                schema: "infolink",
                table: "document",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "document_format",
                schema: "infolink",
                table: "document");
        }
    }
}
