using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SW.Bitween.PgSql.Migrations
{
    /// <inheritdoc />
    public partial class AdapterDrafts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "adapter_draft",
                schema: "infolink",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    adapter_id = table.Column<string>(type: "character varying(200)", unicode: false, maxLength: 200, nullable: false),
                    language = table.Column<string>(type: "character varying(20)", unicode: false, maxLength: 20, nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", unicode: false, maxLength: 20, nullable: true),
                    base_version = table.Column<string>(type: "character varying(50)", unicode: false, maxLength: 50, nullable: true),
                    files_json = table.Column<string>(type: "text", nullable: false),
                    files_hash = table.Column<string>(type: "character varying(64)", unicode: false, maxLength: 64, nullable: false),
                    created_on = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    modified_on = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_adapter_draft", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "adapter_release",
                schema: "infolink",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    adapter_id = table.Column<string>(type: "character varying(200)", unicode: false, maxLength: 200, nullable: false),
                    version = table.Column<string>(type: "character varying(50)", unicode: false, maxLength: 50, nullable: false),
                    action = table.Column<string>(type: "character varying(20)", unicode: false, maxLength: 20, nullable: false),
                    draft_id = table.Column<int>(type: "integer", nullable: true),
                    account_id = table.Column<string>(type: "character varying(100)", unicode: false, maxLength: 100, nullable: true),
                    occurred_on = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_adapter_release", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_adapter_draft_adapter_id",
                schema: "infolink",
                table: "adapter_draft",
                column: "adapter_id");

            migrationBuilder.CreateIndex(
                name: "ix_adapter_release_adapter_id_occurred_on",
                schema: "infolink",
                table: "adapter_release",
                columns: new[] { "adapter_id", "occurred_on" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "adapter_draft",
                schema: "infolink");

            migrationBuilder.DropTable(
                name: "adapter_release",
                schema: "infolink");
        }
    }
}
