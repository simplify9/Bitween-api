using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SW.Bitween.PgSql.Migrations
{
    /// <inheritdoc />
    public partial class XchangeState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "retried",
                schema: "infolink",
                table: "xchange",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "status",
                schema: "infolink",
                table: "xchange",
                type: "smallint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "xchange_state_fill",
                schema: "infolink",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    phase = table.Column<int>(type: "integer", nullable: false),
                    fill_began = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    position_started_on = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    position_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    updated_on = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_xchange_state_fill", x => x.id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "xchange_state_fill",
                schema: "infolink");

            migrationBuilder.DropColumn(
                name: "retried",
                schema: "infolink",
                table: "xchange");

            migrationBuilder.DropColumn(
                name: "status",
                schema: "infolink",
                table: "xchange");
        }
    }
}
