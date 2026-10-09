using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SW.Bitween.PgSql.Migrations
{
    /// <inheritdoc />
    public partial class AdapterSourceAccess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "adapter_source_access",
                schema: "infolink",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    adapter_id = table.Column<string>(type: "character varying(200)", unicode: false, maxLength: 200, nullable: false),
                    version = table.Column<string>(type: "character varying(50)", unicode: false, maxLength: 50, nullable: true),
                    path = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    account_id = table.Column<string>(type: "character varying(100)", unicode: false, maxLength: 100, nullable: true),
                    occurred_on = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_adapter_source_access", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_adapter_source_access_adapter_id_occurred_on",
                schema: "infolink",
                table: "adapter_source_access",
                columns: new[] { "adapter_id", "occurred_on" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "adapter_source_access",
                schema: "infolink");
        }
    }
}
