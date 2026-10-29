using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SW.Bitween.PgSql.Migrations
{
    /// <inheritdoc />
    public partial class DuplicateArrivals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "drop_duplicates",
                schema: "infolink",
                table: "document",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "duplicate_arrival",
                schema: "infolink",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    document_id = table.Column<int>(type: "integer", nullable: false),
                    partner_id = table.Column<int>(type: "integer", nullable: true),
                    original_xchange_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    dropped = table.Column<bool>(type: "boolean", nullable: false),
                    occurred_on = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_duplicate_arrival", x => x.id);
                });

            migrationBuilder.UpdateData(
                schema: "infolink",
                table: "document",
                keyColumn: "id",
                keyValue: 10001,
                column: "drop_duplicates",
                value: false);

            migrationBuilder.CreateIndex(
                name: "ix_duplicate_arrival_document_id_occurred_on",
                schema: "infolink",
                table: "duplicate_arrival",
                columns: new[] { "document_id", "occurred_on" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "duplicate_arrival",
                schema: "infolink");

            migrationBuilder.DropColumn(
                name: "drop_duplicates",
                schema: "infolink",
                table: "document");
        }
    }
}
