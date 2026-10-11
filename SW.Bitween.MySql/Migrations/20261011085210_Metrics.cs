using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SW.Bitween.MySql.Migrations
{
    /// <inheritdoc />
    public partial class Metrics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DurationMs",
                table: "XchangeResults",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HandlerVersion",
                table: "XchangeResults",
                type: "varchar(50)",
                unicode: false,
                maxLength: 50,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "MapperVersion",
                table: "XchangeResults",
                type: "varchar(50)",
                unicode: false,
                maxLength: 50,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "NodeName",
                table: "XchangeResults",
                type: "varchar(100)",
                unicode: false,
                maxLength: 100,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "ResidentHealth",
                table: "ClusterNodes",
                type: "varchar(4000)",
                maxLength: 4000,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "AdapterStats",
                columns: table => new
                {
                    Hour = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Node = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AdapterId = table.Column<string>(type: "varchar(200)", unicode: false, maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Version = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Role = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Runtime = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Calls = table.Column<int>(type: "int", nullable: false),
                    Failures = table.Column<int>(type: "int", nullable: false),
                    Timeouts = table.Column<int>(type: "int", nullable: false),
                    LimitStops = table.Column<int>(type: "int", nullable: false),
                    Crashes = table.Column<int>(type: "int", nullable: false),
                    DurationSumMs = table.Column<long>(type: "bigint", nullable: false),
                    DurationMaxMs = table.Column<long>(type: "bigint", nullable: false),
                    D0 = table.Column<int>(type: "int", nullable: false),
                    D1 = table.Column<int>(type: "int", nullable: false),
                    D2 = table.Column<int>(type: "int", nullable: false),
                    D3 = table.Column<int>(type: "int", nullable: false),
                    D4 = table.Column<int>(type: "int", nullable: false),
                    D5 = table.Column<int>(type: "int", nullable: false),
                    D6 = table.Column<int>(type: "int", nullable: false),
                    D7 = table.Column<int>(type: "int", nullable: false),
                    D8 = table.Column<int>(type: "int", nullable: false),
                    D9 = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdapterStats", x => new { x.Hour, x.Node, x.AdapterId, x.Version, x.Role, x.Runtime });
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ErrorStats",
                columns: table => new
                {
                    Hour = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Node = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Scope = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ScopeKey = table.Column<string>(type: "varchar(200)", unicode: false, maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Fingerprint = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Sample = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Count = table.Column<int>(type: "int", nullable: false),
                    LastSeenOn = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ErrorStats", x => new { x.Hour, x.Node, x.Scope, x.ScopeKey, x.Fingerprint });
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ExchangeStats",
                columns: table => new
                {
                    Hour = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Node = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SubscriptionId = table.Column<int>(type: "int", nullable: false),
                    PartnerId = table.Column<int>(type: "int", nullable: false),
                    Total = table.Column<int>(type: "int", nullable: false),
                    Succeeded = table.Column<int>(type: "int", nullable: false),
                    BadResponse = table.Column<int>(type: "int", nullable: false),
                    Failed = table.Column<int>(type: "int", nullable: false),
                    FailedAtMapper = table.Column<int>(type: "int", nullable: false),
                    FailedAtHandler = table.Column<int>(type: "int", nullable: false),
                    Retries = table.Column<int>(type: "int", nullable: false),
                    ManualRetries = table.Column<int>(type: "int", nullable: false),
                    BytesIn = table.Column<long>(type: "bigint", nullable: false),
                    BytesOut = table.Column<long>(type: "bigint", nullable: false),
                    DurationSumMs = table.Column<long>(type: "bigint", nullable: false),
                    DurationMaxMs = table.Column<long>(type: "bigint", nullable: false),
                    D0 = table.Column<int>(type: "int", nullable: false),
                    D1 = table.Column<int>(type: "int", nullable: false),
                    D2 = table.Column<int>(type: "int", nullable: false),
                    D3 = table.Column<int>(type: "int", nullable: false),
                    D4 = table.Column<int>(type: "int", nullable: false),
                    D5 = table.Column<int>(type: "int", nullable: false),
                    D6 = table.Column<int>(type: "int", nullable: false),
                    D7 = table.Column<int>(type: "int", nullable: false),
                    D8 = table.Column<int>(type: "int", nullable: false),
                    D9 = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExchangeStats", x => new { x.Hour, x.Node, x.SubscriptionId, x.PartnerId });
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "QueueSnapshots",
                columns: table => new
                {
                    At = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    QueueDepth = table.Column<long>(type: "bigint", nullable: false),
                    RetryBacklog = table.Column<long>(type: "bigint", nullable: false),
                    DeadLetterBacklog = table.Column<long>(type: "bigint", nullable: false),
                    IncomingRate = table.Column<double>(type: "double", nullable: false),
                    AckRate = table.Column<double>(type: "double", nullable: false),
                    Consumers = table.Column<int>(type: "int", nullable: false),
                    UnhealthyConsumers = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QueueSnapshots", x => x.At);
                })
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdapterStats");

            migrationBuilder.DropTable(
                name: "ErrorStats");

            migrationBuilder.DropTable(
                name: "ExchangeStats");

            migrationBuilder.DropTable(
                name: "QueueSnapshots");

            migrationBuilder.DropColumn(
                name: "DurationMs",
                table: "XchangeResults");

            migrationBuilder.DropColumn(
                name: "HandlerVersion",
                table: "XchangeResults");

            migrationBuilder.DropColumn(
                name: "MapperVersion",
                table: "XchangeResults");

            migrationBuilder.DropColumn(
                name: "NodeName",
                table: "XchangeResults");

            migrationBuilder.DropColumn(
                name: "ResidentHealth",
                table: "ClusterNodes");
        }
    }
}
