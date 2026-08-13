using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SW.Bitween.PgSql.Migrations
{
    /// <inheritdoc />
    public partial class SharedRetryGroupTotals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "group_attempt_counts",
                schema: "infolink",
                table: "xchange");

            migrationBuilder.DropColumn(
                name: "group_attempt_counts",
                schema: "infolink",
                table: "delayed_retry");

            migrationBuilder.CreateTable(
                name: "retry_group_usage",
                schema: "infolink",
                columns: table => new
                {
                    subscription_id = table.Column<int>(type: "integer", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    attempts_used = table.Column<int>(type: "integer", nullable: false),
                    last_attempt_on = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_retry_group_usage", x => new { x.subscription_id, x.group_id });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "retry_group_usage",
                schema: "infolink");

            migrationBuilder.AddColumn<Dictionary<string, int>>(
                name: "group_attempt_counts",
                schema: "infolink",
                table: "xchange",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<Dictionary<string, int>>(
                name: "group_attempt_counts",
                schema: "infolink",
                table: "delayed_retry",
                type: "jsonb",
                nullable: true);
        }
    }
}
