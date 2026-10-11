using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SW.Bitween.PgSql.Migrations
{
    /// <inheritdoc />
    public partial class Metrics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "duration_ms",
                schema: "infolink",
                table: "xchange_result",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "handler_version",
                schema: "infolink",
                table: "xchange_result",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "mapper_version",
                schema: "infolink",
                table: "xchange_result",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "node_name",
                schema: "infolink",
                table: "xchange_result",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "resident_health",
                schema: "infolink",
                table: "cluster_node",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "adapter_stats",
                schema: "infolink",
                columns: table => new
                {
                    hour = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    node = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    adapter_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    version = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    runtime = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    calls = table.Column<int>(type: "integer", nullable: false),
                    failures = table.Column<int>(type: "integer", nullable: false),
                    timeouts = table.Column<int>(type: "integer", nullable: false),
                    limit_stops = table.Column<int>(type: "integer", nullable: false),
                    crashes = table.Column<int>(type: "integer", nullable: false),
                    duration_sum_ms = table.Column<long>(type: "bigint", nullable: false),
                    duration_max_ms = table.Column<long>(type: "bigint", nullable: false),
                    d0 = table.Column<int>(type: "integer", nullable: false),
                    d1 = table.Column<int>(type: "integer", nullable: false),
                    d2 = table.Column<int>(type: "integer", nullable: false),
                    d3 = table.Column<int>(type: "integer", nullable: false),
                    d4 = table.Column<int>(type: "integer", nullable: false),
                    d5 = table.Column<int>(type: "integer", nullable: false),
                    d6 = table.Column<int>(type: "integer", nullable: false),
                    d7 = table.Column<int>(type: "integer", nullable: false),
                    d8 = table.Column<int>(type: "integer", nullable: false),
                    d9 = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_adapter_stats", x => new { x.hour, x.node, x.adapter_id, x.version, x.role, x.runtime });
                });

            migrationBuilder.CreateTable(
                name: "error_stats",
                schema: "infolink",
                columns: table => new
                {
                    hour = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    node = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    scope = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    scope_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    fingerprint = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    sample = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    count = table.Column<int>(type: "integer", nullable: false),
                    last_seen_on = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_error_stats", x => new { x.hour, x.node, x.scope, x.scope_key, x.fingerprint });
                });

            migrationBuilder.CreateTable(
                name: "exchange_stats",
                schema: "infolink",
                columns: table => new
                {
                    hour = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    node = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    subscription_id = table.Column<int>(type: "integer", nullable: false),
                    partner_id = table.Column<int>(type: "integer", nullable: false),
                    total = table.Column<int>(type: "integer", nullable: false),
                    succeeded = table.Column<int>(type: "integer", nullable: false),
                    bad_response = table.Column<int>(type: "integer", nullable: false),
                    failed = table.Column<int>(type: "integer", nullable: false),
                    failed_at_mapper = table.Column<int>(type: "integer", nullable: false),
                    failed_at_handler = table.Column<int>(type: "integer", nullable: false),
                    retries = table.Column<int>(type: "integer", nullable: false),
                    manual_retries = table.Column<int>(type: "integer", nullable: false),
                    bytes_in = table.Column<long>(type: "bigint", nullable: false),
                    bytes_out = table.Column<long>(type: "bigint", nullable: false),
                    duration_sum_ms = table.Column<long>(type: "bigint", nullable: false),
                    duration_max_ms = table.Column<long>(type: "bigint", nullable: false),
                    d0 = table.Column<int>(type: "integer", nullable: false),
                    d1 = table.Column<int>(type: "integer", nullable: false),
                    d2 = table.Column<int>(type: "integer", nullable: false),
                    d3 = table.Column<int>(type: "integer", nullable: false),
                    d4 = table.Column<int>(type: "integer", nullable: false),
                    d5 = table.Column<int>(type: "integer", nullable: false),
                    d6 = table.Column<int>(type: "integer", nullable: false),
                    d7 = table.Column<int>(type: "integer", nullable: false),
                    d8 = table.Column<int>(type: "integer", nullable: false),
                    d9 = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_exchange_stats", x => new { x.hour, x.node, x.subscription_id, x.partner_id });
                });

            migrationBuilder.CreateTable(
                name: "queue_snapshot",
                schema: "infolink",
                columns: table => new
                {
                    at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    queue_depth = table.Column<long>(type: "bigint", nullable: false),
                    retry_backlog = table.Column<long>(type: "bigint", nullable: false),
                    dead_letter_backlog = table.Column<long>(type: "bigint", nullable: false),
                    incoming_rate = table.Column<double>(type: "double precision", nullable: false),
                    ack_rate = table.Column<double>(type: "double precision", nullable: false),
                    consumers = table.Column<int>(type: "integer", nullable: false),
                    unhealthy_consumers = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_queue_snapshot", x => x.at);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "adapter_stats",
                schema: "infolink");

            migrationBuilder.DropTable(
                name: "error_stats",
                schema: "infolink");

            migrationBuilder.DropTable(
                name: "exchange_stats",
                schema: "infolink");

            migrationBuilder.DropTable(
                name: "queue_snapshot",
                schema: "infolink");

            migrationBuilder.DropColumn(
                name: "duration_ms",
                schema: "infolink",
                table: "xchange_result");

            migrationBuilder.DropColumn(
                name: "handler_version",
                schema: "infolink",
                table: "xchange_result");

            migrationBuilder.DropColumn(
                name: "mapper_version",
                schema: "infolink",
                table: "xchange_result");

            migrationBuilder.DropColumn(
                name: "node_name",
                schema: "infolink",
                table: "xchange_result");

            migrationBuilder.DropColumn(
                name: "resident_health",
                schema: "infolink",
                table: "cluster_node");
        }
    }
}
