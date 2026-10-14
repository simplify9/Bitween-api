using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SW.Bitween.PgSql.Migrations
{
    /// <summary>
    /// Published outbox rows are deleted now rather than marked and kept a day (finding 4a of
    /// docs/design/processing-performance.md), so the dispatcher's index covers unpublished rows
    /// only, and each node reports what it published on its heartbeat. XchangeDeliveries goes: it
    /// was never written.
    /// </summary>
    public partial class OutboxDeleteOnPublish : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "xchange_delivery",
                schema: "infolink");

            migrationBuilder.DropIndex(
                name: "ix_outbox_message_published_on",
                schema: "infolink",
                table: "outbox_message");

            migrationBuilder.AddColumn<int>(
                name: "outbox_published_last_hour",
                schema: "infolink",
                table: "cluster_node",
                type: "integer",
                nullable: true);

            // Last, and outside the transaction: see ConcurrentIndex. The table can hold a day of
            // published rows on a busy deployment when this runs.
            ConcurrentIndex.Create(migrationBuilder, "outbox_message", "ix_outbox_message_created_on",
                "(created_on) WHERE published_on IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ConcurrentIndex.Drop(migrationBuilder, "ix_outbox_message_created_on");

            migrationBuilder.DropColumn(
                name: "outbox_published_last_hour",
                schema: "infolink",
                table: "cluster_node");

            migrationBuilder.CreateTable(
                name: "xchange_delivery",
                schema: "infolink",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    delivered_on = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_xchange_delivery", x => x.id);
                    table.ForeignKey(
                        name: "fk_xchange_delivery_xchange_id",
                        column: x => x.id,
                        principalSchema: "infolink",
                        principalTable: "xchange",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_message_published_on",
                schema: "infolink",
                table: "outbox_message",
                column: "published_on");

            migrationBuilder.CreateIndex(
                name: "ix_xchange_delivery_delivered_on",
                schema: "infolink",
                table: "xchange_delivery",
                column: "delivered_on");
        }
    }
}
