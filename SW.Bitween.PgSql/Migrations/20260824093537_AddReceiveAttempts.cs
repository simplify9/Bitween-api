using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SW.Bitween.PgSql.Migrations
{
    /// <inheritdoc />
    public partial class AddReceiveAttempts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "receive_attempt",
                schema: "infolink",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    subscription_id = table.Column<int>(type: "integer", nullable: false),
                    started_on = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    finished_on = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    outcome = table.Column<int>(type: "integer", nullable: false),
                    error_message = table.Column<string>(type: "text", nullable: true),
                    exchange_ids = table.Column<string[]>(type: "text[]", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_receive_attempt", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_receive_attempt_subscription_id_started_on",
                schema: "infolink",
                table: "receive_attempt",
                columns: new[] { "subscription_id", "started_on" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "receive_attempt",
                schema: "infolink");
        }
    }
}
