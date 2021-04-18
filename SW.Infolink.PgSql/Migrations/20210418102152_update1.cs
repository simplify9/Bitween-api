using Microsoft.EntityFrameworkCore.Migrations;

namespace SW.Infolink.PgSql.Migrations
{
    public partial class update1 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "properties_raw",
                schema: "infolink",
                table: "xchange_promoted_properties",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_xchange_promoted_properties_properties_raw",
                schema: "infolink",
                table: "xchange_promoted_properties",
                column: "properties_raw");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_xchange_promoted_properties_properties_raw",
                schema: "infolink",
                table: "xchange_promoted_properties");

            migrationBuilder.DropColumn(
                name: "properties_raw",
                schema: "infolink",
                table: "xchange_promoted_properties");
        }
    }
}
