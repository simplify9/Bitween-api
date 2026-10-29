using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SW.Bitween.MsSql.Migrations
{
    /// <summary>
    /// Published outbox rows are deleted now rather than marked and kept a day so the dispatcher's index covers unpublished rows
    /// only, and each node reports what it published on its heartbeat. XchangeDeliveries goes: it
    /// was never written.
    /// </summary>
    public partial class OutboxDeleteOnPublish : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "XchangeDeliveries");

            migrationBuilder.DropIndex(
                name: "IX_OutboxMessages_PublishedOn",
                table: "OutboxMessages");

            migrationBuilder.AddColumn<int>(
                name: "OutboxPublishedLastHour",
                table: "ClusterNodes",
                type: "int",
                nullable: true);

            // Online where the edition allows: see OnlineIndex.
            OnlineIndex.Create(migrationBuilder, "OutboxMessages", "IX_OutboxMessages_CreatedOn",
                "([CreatedOn]) WHERE [PublishedOn] IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OutboxMessages_CreatedOn",
                table: "OutboxMessages");

            migrationBuilder.DropColumn(
                name: "OutboxPublishedLastHour",
                table: "ClusterNodes");

            migrationBuilder.CreateTable(
                name: "XchangeDeliveries",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    DeliveredOn = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_XchangeDeliveries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_XchangeDeliveries_Xchanges_Id",
                        column: x => x.Id,
                        principalTable: "Xchanges",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_PublishedOn",
                table: "OutboxMessages",
                column: "PublishedOn");

            migrationBuilder.CreateIndex(
                name: "IX_XchangeDeliveries_DeliveredOn",
                table: "XchangeDeliveries",
                column: "DeliveredOn");
        }
    }
}
