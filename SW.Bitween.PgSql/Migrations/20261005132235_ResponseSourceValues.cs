using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SW.Bitween.PgSql.Migrations
{
    /// <inheritdoc />
    public partial class ResponseSourceValues : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<IReadOnlyDictionary<string, string>>(
                name: "source_values",
                schema: "infolink",
                table: "xchange",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "source_xchange_id",
                schema: "infolink",
                table: "xchange",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<IReadOnlyDictionary<string, string>>(
                name: "source_values",
                schema: "infolink",
                table: "on_hold_xchange",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "source_xchange_id",
                schema: "infolink",
                table: "on_hold_xchange",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "source_values",
                schema: "infolink",
                table: "xchange");

            migrationBuilder.DropColumn(
                name: "source_xchange_id",
                schema: "infolink",
                table: "xchange");

            migrationBuilder.DropColumn(
                name: "source_values",
                schema: "infolink",
                table: "on_hold_xchange");

            migrationBuilder.DropColumn(
                name: "source_xchange_id",
                schema: "infolink",
                table: "on_hold_xchange");
        }
    }
}
