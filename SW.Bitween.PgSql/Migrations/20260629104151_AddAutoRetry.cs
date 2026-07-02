using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using SW.Bitween.Domain;
using SW.Bitween.Model;

#nullable disable

namespace SW.Bitween.PgSql.Migrations
{
    /// <inheritdoc />
    public partial class AddAutoRetry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Dictionary<string, int>>(
                name: "group_attempt_counts",
                schema: "infolink",
                table: "xchange",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "custom_retry_policy",
                schema: "infolink",
                table: "subscription",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "retry_policy_id",
                schema: "infolink",
                table: "subscription",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "delayed_retry",
                schema: "infolink",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    on = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    group_attempt_counts = table.Column<Dictionary<string, int>>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_delayed_retry", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "retry_policy",
                schema: "infolink",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    groups = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_on = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    modified_on = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_retry_policy", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_subscription_retry_policy_id",
                schema: "infolink",
                table: "subscription",
                column: "retry_policy_id");

            migrationBuilder.CreateIndex(
                name: "ix_delayed_retry_on",
                schema: "infolink",
                table: "delayed_retry",
                column: "on");

            migrationBuilder.AddForeignKey(
                name: "fk_subscription_retry_policy_retry_policy_id",
                schema: "infolink",
                table: "subscription",
                column: "retry_policy_id",
                principalSchema: "infolink",
                principalTable: "retry_policy",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_subscription_retry_policy_retry_policy_id",
                schema: "infolink",
                table: "subscription");

            migrationBuilder.DropTable(
                name: "delayed_retry",
                schema: "infolink");

            migrationBuilder.DropTable(
                name: "retry_policy",
                schema: "infolink");

            migrationBuilder.DropIndex(
                name: "ix_subscription_retry_policy_id",
                schema: "infolink",
                table: "subscription");

            migrationBuilder.DropColumn(
                name: "group_attempt_counts",
                schema: "infolink",
                table: "xchange");

            migrationBuilder.DropColumn(
                name: "custom_retry_policy",
                schema: "infolink",
                table: "subscription");

            migrationBuilder.DropColumn(
                name: "retry_policy_id",
                schema: "infolink",
                table: "subscription");
        }
    }
}
