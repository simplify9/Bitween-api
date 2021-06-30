using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

namespace SW.Infolink.PgSql.Migrations
{
    public partial class update3 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "xchange_notification",
                schema: "infolink",
                columns: table => new
                {
                    id = table.Column<int>(nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    xchange_id = table.Column<string>(unicode: false, maxLength: 50, nullable: true),
                    success = table.Column<bool>(nullable: false),
                    notifier_id = table.Column<int>(nullable: false),
                    notifier_name = table.Column<string>(nullable: true),
                    exception = table.Column<string>(nullable: true),
                    finished_on = table.Column<DateTime>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_xchange_notification", x => x.id);
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "xchange_notification",
                schema: "infolink");
        }
    }
}
