using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SW.Bitween.PgSql.Migrations
{
    /// <inheritdoc />
    public partial class AddBackgroundRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "background_request",
                schema: "infolink",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    owner = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    path = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    page = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    node = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    started_on = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    finished_on = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    done = table.Column<int>(type: "integer", nullable: true),
                    total = table.Column<int>(type: "integer", nullable: true),
                    summary = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    result_status = table.Column<int>(type: "integer", nullable: true),
                    result_content_type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    result_disposition = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    result = table.Column<byte[]>(type: "bytea", nullable: true),
                    result_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_background_request", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_background_request_owner_started_on",
                schema: "infolink",
                table: "background_request",
                columns: new[] { "owner", "started_on" });

            migrationBuilder.CreateIndex(
                name: "ix_background_request_started_on",
                schema: "infolink",
                table: "background_request",
                column: "started_on");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "background_request",
                schema: "infolink");
        }
    }
}
