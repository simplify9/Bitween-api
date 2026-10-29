using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SW.Bitween.PgSql.Migrations
{
    /// <inheritdoc />
    public partial class ExpectTraffic : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "expect_traffic",
                schema: "infolink",
                table: "subscription",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "quiet_since",
                schema: "infolink",
                table: "subscription",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "expect_traffic",
                schema: "infolink",
                table: "subscription");

            migrationBuilder.DropColumn(
                name: "quiet_since",
                schema: "infolink",
                table: "subscription");
        }
    }
}
