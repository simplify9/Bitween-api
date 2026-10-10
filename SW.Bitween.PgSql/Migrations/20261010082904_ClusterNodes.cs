using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SW.Bitween.PgSql.Migrations
{
    /// <inheritdoc />
    public partial class ClusterNodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cluster_node",
                schema: "infolink",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    host = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    started_on = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_seen_on = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    data_sources = table.Column<bool>(type: "boolean", nullable: false),
                    runtimes = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cluster_node", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_cluster_node_last_seen_on",
                schema: "infolink",
                table: "cluster_node",
                column: "last_seen_on");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cluster_node",
                schema: "infolink");
        }
    }
}
