using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

namespace SW.Infolink.PgSql.Migrations
{
    public partial class update2 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "notifier",
                schema: "infolink",
                columns: table => new
                {
                    id = table.Column<int>(nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(maxLength: 100, nullable: false),
                    run_on_successful_result = table.Column<bool>(nullable: false),
                    run_on_bad_result = table.Column<bool>(nullable: false),
                    run_on_failed_result = table.Column<bool>(nullable: false),
                    handler_id = table.Column<string>(unicode: false, maxLength: 200, nullable: true),
                    inactive = table.Column<bool>(nullable: false),
                    handler_properties = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notifier", x => x.id);
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "notifier",
                schema: "infolink");
        }
    }
}
